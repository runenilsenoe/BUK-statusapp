using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BukStatus.Web.Auth;

public sealed class MyshareTokenStore(IDbContextFactory<BukStatusDbContext> factory, IDataProtectionProvider protection,
    IHttpClientFactory clients, IOptions<BukAuthOptions> auth, IOptionsMonitor<OpenIdConnectOptions> oidc, TimeProvider time)
{
    private readonly IDataProtector protector = protection.CreateProtector("BUK.MyshareUserTokens.v1");
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gates = new();
    private sealed record Tokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
    static MyshareDirectoryException LoginNeeded() => new("MyShare-tilgangen må kobles til på nytt. Trykk «Koble til MyShare» øverst på siden.");
    void CheckAudience(string token)
    {
        try
        {
            var jwt = new JsonWebToken(token);
            if (!jwt.Audiences.Contains(auth.Value.MyshareAudience, StringComparer.Ordinal)) throw LoginNeeded();
        }
        catch (MyshareDirectoryException) { throw; }
        catch { throw LoginNeeded(); }
    }
    public async Task CaptureAsync(ClaimsPrincipal principal, AuthenticationProperties properties, CancellationToken ct = default)
    {
        var id = principal.PersonId(); var subject = principal.FindFirst("sub")?.Value;
        var accessToken = properties.GetTokenValue("access_token");
        if (principal.Identity?.IsAuthenticated != true || id is not > 0 || string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(accessToken)) throw LoginNeeded();
        CheckAudience(accessToken);
        if (!DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires)
            || expires <= time.GetUtcNow()) throw LoginNeeded();
        var session = new MyshareUserSession { Id = Guid.NewGuid(), PersonId = id.Value, Subject = subject,
            ExpiresAtUtc = time.GetUtcNow().AddDays(7).UtcDateTime,
            ProtectedTokens = protector.Protect(JsonSerializer.Serialize(new Tokens(accessToken, properties.GetTokenValue("refresh_token"), expires))) };
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        await db.MyshareUserSessions.Where(s => s.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);
        db.MyshareUserSessions.Add(session); await db.SaveChangesAsync(ct);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new(BukClaims.MyshareSession, session.Id.ToString()));
        // The browser only gets a signed session reference, never access/refresh tokens.
        properties.StoreTokens([]);
    }
    public async Task<string> GetAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var id = principal.PersonId(); var subject = principal.FindFirst("sub")?.Value;
        if (principal.Identity?.IsAuthenticated != true || id is not > 0 || string.IsNullOrEmpty(subject)
            || !Guid.TryParse(principal.FindFirst(BukClaims.MyshareSession)?.Value, out var sessionId)) throw LoginNeeded();
        var gate = gates.GetOrAdd(sessionId, _ => new(1,1));
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var session = await db.MyshareUserSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.PersonId == id && s.Subject == subject, ct);
            if (session is null || session.ExpiresAtUtc <= time.GetUtcNow().UtcDateTime) throw LoginNeeded();
            Tokens tokens;
            try { tokens = JsonSerializer.Deserialize<Tokens>(protector.Unprotect(session.ProtectedTokens))!; }
            catch { throw LoginNeeded(); }
            if (tokens is null) throw LoginNeeded();
            CheckAudience(tokens.AccessToken);
            if (tokens.ExpiresAt > time.GetUtcNow().AddMinutes(1)) return tokens.AccessToken;
            if (string.IsNullOrEmpty(tokens.RefreshToken)) throw LoginNeeded();
            var options = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
            var config = options.Configuration ?? (options.ConfigurationManager is null ? null : await options.ConfigurationManager.GetConfigurationAsync(ct));
            if (!Uri.TryCreate(config?.TokenEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https"
                || endpoint.GetLeftPart(UriPartial.Authority) != new Uri(auth.Value.Authority).GetLeftPart(UriPartial.Authority)) throw LoginNeeded();
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.RefreshToken,
                ["client_id"] = auth.Value.ClientId, ["client_secret"] = auth.Value.ClientSecret
            }) };
            using var response = await clients.CreateClient("MyshareTokenRefresh").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode is 400 or 401 or 403) { db.MyshareUserSessions.Remove(session); await db.SaveChangesAsync(ct); }
                throw LoginNeeded();
            }
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var token = body.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrEmpty(token)) throw LoginNeeded();
            CheckAudience(token);
            var seconds = body.RootElement.GetProperty("expires_in").GetInt32();
            if (seconds <= 0) throw LoginNeeded();
            var refresh = body.RootElement.TryGetProperty("refresh_token",out var rotated) ? rotated.GetString() : tokens.RefreshToken;
            session.ProtectedTokens = protector.Protect(JsonSerializer.Serialize(new Tokens(token, refresh, time.GetUtcNow().AddSeconds(seconds))));
            await db.SaveChangesAsync(ct);
            return token;
        }
        finally { gate.Release(); }
    }
    public async Task RevokeAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var personId = principal.PersonId(); var subject = principal.FindFirst("sub")?.Value;
        if (personId is not > 0 || string.IsNullOrEmpty(subject) || !Guid.TryParse(principal.FindFirst(BukClaims.MyshareSession)?.Value, out var session)) return;
        var gate = gates.GetOrAdd(session, _=>new(1,1)); await gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await db.MyshareUserSessions.Where(s => s.Id == session && s.PersonId == personId && s.Subject == subject).ExecuteDeleteAsync(ct);
        }
        finally { gate.Release(); }
    }
}
