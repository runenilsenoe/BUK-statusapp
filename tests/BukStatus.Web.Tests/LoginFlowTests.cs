using BukStatus.Core.Groups;
using Microsoft.EntityFrameworkCore;
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
            host.UseSetting("Auth:ApiKeys:0", "samvirk-test-key");
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
    [InlineData(null, "2026_11_10_ung.csv", HttpStatusCode.OK)]
    [InlineData("2026-11-10", "2026_11_10_ung.csv", HttpStatusCode.OK)]
    [InlineData("2026-11-11", "2026_11_10_ung.csv", HttpStatusCode.BadRequest)]
    [InlineData(null, "2026_11_31_ung.csv", HttpStatusCode.BadRequest)]
    public async Task Samvirk_filename_date_is_used_and_conflicts_are_rejected(string? date, string filename, HttpStatusCode expected)
    {
        using var content = new MultipartFormDataContent();
        if (date is not null) content.Add(new StringContent(date), "date");
        content.Add(new StringContent("BCC Person ID;Display Name;Church Id\n1;Synthetic;49"), "file", filename);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/samvirk/preview") { Content = content };
        request.Headers.Add("X-Api-Key", "samvirk-test-key");
        var response = await _client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK) Assert.Contains("2026-11-10", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Samvirk_machine_bridge_requires_preview_and_confirmation_and_blocks_cookie_calls()
    {
        var forbidden = await Send(HttpMethod.Get, "/api/samvirk/latest", LoginCookie("21410"));
        Assert.NotEqual(HttpStatusCode.OK, forbidden.StatusCode);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("2026-01-10"), "date");
        content.Add(new StringContent("BCC Person ID;Display Name;Church Id;Active BUK Drive;Birth Date;On Track Amount NOK;Get On Track Amount NOK;Coins 2026-01;Coins 2027-03\n1;Synthetic Active;49;True;11.01.2000;3375;12000;100;0\n2;Synthetic Inactive;49;False;01.01.2000;2700;;50;0"), "file", "sample.csv");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/samvirk/preview") { Content = content };
        upload.Headers.Add("X-Api-Key", "samvirk-test-key");
        var previewResponse = await _client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        using var preview = System.Text.Json.JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var id = preview.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("2027-03", preview.RootElement.GetProperty("months")[1].GetString());
        using var confirm = new HttpRequestMessage(HttpMethod.Post, $"/api/samvirk/confirm/{id}");
        confirm.Headers.Add("X-Api-Key", "samvirk-test-key");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(confirm)).StatusCode);
        using var latest = new HttpRequestMessage(HttpMethod.Get, "/api/samvirk/latest");
        latest.Headers.Add("X-Api-Key", "samvirk-test-key");
        var result = await _client.SendAsync(latest);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("Synthetic", await result.Content.ReadAsStringAsync());
        var page = await Send(HttpMethod.Get, "/samvirk", LoginCookie("21410"));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = System.Net.WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("Aktive (1)", html);
        Assert.Contains("Inaktive (1)", html);
        Assert.DoesNotContain(new DateOnly(2025, 9, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("nb-NO")), html);
        Assert.Contains("5 måneder", html);
        Assert.Contains("aria-expanded=\"false\"", html);
        Assert.Contains(new DateOnly(2026, 1, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("nb-NO")), html);
        Assert.Contains("✅3375+", html);
        Assert.Contains("≥ 2", html);
        Assert.Contains("50 coins", html);
        Assert.Contains("Lagre minstebeløp", html);
    }

    [Fact]
    public async Task Monthly_report_has_team_tabs_and_scopes_samvirk_rows_to_selected_team()
    {
        var factory = _app.Services.GetRequiredService<IDbContextFactory<BukStatusDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var first = new Team { Name = "Testlag A", Members = [new() { PersonId = 1, DisplayName = "Person A" }] };
        var second = new Team { Name = "Testlag B", Members = [new() { PersonId = 2, DisplayName = "Person B" }] };
        db.Teams.AddRange(first, second);
        BukStatus.Core.Samvirk.SamvirkPerson Person(int id, string name) => new(id, name, 49, new(2000, 1, 1),
            new() { ["Active BUK Drive"] = "True" },
            new() { ["On Track Amount NOK"] = 3375, ["Current Target NOK"] = 10000, ["Current Balance NOK"] = 9000 },
            new(), [new(new(2026, 1, 1), null, 100, null, null)]);
        db.SamvirkImports.Add(new() { Id = Guid.NewGuid(), SnapshotDate = new(2026, 1, 10), ConfirmedAt = DateTimeOffset.UtcNow,
            Hash = "synthetic-report", DataJson = System.Text.Json.JsonSerializer.Serialize(new[] { Person(1, "Eksportnavn A"), Person(2, "Eksportnavn B") }) });
        await db.SaveChangesAsync();
        var cookie = LoginCookie("21410");
        var tableResponse = await Send(HttpMethod.Get, "/report?month=2026-01", cookie);
        var table = WebUtility.HtmlDecode(await tableResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, tableResponse.StatusCode);
        Assert.Contains("Tabell", table);
        Assert.Contains("Testlag A", table);
        Assert.Contains("Testlag B", table);
        Assert.DoesNotContain("Detaljtransaksjoner", table);
        var response = await Send(HttpMethod.Get, $"/report?month=2026-01&team={first.Id}", cookie);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Budsjett og inntjent", html);
        Assert.Contains("Transaksjoner", html);
        Assert.Contains("Eksportnavn A", html);
        Assert.DoesNotContain("Eksportnavn B", html);
        Assert.Contains("✅3375+", html);
        Assert.Contains("Akkumulert til eksportdato", html);
        var missing = await Send(HttpMethod.Get, $"/report?month=2026-02&team={first.Id}", cookie);
        Assert.Contains("Ingen bekreftet Samvirk-eksport", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()));
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
    public async Task Transaction_page_renders_search_period_and_totals()
    {
        var response = await Send(HttpMethod.Get, "/transactions", LoginCookie(AllowedPersonId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Sum for søket", html);
        Assert.Contains("Transaksjons-ID", html);
        Assert.Contains("syntetiske testdata", html);
    }

    [Fact]
    public async Task Access_denied_shows_the_person_id_to_pass_on()
    {
        var html = await (await Send(HttpMethod.Get, "/access-denied", LoginCookie("55555"))).Content.ReadAsStringAsync();

        Assert.Contains("55555", html);
    }

    [Fact]
    public async Task Club_mode_also_requires_approval_before_opening_app_or_person_notes()
    {
        using var app = _app.WithWebHostBuilder(host => host.UseSetting("ClubAccess:Enabled", "true"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var path in new[] { "/", "/transactions", "/people", "/administrators" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Cookie", LoginCookie("55555"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/access-denied", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task Access_request_requires_cookie_identity_and_antiforgery_and_enables_login_after_approval()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "/account/request-access")).StatusCode);
        var cookie = LoginCookie("55555", "Synthetic Applicant");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/account/request-access", cookie)).StatusCode);
        var page = await Send(HttpMethod.Get, "/access-denied", cookie);
        var html = await page.Content.ReadAsStringAsync();
        var token = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        var anti = page.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith(".AspNetCore.Antiforgery"));
        var response = await Send(HttpMethod.Post, "/account/request-access", $"{cookie}; {anti}", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token, ["personId"] = "99999", ["name"] = "Forged" }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var requests = _app.Services.GetRequiredService<BukStatus.Core.Administration.AccessRequestService>();
        var pending = Assert.Single(await requests.GetPendingAsync(21410));
        Assert.Equal(55555, pending.PersonId); Assert.Equal("Synthetic Applicant", pending.DisplayName);
        var panel = System.Net.WebUtility.HtmlDecode(await (await Send(HttpMethod.Get, "/administrators", LoginCookie("21410"))).Content.ReadAsStringAsync());
        Assert.Contains("Legg til ny admin", panel); Assert.Contains("Synthetic Applicant", panel);
        await requests.ApproveAsync(21410, 55555);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/", cookie)).StatusCode);
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

    [Fact]
    public async Task Connecting_myshare_requests_verified_audience_and_refresh_scope_without_changing_basic_login()
    {
        Assert.Equal(HttpStatusCode.Redirect,(await Send(HttpMethod.Get,"/account/myshare/connect")).StatusCode);
        var response=await Send(HttpMethod.Get,"/account/myshare/connect?returnUrl=https://other.example",LoginCookie(AllowedPersonId));
        var location=System.Net.WebUtility.UrlDecode(response.Headers.Location!.ToString());
        Assert.Contains("audience=https://myshare.today/api/",location);
        Assert.Contains("offline_access",location); Assert.Contains("mfa",location);
        Assert.Contains("code_challenge_method=S256",location); Assert.Equal("/",_lastReturnUrl);
        var basic=await Send(HttpMethod.Get,"/account/login");
        Assert.DoesNotContain("audience=",basic.Headers.Location!.ToString());
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
        var factory = _app.Services.GetRequiredService<IDbContextFactory<BukStatusDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var team = new Team { Name = "Snekker", Members = [new() { PersonId = 424242, DisplayName = "Kari Hemmelig" }] };
        db.Teams.Add(team);
        db.PotSettings.Add(new PotSettings { Title = "SØRLANDET", SamvirkPercent = 72, PublicScreenEnabled = true });
        await db.SaveChangesAsync();

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
    [Fact]
    public async Task Superadmin_page_does_not_require_legacy_allowlist_and_is_hidden_from_other_users()
    {
        var super = await Send(HttpMethod.Get, "/administrators", LoginCookie("21410"));
        Assert.Equal(HttpStatusCode.OK, super.StatusCode);
        Assert.Contains("Legg til administrator", await super.Content.ReadAsStringAsync());
        var ordinary = await Send(HttpMethod.Get, "/administrators", LoginCookie(AllowedPersonId));
        Assert.DoesNotContain("Legg til administrator", await ordinary.Content.ReadAsStringAsync());
    }
}
