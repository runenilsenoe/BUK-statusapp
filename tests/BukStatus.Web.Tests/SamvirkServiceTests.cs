using System.Text;
using BukStatus.Core.Administration;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using BukStatus.Core.Samvirk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace BukStatus.Core.Tests;
public sealed class SamvirkServiceTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"samvirk-{Guid.NewGuid()}.db");
    readonly Factory factory;
    readonly Gate gate = new();
    readonly Clubs clubs = new();
    readonly SamvirkService service;
    public SamvirkServiceTests()
    {
        factory = new(new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite($"Data Source={path}").Options);
        using var db = factory.CreateDbContext(); db.Database.Migrate();
        service = new(factory, gate, clubs, new ConfigurationBuilder().Build());
    }
    static byte[] Data(int balance, int church = 49) => Encoding.UTF8.GetBytes($"BCC Person ID;Display Name;Church Id;Current Balance NOK;Coins 2027-01\n1;Testperson;{church};{balance};0");
    [Fact] public async Task Preview_is_not_live_confirm_is_idempotent_and_old_upload_does_not_replace_latest()
    {
        var date = new DateOnly(2026, 1, 10);
        var preview = await service.PreviewAsync(Data(100), "test.csv", date);
        Assert.Null(await service.LatestAsync());
        await service.ConfirmAsync(preview.Id); await service.ConfirmAsync(preview.Id);
        Assert.True((await service.PreviewAsync(Data(100), "test.csv", date)).AlreadyImported);
        var old = await service.PreviewAsync(Data(50), "test.csv", date.AddDays(-1)); await service.ConfirmAsync(old.Id);
        Assert.Equal(100m, (await service.LatestAsync())!.People[0].Amounts["Current Balance NOK"]);
        var correction = await service.PreviewAsync(Data(80), "test.csv", date); await service.ConfirmAsync(correction.Id);
        Assert.Equal(80m, (await service.LatestAsync())!.People[0].Amounts["Current Balance NOK"]);
        using var db = factory.CreateDbContext(); Assert.Equal(3, await db.SamvirkImports.CountAsync());
    }
    [Fact] public async Task Denied_users_wrong_church_and_unconfigured_club_cannot_import()
    {
        gate.Denied = true;
        await Assert.ThrowsAsync<GroupException>(() => service.PreviewAsync(Data(1), "test.csv", new(2026, 1, 1)));
        await Assert.ThrowsAsync<GroupException>(() => service.LatestAsync());
        gate.Denied = false;
        await Assert.ThrowsAsync<GroupException>(() => service.PreviewAsync(Data(1, 99), "test.csv", new(2026, 1, 1)));
        clubs.Enabled = true;
        await Assert.ThrowsAsync<GroupException>(() => service.PreviewAsync(Data(1), "test.csv", new(2026, 1, 1)));
        using var db = factory.CreateDbContext(); Assert.Empty(db.SamvirkImports);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    sealed class Factory(DbContextOptions<BukStatusDbContext> options) : IDbContextFactory<BukStatusDbContext>
    { public BukStatusDbContext CreateDbContext() => new(options); }
    sealed class Gate : IWriteAccess
    { public bool Denied; public Task RequireAsync(CancellationToken ct = default) => Denied ? throw new GroupException("Denied") : Task.CompletedTask; }
    sealed class Clubs : IClubContext
    {
        public bool Enabled { get; set; }
        public Task<IReadOnlyList<MyshareClub>> GetClubsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MyshareClub>>([]);
        public Task<ClubScope?> GetSelectedAsync(CancellationToken ct = default) => Task.FromResult<ClubScope?>(Enabled ? new(new(4050,10016,"Synthetic",[]),4050) : null);
    }
}
