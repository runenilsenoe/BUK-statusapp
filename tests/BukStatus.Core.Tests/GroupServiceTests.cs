using BukStatus.Core.Groups;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Tests;

public sealed class GroupServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly GroupService _groups;

    public GroupServiceTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(_connection).Options;
        using (var db = new BukStatusDbContext(options))
            db.Database.Migrate();
        _groups = new GroupService(new TestDbContextFactory(options));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Rejects_duplicate_group_names()
    {
        await _groups.CreateAsync("Ungdom");

        var ex = await Assert.ThrowsAsync<GroupException>(() => _groups.CreateAsync(" Ungdom "));
        Assert.Contains("Ungdom", ex.Message);
    }

    [Fact]
    public async Task Rejects_same_person_twice_in_a_group_but_allows_other_groups()
    {
        var a = await _groups.CreateAsync("A");
        var b = await _groups.CreateAsync("B");
        await _groups.AddMemberAsync(a.Id, 42, "Kari");

        await Assert.ThrowsAsync<GroupException>(() => _groups.AddMemberAsync(a.Id, 42, null));
        await _groups.AddMemberAsync(b.Id, 42, null);

        Assert.Single((await _groups.GetAsync(b.Id))!.Members);
    }

    [Fact]
    public async Task Deleting_a_group_removes_its_members()
    {
        var group = await _groups.CreateAsync("A");
        await _groups.AddMemberAsync(group.Id, 1, null);
        await _groups.AddMemberAsync(group.Id, 2, null);

        await _groups.DeleteAsync(group.Id);

        Assert.Null(await _groups.GetAsync(group.Id));
        await using var db = new BukStatusDbContext(
            new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(_connection).Options);
        Assert.Equal(0, await db.GroupMembers.CountAsync());
    }

    [Fact]
    public async Task Blank_display_name_is_stored_as_null()
    {
        var group = await _groups.CreateAsync("A");

        var member = await _groups.AddMemberAsync(group.Id, 1, "   ");

        Assert.Null(member.DisplayName);
    }

    private sealed class TestDbContextFactory(DbContextOptions<BukStatusDbContext> options)
        : IDbContextFactory<BukStatusDbContext>
    {
        public BukStatusDbContext CreateDbContext() => new(options);
    }
}
