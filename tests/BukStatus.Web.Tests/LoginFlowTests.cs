using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using BukStatus.Core.Pot;
using BukStatus.Core.Teams;
using BukStatus.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BukStatus.Web.Tests;

/// <summary>
/// The login flow as a browser sees it: login cookies are made with the app's own keys, and BCC's OIDC metadata is
/// stubbed so the redirect to login.bcc.no can be inspected without network access.
/// </summary>
public sealed class LoginFlowTests : IDisposable
{
    private const string AllowedPersonId = "10001";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"bukstatus-login-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private string? _lastReturnUrl;

    public LoginFlowTests()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.UseSetting("ConnectionStrings:BukStatus", $"Data Source={_dbPath}");
            host.UseSetting("BccApi:UseFakeData", "true");
            host.UseSetting("Auth:AllowedPersonIds:0", AllowedPersonId);
            host.UseSetting("Auth:ClientId", "test-client");
            host.UseSetting("Auth:ClientSecret", "test-secret");
            host.ConfigureServices(services => services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, oidc =>
            {
                oidc.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = "https://login.bcc.no/",
                    AuthorizationEndpoint = "https://login.bcc.no/authorize",
                    TokenEndpoint = "https://login.bcc.no/oauth/token",
                };
                oidc.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc.Configuration);
                var redirect = oidc.Events.OnRedirectToIdentityProvider;
                oidc.Events.OnRedirectToIdentityProvider = context =>
                {
                    _lastReturnUrl = context.Properties.RedirectUri;
                    return redirect(context);
                };
            }));
        });
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    /// <summary>A login cookie as the OIDC handler would have issued it after a BCC login.</summary>
    private string LoginCookie(string? personId, string name = "Kari Test")
    {
        var options = _app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var claims = new List<Claim> { new("name", name), new("sub", $"bcc|{personId ?? "x"}") };
        if (personId is not null)
            claims.Add(new Claim(BukClaims.PersonId, personId));
        var identity = new ClaimsIdentity(claims, OpenIdConnectDefaults.AuthenticationScheme, "name", null);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), CookieAuthenticationDefaults.AuthenticationScheme);
        return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? cookie = null, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        return _client.SendAsync(request);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/pot")]
    [InlineData("/settings/pot")]
    public async Task Allowed_people_get_in(string path)
    {
        var response = await Send(HttpMethod.Get, path, LoginCookie(AllowedPersonId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Kari Test", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/pot")]
    [InlineData("/settings/pot")]
    public async Task People_not_on_the_list_are_sent_to_access_denied(string path)
    {
        var response = await Send(HttpMethod.Get, path, LoginCookie("55555"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/access-denied", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Api_answers_403_for_people_not_on_the_list()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/pot", LoginCookie("55555"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/pot", LoginCookie(AllowedPersonId))).StatusCode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("55555", true)]
    public async Task Blazor_connection_is_closed_to_anyone_not_allowed(string? personId, bool signedIn)
    {
        var cookie = signedIn ? LoginCookie(personId) : null;

        var response = await Send(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", cookie);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1", LoginCookie(AllowedPersonId))).StatusCode);
    }

    [Fact]
    public async Task Access_denied_shows_the_person_id_to_pass_on()
    {
        var html = await (await Send(HttpMethod.Get, "/access-denied", LoginCookie("55555"))).Content.ReadAsStringAsync();

        Assert.Contains("55555", html);
    }

    [Fact]
    public async Task Access_denied_explains_a_login_without_person_id()
    {
        var html = await (await Send(HttpMethod.Get, "/access-denied", LoginCookie(null))).Content.ReadAsStringAsync();

        Assert.Contains("ingen person-ID", html);
    }

    [Fact]
    public async Task Login_sends_people_to_bcc_with_pkce_and_the_public_https_callback()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/account/login?ReturnUrl=%2Fpot");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "status.example.no");

        var response = await _client.SendAsync(request);

        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://login.bcc.no/authorize?", location);
        Assert.Contains("redirect_uri=https%3A%2F%2Fstatus.example.no%2Fsignin-auth0", location);
        Assert.Contains("response_type=code", location);
        Assert.Contains("code_challenge_method=S256", location);
        Assert.Equal("/pot", _lastReturnUrl);
        // The correlation and nonce cookies must survive BCC's cross-site form post back to us.
        Assert.All(response.Headers.GetValues("Set-Cookie"), cookie =>
        {
            Assert.Contains("secure", cookie);
            Assert.Contains("samesite=none", cookie);
        });
    }

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("/\\evil.example")]
    public async Task Login_only_returns_to_pages_on_this_site(string returnUrl)
    {
        await Send(HttpMethod.Get, "/account/login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal("/", _lastReturnUrl);
    }

    [Fact]
    public async Task Logout_requires_the_form_token_and_then_ends_the_bcc_session()
    {
        var cookie = LoginCookie(AllowedPersonId);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/account/logout", cookie)).StatusCode);

        // Take the token from the logout form, the way the browser would.
        var page = await Send(HttpMethod.Get, "/", cookie);
        var token = Regex.Match(await page.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var antiforgeryCookie = page.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith(".AspNetCore.Antiforgery"));

        var response = await Send(HttpMethod.Post, "/account/logout", $"{cookie}; {antiforgeryCookie}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://login.bcc.no/v2/logout?client_id=test-client&returnTo=", response.Headers.Location!.ToString());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("bukstatus.auth=;"));
    }

    [Fact]
    public async Task Open_screen_is_off_until_switched_on()
    {
        _client.DefaultRequestHeaders.Clear();
        var response = await Send(HttpMethod.Get, "/skjerm");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Open_screen_needs_no_login_and_shows_no_names()
    {
        var teams = _app.Services.GetRequiredService<TeamService>();
        var team = await teams.CreateAsync("Snekker");
        await teams.AddMemberAsync(team.Id, 424242, "Kari Hemmelig");
        await _app.Services.GetRequiredService<PotService>().SaveSettingsAsync(
            new PotSettings { Title = "SØRLANDET", SamvirkPercent = 72, PublicScreenEnabled = true });

        var response = await Send(HttpMethod.Get, "/skjerm?kilde=" + PotSource.TeamKey(team.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Snekker", html);
        Assert.Contains("pot-bar", html);
        Assert.Contains("http-equiv=\"refresh\"", html);
        Assert.DoesNotContain("Kari Hemmelig", html);
        Assert.DoesNotContain("424242", html);
        // The rest of the site stays closed.
        Assert.Equal(HttpStatusCode.Redirect, (await Send(HttpMethod.Get, "/pot")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/pot")).StatusCode);
    }
}
