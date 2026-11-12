using System.Collections.Concurrent;
using BukStatus.Core.Groups;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Clubs;

public sealed record ClubScope(MyshareClub Club, int LegacyClubId);
public interface IClubContext
{
    bool Enabled { get; }
    Task<IReadOnlyList<MyshareClub>> GetClubsAsync(CancellationToken ct = default);
    Task<ClubScope?> GetSelectedAsync(CancellationToken ct = default);
}

public static class ClubData
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<string, bool> Migrated = new();

    // Each club has its own local teams, groups, targets and pot settings. The existing database is
    // retained only for the explicitly configured legacy club; no memberships are copied between clubs.
    public static async Task<BukStatusDbContext> OpenForClubAsync(this IDbContextFactory<BukStatusDbContext> factory,
        IClubContext? context, CancellationToken ct)
    {
        var scope = context is null ? null : await context.GetSelectedAsync(ct);
        var db = await factory.CreateDbContextAsync(ct);
        if (scope is null || scope.Club.Id == scope.LegacyClubId) return db;
        var connection = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        await db.DisposeAsync();
        var root = Path.GetFullPath(connection.DataSource);
        connection.DataSource = Path.Combine(Path.GetDirectoryName(root)!, $"club-{scope.Club.Id}.db");
        var gate = Locks.GetOrAdd(connection.DataSource, _ => new(1, 1));
        var scoped = new BukStatusDbContext(new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(connection.ToString()).Options);
        await gate.WaitAsync(ct);
        try
        {
            if (!Migrated.ContainsKey(connection.DataSource))
            {
                await scoped.Database.MigrateAsync(ct);
                Migrated[connection.DataSource] = true;
            }
        }
        catch { await scoped.DisposeAsync(); throw; }
        finally { gate.Release(); }
        return scoped;
    }

    public static async Task<string?> RequireMemberAsync(IClubContext? context, int personId, string? fallback, CancellationToken ct)
    {
        var scope = context is null ? null : await context.GetSelectedAsync(ct);
        if (scope is null) return fallback;
        return scope.Club.Members.FirstOrDefault(m => m.Id == personId)?.DisplayName
            ?? throw new GroupException("Personen er ikke medlem av den valgte klubben i MyShare.");
    }
}
