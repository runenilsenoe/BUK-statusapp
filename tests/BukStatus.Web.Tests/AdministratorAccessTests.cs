using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using BukStatus.Core.Administration;
using BukStatus.Core.Groups;
using BukStatus.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Tests;

public sealed class AdministratorAccessTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AdministratorStore store;
    private readonly AccessRequestService requests;
    public AdministratorAccessTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(connection).Options;
        using var db = new BukStatusDbContext(options);
        db.Database.Migrate();
        store = new(new Factory(options));
        requests = new(new Factory(options));
    }
    public void Dispose() => connection.Dispose();
    private AdministratorAccess Access(int person, string authType = "oidc") => new(
        new HttpContextAccessor(), new UserState(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(BukClaims.PersonId, person.ToString()), new Claim(ClaimTypes.Role, "SuperAdmin")], authType))),
        store, requests, Options.Create(new ClubAccessOptions { Enabled = true, ApiPersonId = 21410 }), Options.Create(new BukAuthOptions()));
    [Fact]
    public async Task Role_claim_cannot_grant_admin_and_revocation_applies_immediately()
    {
        var user = Access(12345);
        Assert.False(await user.CanEditAsync());
        await Assert.ThrowsAsync<GroupException>(() => user.RequireAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => user.AddAsync(54321, null));
        await Access(21410).AddAsync(12345, null);
        Assert.True(await user.CanEditAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => user.GetAllAsync());
        await Access(21410).RemoveAsync(12345);
        Assert.False(await user.CanEditAsync());
    }
    [Fact]
    public async Task Machine_key_cannot_manage_roles_even_when_bound_to_superadmin()
    {
        var machine = Access(21410, ApiKeyAuthenticationHandler.SchemeName);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => machine.AddAsync(12345, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => machine.GetAllAsync());
    }
    [Fact]
    public async Task Diagnostics_deny_other_users_before_any_network_request()
    {
        var network = new ProbeNetwork();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BccApi:ClientId"] = "client", ["BccApi:ClientSecret"] = "secret" }).Build();
        var service = new BukStatus.Web.Diagnostics.BccDiagnostics(Access(12345), network, config);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunAsync());
        Assert.Equal(0, network.Calls);
    }
    [Fact]
    public async Task Diagnostics_report_denied_scopes_without_leaking_upstream_secrets_and_continue()
    {
        var network = new ProbeNetwork();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BccApi:ClientId"] = "client", ["BccApi:ClientSecret"] = "secret" }).Build();
        var service = new BukStatus.Web.Diagnostics.BccDiagnostics(Access(21410), network, config);
        var results = await service.RunAsync();
        Assert.Contains(results, r => r.Result == "Token avvist" && r.HttpStatus == 403);
        Assert.Contains(results, r => r.Name == "Team og tilgang" && r.Count == 1 && r.Summary.Contains("Syntetisk team"));
        Assert.Contains(results, r => r.Name == "Klubb–organisasjon-kobling" && r.Result == "API avvist" && r.HttpStatus == 401);
        Assert.DoesNotContain("TOP_SECRET", System.Text.Json.JsonSerializer.Serialize(results));
    }
    [Theory]
    [InlineData(12345, "oidc")]
    [InlineData(21410, ApiKeyAuthenticationHandler.SchemeName)]
    public async Task Samvirk_diagnostics_require_superadmin_browser_identity(int person, string authentication)
    {
        var network = new SamvirkNetwork();
        var tokens = new ProbeUserToken();
        var service = new BukStatus.Web.Diagnostics.SamvirkDiagnostics(Access(person, authentication), network, ProbeConfig(), tokens);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunAsync());
        Assert.Empty(network.TokenForms);
        Assert.Empty(network.ApiTokens);
        Assert.Equal(0, tokens.Calls);
    }

    [Fact]
    public async Task Samvirk_tests_existing_tokens_and_separate_audience_without_exposing_values()
    {
        var network = new SamvirkNetwork();
        var service = new BukStatus.Web.Diagnostics.SamvirkDiagnostics(Access(21410), network, ProbeConfig(), new ProbeUserToken());
        var results = await service.RunAsync();
        Assert.Equal(3, results.Count);
        Assert.Equal(401, results[0].HttpStatus);
        Assert.Equal(401, results[1].HttpStatus);
        Assert.Equal("OK – Samvirk-data tilgjengelig", results[2].Result);
        Assert.Contains("samvirkCoin", results[2].Summary);
        Assert.Contains("ikke en komplett medlemsliste", results[2].Summary);
        Assert.Equal(new[] { "TOP_SECRET_CONTRIBUTIONS", "TOP_SECRET_USER", "TOP_SECRET_AKSJON" }, network.ApiTokens);
        Assert.Equal(2, network.TokenForms.Count);
        Assert.Contains("scope=contributions#read", network.TokenForms[0]);
        Assert.Contains("audience=aksjon.bcc.no", network.TokenForms[1]);
        Assert.DoesNotContain("scope=", network.TokenForms[1]);
        var output = System.Text.Json.JsonSerializer.Serialize(results);
        Assert.DoesNotContain("TOP_SECRET", output);
        Assert.DoesNotContain("999999", output);
        Assert.DoesNotContain("SensitiveName", output);
    }

    [Theory]
    [InlineData(204, "", "OK – ingen personstatus")]
    [InlineData(200, "<html>TOP_SECRET</html>", "Ukjent svarformat")]
    [InlineData(200, "{\"unexpected\":\"TOP_SECRET\"}", "Ukjent svarformat")]
    [InlineData(200, "{\"year\":2026,\"personId\":21410}", "Ukjent svarformat")]
    [InlineData(403, "TOP_SECRET", "API avvist")]
    public async Task Samvirk_distinguishes_missing_data_bad_format_and_denied_access(int status, string body, string expected)
    {
        var network = new SamvirkNetwork { Status = status, Body = body };
        var service = new BukStatus.Web.Diagnostics.SamvirkDiagnostics(Access(21410), network, ProbeConfig(), new ProbeUserToken());
        var results = await service.RunAsync();
        Assert.Equal(expected, results[2].Result);
        Assert.DoesNotContain("TOP_SECRET", System.Text.Json.JsonSerializer.Serialize(results));
    }

    [Fact]
    public async Task Missing_user_token_and_rejected_audience_do_not_stop_other_tests()
    {
        var network = new SamvirkNetwork { RejectAudience = true };
        var service = new BukStatus.Web.Diagnostics.SamvirkDiagnostics(Access(21410), network, ProbeConfig(), new ProbeUserToken { Missing = true });
        var results = await service.RunAsync();
        Assert.Equal(3, results.Count);
        Assert.Equal("API avvist", results[0].Result);
        Assert.Equal("Ikke kjørt", results[1].Result);
        Assert.Equal("Token avvist", results[2].Result);
        Assert.DoesNotContain("TOP_SECRET", System.Text.Json.JsonSerializer.Serialize(results));
    }

    static IConfiguration ProbeConfig() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BccApi:ClientId"] = "client", ["BccApi:ClientSecret"] = "secret" }).Build();

    private sealed class ProbeUserToken : BukStatus.Core.Clubs.IMyshareTokenProvider
    {
        public bool CanUse => true;
        public int Calls;
        public bool Missing;
        public Task<string> GetAsync(CancellationToken ct = default)
        {
            Calls++;
            if (Missing) throw new BukStatus.Core.Clubs.MyshareDirectoryException("TOP_SECRET");
            return Task.FromResult("TOP_SECRET_USER");
        }
    }
    private sealed class SamvirkNetwork : HttpMessageHandler, IHttpClientFactory
    {
        public readonly List<string> TokenForms = [];
        public readonly List<string> ApiTokens = [];
        public int Status = 200;
        public string Body = "[{\"personId\":999999,\"year\":2026,\"month\":11,\"samvirkCoin\":100,\"displayName\":\"SensitiveName\",\"TOP_SECRET\":\"TOP_SECRET\"}]";
        public bool RejectAudience;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("login.bcc.no", request.RequestUri!.Host);
                var form = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync(ct));
                TokenForms.Add(form);
                if (form.Contains("audience=aksjon.bcc.no") && RejectAudience) return Reply(403, "TOP_SECRET");
                return Reply(200, "{\"access_token\":\"" + (form.Contains("contributions#read") ? "TOP_SECRET_CONTRIBUTIONS" : "TOP_SECRET_AKSJON") + "\"}");
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(BukStatus.Web.Diagnostics.SamvirkDiagnostics.Endpoint, request.RequestUri!.AbsoluteUri);
            var token = request.Headers.Authorization!.Parameter!;
            ApiTokens.Add(token);
            return token == "TOP_SECRET_AKSJON" ? Reply(Status, Body) : Reply(401, "TOP_SECRET");
        }
        static HttpResponseMessage Reply(int code, string body) => new((System.Net.HttpStatusCode)code) { Content = new StringContent(body) };
    }

    private sealed class ProbeNetwork : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (request.Method == HttpMethod.Post)
            {
                var form = await request.Content!.ReadAsStringAsync(ct);
                return form.Contains("contributions") ? Reply(200, "{\"access_token\":\"TOP_SECRET\"}") : Reply(403, "{\"error_description\":\"TOP_SECRET\"}");
            }
            Assert.Equal("TOP_SECRET", request.Headers.Authorization?.Parameter);
            if (request.RequestUri!.AbsolutePath.EndsWith("TeamConfig"))
                return Reply(200, "{\"data\":[{\"teamID\":1,\"teamName\":\"Syntetisk team\",\"internalSecret\":\"TOP_SECRET\"}]}");
            return Reply(401, "{\"error\":\"TOP_SECRET\"}");
        }
        static HttpResponseMessage Reply(int code, string json) => new((System.Net.HttpStatusCode)code) { Content = new StringContent(json) };
    }
    private sealed class Factory(DbContextOptions<BukStatusDbContext> options) : IDbContextFactory<BukStatusDbContext>
    {
        public BukStatusDbContext CreateDbContext() => new(options);
    }
    private sealed class UserState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
