using BukStatus.Core.Groups;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Administration;

public sealed class Administrator
{
    public int PersonId { get; set; }
    public string? DisplayName { get; set; }
    public int AddedBy { get; set; }
    public DateTime AddedAtUtc { get; set; }
}

public interface IWriteAccess
{
    Task RequireAsync(CancellationToken ct = default);
}

/// <summary>Global application roles. They never grant access to a MyShare club.</summary>
public sealed class AdministratorStore(IDbContextFactory<BukStatusDbContext> factory)
{
    public const int SuperAdminPersonId = 21410;
    public async Task<bool> IsAdministratorAsync(int personId, CancellationToken ct = default)
    {
        if (personId == SuperAdminPersonId) return true;
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Administrators.AnyAsync(a => a.PersonId == personId, ct);
    }
    public async Task<List<Administrator>> GetAllAsync(int actor, CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Administrators.AsNoTracking().OrderBy(a => a.DisplayName).ThenBy(a => a.PersonId).ToListAsync(ct);
    }
    public async Task AddAsync(int actor, int personId, string? name, CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        if (personId <= 0) throw new GroupException("Person-ID må være et positivt tall.");
        if (personId == SuperAdminPersonId) throw new GroupException("21410 er allerede fast superadministrator.");
        if (name?.Length > 200) throw new GroupException("Navnet kan være maksimalt 200 tegn.");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Administrators.AnyAsync(a => a.PersonId == personId, ct))
            throw new GroupException("Personen er allerede administrator.");
        db.Administrators.Add(new() { PersonId = personId, DisplayName = string.IsNullOrWhiteSpace(name) ? null : name.Trim(), AddedBy = actor, AddedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
    }
    public async Task RemoveAsync(int actor, int personId, CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        if (personId == SuperAdminPersonId) throw new GroupException("Den faste superadministratoren kan ikke fjernes.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Administrators.Where(a => a.PersonId == personId).ExecuteDeleteAsync(ct);
    }
    private static void RequireSuperAdmin(int actor)
    {
        if (actor != SuperAdminPersonId) throw new UnauthorizedAccessException("Kun superadministratoren kan endre administratorer.");
    }
}
