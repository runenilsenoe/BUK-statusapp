using BukStatus.Core.Groups;
using BukStatus.Core.Teams;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Tests;

public sealed class TeamServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TeamService _teams;

    public TeamServiceTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(_connection).Options;
        using (var db = new BukStatusDbContext(options))
            db.Database.Migrate();
        _teams = new TeamService(new TestDbContextFactory(options));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task A_person_can_only_be_on_one_team()
    {
        var a = await _teams.CreateAsync("A");
        var b = await _teams.CreateAsync("B");
        await _teams.AddMemberAsync(a.Id, 7, null);

        var ex = await Assert.ThrowsAsync<GroupException>(() => _teams.AddMemberAsync(b.Id, 7, null));
        Assert.Contains("«A»", ex.Message);
    }

    [Fact]
    public async Task Import_creates_teams_moves_people_and_reports_bad_lines()
    {
        var a = await _teams.CreateAsync("Snekker");
        await _teams.AddMemberAsync(a.Id, 1, "Gammelt navn");
        await _teams.AddMemberAsync(a.Id, 2, null);

        var result = await _teams.ImportAsync(
            "Person ID\tNavn\tLagordning\n" +
            "1\tNytt navn\tsnekker\n" +   // same team, different case: unchanged, name updated
            "2\tKari\tUtomhus\n" +        // moved to a new team
            "3\tPer\tUtomhus\n" +         // added
            "abc\tFeil\tUtomhus\n" +
            "4\tUten lag\t\n");

        Assert.Equal((1, 1, 1, 1), (result.Added, result.Moved, result.Unchanged, result.TeamsCreated));
        Assert.Equal(2, result.Errors.Count);

        var teams = await _teams.GetAllAsync();
        Assert.Equal(["Snekker", "Utomhus"], teams.Select(t => t.Name));
        Assert.Equal("Nytt navn", teams[0].Members.Single().DisplayName);
        Assert.Equal([2, 3], teams[1].Members.Select(m => m.PersonId).Order());
    }

    [Fact]
    public async Task Monthly_targets_must_be_complete_and_increasing()
    {
        var increasing = Enumerable.Range(1, 12).ToDictionary(m => m, m => m / 12m);
        await _teams.SaveMonthlyTargetsAsync(2026, increasing);
        Assert.Equal(0.5m, (await _teams.GetMonthlyTargetsAsync(2026))[6]);

        var decreasing = new Dictionary<int, decimal>(increasing) { [7] = 0.1m };
        await Assert.ThrowsAsync<GroupException>(() => _teams.SaveMonthlyTargetsAsync(2026, decreasing));
        await Assert.ThrowsAsync<GroupException>(() => _teams.SaveMonthlyTargetsAsync(2026, new Dictionary<int, decimal> { [1] = 0.1m }));
    }

    private sealed class TestDbContextFactory(DbContextOptions<BukStatusDbContext> options)
        : IDbContextFactory<BukStatusDbContext>
    {
        public BukStatusDbContext CreateDbContext() => new(options);
    }
}
