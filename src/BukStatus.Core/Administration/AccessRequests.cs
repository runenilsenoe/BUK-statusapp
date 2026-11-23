using System.Net.Mail;
using BukStatus.Core.Groups;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Administration;
public class AccessRequest
{
    public int PersonId { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public bool Approved { get; set; }
    public DateTime? NotifiedAtUtc { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public string? DeliveryError { get; set; }
}
public class AccessNotificationSettings
{
    public int Id { get; set; } = 1;
    public string Recipient { get; set; } = "";
}
public sealed class AccessRequestService(IDbContextFactory<BukStatusDbContext> factory)
{
    static void Super(int actor) { if (actor != AdministratorStore.SuperAdminPersonId) throw new UnauthorizedAccessException(); }
    public async Task RequestAsync(int personId, string? name, CancellationToken ct = default)
    {
        if (personId <= 0) throw new UnauthorizedAccessException();
        await using var db = await factory.CreateDbContextAsync(ct);
        if (personId == AdministratorStore.SuperAdminPersonId || await db.Administrators.AnyAsync(a => a.PersonId == personId, ct)) return;
        var row = await db.AccessRequests.FindAsync([personId], ct);
        if (row is { Approved: false }) return; // Repeated clicks never generate more mail.
        if (row is null) { row = new() { PersonId = personId }; db.AccessRequests.Add(row); }
        row.DisplayName = string.IsNullOrWhiteSpace(name) ? $"Person {personId}" : name.Trim()[..Math.Min(name.Trim().Length, 200)];
        row.RequestedAtUtc = DateTime.UtcNow; row.Approved = false; row.NotifiedAtUtc = null;
        row.NextAttemptUtc = DateTime.UtcNow; row.DeliveryError = null;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { if (!await PendingAsync(personId, ct)) throw; }
    }
    async Task<bool> PendingAsync(int personId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AccessRequests.AnyAsync(r => r.PersonId == personId && !r.Approved, ct);
    }
    public async Task<List<AccessRequest>> GetPendingAsync(int actor, CancellationToken ct = default)
    {
        Super(actor); await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AccessRequests.AsNoTracking().Where(r => !r.Approved && !db.Administrators.Any(a => a.PersonId == r.PersonId)).OrderBy(r => r.RequestedAtUtc).ToListAsync(ct);
    }
    public async Task<string> GetRecipientAsync(int actor, CancellationToken ct = default)
    {
        Super(actor); await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.AccessNotificationSettings.FindAsync([1], ct))?.Recipient ?? "";
    }
    public async Task SaveRecipientAsync(int actor, string? email, CancellationToken ct = default)
    {
        Super(actor); email = email?.Trim() ?? "";
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new GroupException("Oppgi én gyldig e-postadresse.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.AccessNotificationSettings.FindAsync([1], ct);
        if (settings is null) { settings = new(); db.AccessNotificationSettings.Add(settings); }
        settings.Recipient = email; await db.SaveChangesAsync(ct);
    }
    public async Task ApproveAsync(int actor, int personId, CancellationToken ct = default)
    {
        Super(actor); await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.AccessRequests.FindAsync([personId], ct) ?? throw new GroupException("Forespørselen finnes ikke.");
        if (!await db.Administrators.AnyAsync(a => a.PersonId == personId, ct))
            db.Administrators.Add(new() { PersonId = personId, DisplayName = request.DisplayName, AddedBy = actor, AddedAtUtc = DateTime.UtcNow });
        request.Approved = true;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
}
