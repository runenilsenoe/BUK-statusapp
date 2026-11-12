using BukStatus.Core.Administration;
using BukStatus.Core.Groups;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Tests;

public sealed class AdministratorTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AdministratorStore store;
    public AdministratorTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(connection).Options;
        using var db = new BukStatusDbContext(options);
        db.Database.Migrate();
        store = new(new Factory(options));
    }
    public void Dispose() => connection.Dispose();
    [Fact]
    public async Task Superadmin_is_permanent_and_other_roles_can_be_added_and_revoked()
    {
        Assert.True(await store.IsAdministratorAsync(21410));
        Assert.False(await store.IsAdministratorAsync(12345));
        await store.AddAsync(21410, 12345, "Test Admin");
        Assert.True(await store.IsAdministratorAsync(12345));
        var admin = Assert.Single(await store.GetAllAsync(21410));
        Assert.Equal(21410, admin.AddedBy);
        Assert.Equal("Test Admin", admin.DisplayName);
        await store.RemoveAsync(21410, 12345);
        Assert.False(await store.IsAdministratorAsync(12345));
        await Assert.ThrowsAsync<GroupException>(() => store.RemoveAsync(21410, 21410));
        Assert.True(await store.IsAdministratorAsync(21410));
    }
    [Fact]
    public async Task Ordinary_administrators_cannot_grant_or_remove_roles()
    {
        await store.AddAsync(21410, 12345, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.AddAsync(12345, 54321, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.RemoveAsync(12345, 21410));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GetAllAsync(12345));
        await Assert.ThrowsAsync<GroupException>(() => store.AddAsync(21410, -1, null));
        await Assert.ThrowsAsync<GroupException>(() => store.AddAsync(21410, 12345, null));
    }
    private sealed class Factory(DbContextOptions<BukStatusDbContext> options) : IDbContextFactory<BukStatusDbContext>
    {
        public BukStatusDbContext CreateDbContext() => new(options);
    }
}
