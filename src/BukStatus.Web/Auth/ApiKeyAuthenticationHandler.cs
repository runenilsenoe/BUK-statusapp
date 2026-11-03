using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Auth;

/// <summary>Authenticates /api callers that send one of the configured keys in the X-Api-Key header.</summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<BukAuthOptions> authOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var provided) || string.IsNullOrEmpty(provided))
            return Task.FromResult(AuthenticateResult.NoResult());

        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        var match = authOptions.CurrentValue.ApiKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Any(key => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), providedBytes));
        if (!match)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "API"), new Claim(BukClaims.Trusted, "true")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
