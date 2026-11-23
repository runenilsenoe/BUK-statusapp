using BukStatus.Core.Groups;
using BukStatus.Core.Clubs;
using BukStatus.Core.Pot;
using BukStatus.Core.Status;
using BukStatus.Core.Teams;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace BukStatus.Core.Tests;

public sealed class PotServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TeamService _teams;
    private readonly GroupService _groups;
    private readonly PotService _pot;
    private readonly TestDbContextFactory _factory;

    public PotServiceTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(_connection).Options;
        using (var db = new BukStatusDbContext(options))
            db.Database.Migrate();
        var factory = _factory = new TestDbContextFactory(options);
        _teams = new TeamService(factory);
        _groups = new GroupService(factory);
        var extra = new ExtraContributionsService([], new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ExtraContributionsService>.Instance);
        _pot = new PotService(factory, _teams, _groups, new FakeMemberStatusService(), extra, TimeProvider.System);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Settings_have_defaults_and_are_saved()
    {
        Assert.Equal("Samlet", (await _pot.GetSettingsAsync()).Title);

        await _pot.SaveSettingsAsync(new PotSettings
        {
            Title = " SØRLANDET ",
            Subtitle = " ",
            SamvirkPercent = 72,
            ExtraContributions = 12_500,
            Milestones = "70, 75",
        });

        var saved = await _pot.GetSettingsAsync();
        Assert.Equal("SØRLANDET", saved.Title);
        Assert.Null(saved.Subtitle);
        Assert.Equal(72m, saved.SamvirkPercent);
        Assert.Equal(12_500m, saved.ExtraContributions);
        Assert.Equal("70;75", saved.Milestones);
        Assert.Equal(ExtraContributionsMode.Manual, saved.ExtraMode);

        await _pot.SaveSettingsAsync(new PotSettings
        {
            Title = "T",
            ExtraMode = ExtraContributionsMode.Automatic,
            ExtraOrgId = 55,
            ExtraDescriptionFilter = " Donation ",
            ExtraFrom = new DateOnly(2026, 1, 1),
            ExtraExcludedPersonIds = "12, 13",
        });
        saved = await _pot.GetSettingsAsync();
        Assert.Equal(ExtraContributionsMode.Automatic, saved.ExtraMode);
        Assert.Equal(55, saved.ExtraOrgId);
        Assert.Equal("Donation", saved.ExtraDescriptionFilter);
        Assert.Equal(new DateOnly(2026, 1, 1), saved.ExtraFrom);
        Assert.Equal("12;13", saved.ExtraExcludedPersonIds);
    }

    [Theory]
    [InlineData("", 50, 0, "70")]
    [InlineData("X", 101, 0, "70")]
    [InlineData("X", 50, -1, "70")]
    [InlineData("X", 50, 0, "70;mye")]
    public async Task Rejects_invalid_settings(string title, decimal samvirk, decimal extra, string milestones)
    {
        await Assert.ThrowsAsync<GroupException>(() => _pot.SaveSettingsAsync(new PotSettings
        {
            Title = title,
            SamvirkPercent = samvirk,
            ExtraContributions = extra,
            Milestones = milestones,
        }));
    }

    [Fact]
    public async Task Screen_covers_all_teams_one_team_or_one_group()
    {
        var a = await _teams.CreateAsync("A");
        var b = await _teams.CreateAsync("B");
        await _teams.AddMemberAsync(a.Id, 1, null);
        await _teams.AddMemberAsync(a.Id, 2, null);
        await _teams.AddMemberAsync(b.Id, 3, null);
        var group = await _groups.CreateAsync("G");
        await _groups.AddMemberAsync(group.Id, 3, null);
        await _pot.SaveSettingsAsync(new PotSettings { Title = "T", ExtraContributions = 1_000 }); // manual mode

        var all = await _pot.GetScreenAsync(null, 500, PotStrategy.Even);
        Assert.Equal(PotSource.AllTeamsKey, all.Source.Key);
        Assert.Equal(3, all.Result.Count);
        Assert.Equal(1_500m, all.Result.Pot);

        var team = await _pot.GetScreenAsync(PotSource.TeamKey(a.Id), 0, PotStrategy.Even);
        Assert.Equal("A", team.Source.Name);
        Assert.Equal(2, team.Result.Count);

        var fromGroup = await _pot.GetScreenAsync(PotSource.GroupKey(group.Id), 0, PotStrategy.Even);
        Assert.Equal("G", fromGroup.Source.Name);
        Assert.Equal([3], fromGroup.Result.Members.Select(m => m.PersonId));

        var unknown = await _pot.GetScreenAsync("gruppe-999", 0, PotStrategy.Even);
        Assert.Equal(PotSource.AllTeamsKey, unknown.Source.Key);

        var sources = await _pot.GetSourcesAsync();
        Assert.Equal(["lag", PotSource.TeamKey(a.Id), PotSource.TeamKey(b.Id), PotSource.GroupKey(group.Id)], sources.Select(s => s.Key));
    }

    [Fact]
    public async Task Default_covers_complete_roster_even_without_teams_and_team_selection_remains_limited()
    {
        var roster = new Roster();
        var extra = new ExtraContributionsService([], new MemoryCache(new MemoryCacheOptions()), NullLogger<ExtraContributionsService>.Instance);
        var pot = new PotService(_factory, _teams, _groups, new FakeMemberStatusService(), extra, TimeProvider.System, roster: roster);
        var emptyTeams = await pot.GetScreenAsync(null, 0, PotStrategy.Even);
        Assert.Equal([1, 2, 3, 4], emptyTeams.Result.Members.Select(m => m.PersonId).Order());
        Assert.Equal("BUK test", emptyTeams.Source.Name);
        var team = await _teams.CreateAsync("Only one");
        await _teams.AddMemberAsync(team.Id, 1, "Local name");
        var group = await _groups.CreateAsync("Only group");
        await _groups.AddMemberAsync(group.Id, 2, "Local name");
        var all = await pot.GetScreenAsync(PotSource.AllTeamsKey, 0, PotStrategy.Even);
        Assert.Equal(4, all.Result.Count);
        Assert.Contains(all.Result.Members, m => m.PersonId == 4 && m.DisplayName == "No team");
        Assert.Equal([1], (await pot.GetScreenAsync(PotSource.TeamKey(team.Id), 0, PotStrategy.Even)).Result.Members.Select(m => m.PersonId));
        Assert.Equal([2], (await pot.GetScreenAsync(PotSource.GroupKey(group.Id), 0, PotStrategy.Even)).Result.Members.Select(m => m.PersonId));
        Assert.Equal("Alle i BUK test", (await pot.GetSourcesAsync())[0].Name);
        roster.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pot.GetScreenAsync(null, 0, PotStrategy.Even));
    }

    [Fact]
    public async Task Local_only_default_includes_group_only_people_and_deduplicates_overlapping_members()
    {
        var team = await _teams.CreateAsync("Team"); await _teams.AddMemberAsync(team.Id, 1, null);
        var group = await _groups.CreateAsync("Group");
        await _groups.AddMemberAsync(group.Id, 1, null); await _groups.AddMemberAsync(group.Id, 2, null);
        Assert.Equal([1, 2], (await _pot.GetScreenAsync(null, 0, PotStrategy.Even)).Result.Members.Select(m => m.PersonId).Order());
    }

    [Fact]
    public async Task Own_contribution_from_member_without_team_is_not_counted_as_extra()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var json = "{\"statusCode\":200,\"data\":[{\"userId\":4,\"transactionDate\":\"" + today.ToString("yyyy-MM-dd") + "\",\"amount\":500},{\"userId\":900,\"transactionDate\":\"" + today.ToString("yyyy-MM-dd") + "\",\"amount\":100}]}";
        var source = new MyshareExtraContributionsSource(new HttpFactory(json), Microsoft.Extensions.Options.Options.Create(new MyshareOptions { BaseUrl = "https://synthetic.example/api", Bearer = "synthetic", ClubId = 4050 }));
        var extra = new ExtraContributionsService([source], new MemoryCache(new MemoryCacheOptions()), NullLogger<ExtraContributionsService>.Instance);
        var pot = new PotService(_factory, _teams, _groups, new FakeMemberStatusService(), extra, TimeProvider.System, roster: new Roster());
        await pot.SaveSettingsAsync(new PotSettings { Title = "Test", ExtraMode = ExtraContributionsMode.Automatic });
        var result = await pot.GetScreenAsync(null, 0, PotStrategy.Even);
        Assert.Equal(4, result.Result.Count);
        Assert.Equal(100, result.Extra.Amount); Assert.Equal(1, result.Extra.Count);
    }
    private sealed class HttpFactory(string json) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(json));
    }
    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private sealed class Roster : IPotRoster
    {
        public bool Denied;
        public Task<ClubScope?> GetAsync(CancellationToken ct = default) => Denied ? throw new UnauthorizedAccessException() : Task.FromResult<ClubScope?>(new(new(4050, 10016, "BUK test", [new(1,"One",null,null,null,20),new(2,"Two",null,null,null,21),new(3,"Three",null,null,null,22),new(4,"No team",null,null,null,23)]),4050));
    }

    private sealed class TestDbContextFactory(DbContextOptions<BukStatusDbContext> options)
        : IDbContextFactory<BukStatusDbContext>
    {
        public BukStatusDbContext CreateDbContext() => new(options);
    }
}
