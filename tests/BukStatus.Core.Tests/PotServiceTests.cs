using BukStatus.Core.Groups;
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

    public PotServiceTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(_connection).Options;
        using (var db = new BukStatusDbContext(options))
            db.Database.Migrate();
        var factory = new TestDbContextFactory(options);
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

    private sealed class TestDbContextFactory(DbContextOptions<BukStatusDbContext> options)
        : IDbContextFactory<BukStatusDbContext>
    {
        public BukStatusDbContext CreateDbContext() => new(options);
    }
}
