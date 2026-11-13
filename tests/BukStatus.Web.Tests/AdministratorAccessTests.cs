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
    public AdministratorAccessTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(connection).Options;
        using var db = new BukStatusDbContext(options);
        db.Database.Migrate();
        store = new(new Factory(options));
    }
    public void Dispose() => connection.Dispose();
    private AdministratorAccess Access(int person, string authType = "oidc") => new(
        new HttpContextAccessor(), new UserState(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(BukClaims.PersonId, person.ToString()), new Claim(ClaimTypes.Role, "SuperAdmin")], authType))),
        store, Options.Create(new ClubAccessOptions { Enabled = true, ApiPersonId = 21410 }), Options.Create(new BukAuthOptions()));
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
