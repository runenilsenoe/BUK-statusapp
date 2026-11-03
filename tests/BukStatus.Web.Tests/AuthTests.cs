using System.Net;
using System.Security.Claims;
using BukStatus.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BukStatus.Web.Tests;

public sealed class AuthTests : IDisposable
{
    private const string ApiKey = "test-api-key";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"bukstatus-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private WebApplicationFactory<Program> App(string environment, bool withOidc = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment(environment);
            host.UseSetting("ConnectionStrings:BukStatus", $"Data Source={_dbPath}");
            host.UseSetting("BccApi:UseFakeData", "true");
            host.UseSetting("Auth:ApiKeys:0", ApiKey);
            host.UseSetting("Auth:AllowedPersonIds:0", "10001");
            host.UseSetting("Auth:ClientId", withOidc ? "test-client" : "");
            host.UseSetting("Auth:ClientSecret", withOidc ? "test-secret" : "");
        });

    private static HttpClient Client(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Api_requires_a_valid_key()
    {
        using var app = App("Production");
        var client = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/groups")).StatusCode);

        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/groups")).StatusCode);

        client.DefaultRequestHeaders.Remove(ApiKeyAuthenticationHandler.HeaderName);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/groups")).StatusCode);
    }

    [Fact]
    public async Task Pages_send_anonymous_visitors_to_login()
    {
        using var app = App("Production");

        var response = await Client(app).GetAsync("/groups/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/account/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Uses_the_proxy_scheme_and_host_for_redirects()
    {
        using var app = App("Production");
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "status.example.no");

        var response = await Client(app).SendAsync(request);

        Assert.StartsWith("https://status.example.no/account/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Health_check_is_open()
    {
        using var app = App("Production");

        var response = await Client(app).GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Production_without_bcc_login_refuses_to_start()
    {
        using var app = App("Production", withOidc: false);

        var ex = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("Auth:ClientId", ex.ToString());
    }

    [Fact]
    public async Task Development_without_bcc_login_signs_in_a_local_developer()
    {
        using var app = App("Development", withOidc: false);

        var response = await Client(app).GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Utvikler (lokal)", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("10001", true)]
    [InlineData("12345", false)]
    [InlineData(null, false)]
    public async Task Only_allowed_person_ids_get_in(string? personId, bool allowed)
    {
        using var app = App("Production");
        var authorization = app.Services.GetRequiredService<IAuthorizationService>();
        var claims = personId is null ? [] : new[] { new Claim(BukClaims.PersonId, personId) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var result = await authorization.AuthorizeAsync(user, AuthSetup.Policy);

        Assert.Equal(allowed, result.Succeeded);
    }
}
