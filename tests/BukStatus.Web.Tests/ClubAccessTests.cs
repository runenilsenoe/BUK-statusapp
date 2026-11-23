using System.Net;
using System.Net.Http.Json;
using BukStatus.Core.Clubs;
using BukStatus.Web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BukStatus.Web.Tests;

public sealed class ClubAccessTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "buk-clubs-" + Guid.NewGuid());
    private readonly DirectoryStub directory = new();
    public ClubAccessTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder, true);

    private WebApplicationFactory<Program> App() => new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
    {
        host.UseEnvironment("Production");
        host.UseSetting("ConnectionStrings:BukStatus", $"Data Source={Path.Combine(folder, "legacy.db")};Pooling=False");
        host.UseSetting("BccApi:UseFakeData", "true");
        host.UseSetting("Auth:ClientId", "test-client");
        host.UseSetting("Auth:ClientSecret", "test-secret");
        host.UseSetting("Auth:ApiKeys:0", "test-key");
        host.UseSetting("ClubAccess:Enabled", "true");
        host.UseSetting("ClubAccess:LegacyClubId", "4050");
        host.UseSetting("ClubAccess:ApiPersonId", "21410");
        host.ConfigureServices(s => s.AddSingleton<IMyshareDirectory>(directory));
    });
    private static HttpClient Client(WebApplicationFactory<Program> app, int club)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "test-key");
        client.DefaultRequestHeaders.Add("X-Club-Id", club.ToString());
        return client;
    }
    [Fact]
    public async Task Team_access_grants_both_clubs_without_person_membership_and_ignores_userId_query()
    {
        using var app = App();
        using var client = Client(app, 4050);
        var response = await client.GetAsync("/api/clubs?userId=999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("BCC test", body);
        Assert.Contains("BUK test", body);
        Assert.All(directory.Requests, id => Assert.Equal(21410, id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/clubs/999/members")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/clubs/999/status")).StatusCode);
    }
    [Fact]
    public async Task Local_groups_are_isolated_and_forged_members_are_rejected()
    {
        using var app = App();
        using var first = Client(app, 4050);
        using var second = Client(app, 6817);
        var created = await first.PostAsJsonAsync("/api/groups", new { Name = "Only BUK" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync("/api/groups/1")).StatusCode);
        Assert.DoesNotContain("Only BUK", await second.GetStringAsync("/api/groups"));
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/groups/1/members", new { PersonId = 202, DisplayName = "Forged" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/groups/1/members", new { PersonId = 101, DisplayName = "Forged" })).StatusCode);
        var group = await first.GetStringAsync("/api/groups/1");
        Assert.Contains("BUK member", group);
        Assert.DoesNotContain("Forged", group);
    }
    [Fact]
    public async Task All_club_members_have_status_without_local_teams_and_birthdates_are_not_exposed()
    {
        using var app = App();
        using var client = Client(app, 4050);
        var status = await client.GetStringAsync("/api/clubs/4050/status");
        Assert.Contains("BUK member", status);
        var members = await client.GetStringAsync("/api/clubs/4050/members");
        Assert.Contains("\"age\":", members);
        Assert.DoesNotContain("birthDate", members);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/skjerm")).StatusCode);
    }
    [Fact]
    public async Task Unknown_selected_club_is_denied_and_picker_shows_names_and_ages()
    {
        using var app = App();
        using var denied = Client(app, 999);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/api/groups")).StatusCode);
        using var client = Client(app, 4050);
        await client.PostAsJsonAsync("/api/groups", new { Name = "Members" });
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/groups/1"));
        Assert.Contains("Velg medlem fra MyShare", html);
        Assert.Contains("BUK member", html);
        Assert.Contains("år · #101", html);
        Assert.DoesNotContain("BCC member", html);
    }

    [Fact]
    public async Task People_page_uses_full_club_roster_without_local_teams()
    {
        using var app = App();
        using var client = Client(app, 4050);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/people"));
        Assert.Contains("BUK member", html);
        Assert.DoesNotContain("BCC member", html);
        Assert.Contains("Ikke valgt", html);
        Assert.Contains("Rediger", html);
        Assert.DoesNotContain("Personlisten kunne ikke hentes", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Team_member_picker_has_name_search_in_both_club_and_legacy_modes(bool clubMode)
    {
        using var app = App().WithWebHostBuilder(host => host.UseSetting("ClubAccess:Enabled", clubMode.ToString()));
        using var client = Client(app, 4050);
        await using var db = await app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BukStatus.Core.Groups.BukStatusDbContext>>().CreateDbContextAsync();
        var team = new BukStatus.Core.Teams.Team { Name = "Searchable members" };
        db.Teams.Add(team); await db.SaveChangesAsync();
        var id = team.Id;
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/teams/{id}"));
        Assert.Contains("type=\"search\"", html);
        Assert.Contains("Søk etter person", html);
        Assert.Contains("<option value=\"101\">BUK member", html);
        Assert.DoesNotContain("BCC member", html);
        Assert.DoesNotContain("Navn (valgfritt)", html);
        db.TeamMembers.Add(new BukStatus.Core.Teams.TeamMember { TeamId = id, PersonId = 101, DisplayName = "BUK member" });
        await db.SaveChangesAsync();
        html = WebUtility.HtmlDecode(await client.GetStringAsync($"/teams/{id}"));
        Assert.DoesNotContain("<option value=\"101\">", html);
    }

    [Fact]
    public async Task Legacy_member_form_keeps_local_search_and_manual_entry_when_myshare_rejects_login()
    {
        directory.Fail = true;
        using var app = App().WithWebHostBuilder(host => host.UseSetting("ClubAccess:Enabled", "false"));
        using var client = Client(app,4050);
        await using var db = await app.Services.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BukStatus.Core.Groups.BukStatusDbContext>>().CreateDbContextAsync();
        var team = new BukStatus.Core.Teams.Team { Name = "Target" };
        var other = new BukStatus.Core.Teams.Team { Name = "Source", Members = [new() { PersonId = 999, DisplayName = "Locally known person" }] };
        db.Teams.AddRange(team,other); await db.SaveChangesAsync();
        var response = await client.GetAsync($"/teams/{team.Id}");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("HTTP 401",html);
        Assert.Contains("ikke hele MyShare-listen",html);
        Assert.Contains("Locally known person",html);
        Assert.Contains("aria-label=\"Person-ID\"",html);
        Assert.Contains("aria-label=\"Navn\"",html);
        Assert.Contains("type=\"search\"",html);
    }

    private sealed class DirectoryStub : IMyshareDirectory
    {
        public List<int> Requests { get; } = [];
        public bool Fail;
        public Task<IReadOnlyList<MyshareClub>> GetClubsAsync(int personId, CancellationToken ct = default)
        {
            Requests.Add(personId);
            if (Fail) throw new MyshareDirectoryException("MyShare avviser innloggingen (HTTP 401). Kontroller MyShare-tokenet.");
            return Task.FromResult<IReadOnlyList<MyshareClub>>([
                new(4050, 10016, "BUK test", [new(101, "BUK member", "BUK", "member", new DateOnly(2000, 5, 12), 1)]),
                new(6817, 10016, "BCC test", [new(202, "BCC member", null, null, null, 40)])]);
        }
    }
}
