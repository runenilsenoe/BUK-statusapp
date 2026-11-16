using System.Text.Json;
using BukStatus.Core.Administration;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BukStatus.Core.Samvirk;
public class SamvirkImport
{
    public Guid Id { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string Hash { get; set; } = "";
    public string DataJson { get; set; } = "";
}
public record SamvirkPreview(Guid Id, DateOnly SnapshotDate, int People, string[] Months, int ChurchId, bool AlreadyImported);
public record SamvirkSnapshot(DateOnly SnapshotDate, DateTimeOffset ImportedAt, SamvirkPerson[] People);

public sealed class SamvirkService(IDbContextFactory<BukStatusDbContext> factory, IWriteAccess access, IClubContext clubs, IConfiguration config)
{
    private async Task<int> ChurchAsync(CancellationToken ct)
    {
        await access.RequireAsync(ct);
        var scope = await clubs.GetSelectedAsync(ct);
        // Church IDs in this export are a separate namespace from MyShare club IDs.
        var key = scope is null ? "Samvirk:LegacyChurchId" : $"Samvirk:ClubChurchIds:{scope.Club.Id}";
        var church = config.GetValue<int?>(key) ?? (scope is null ? 49 : 0);
        if (church <= 0) throw new GroupException("Samvirk-kobling for valgt klubb må konfigureres først.");
        return church;
    }
    public async Task<SamvirkPreview> PreviewAsync(byte[] bytes, string filename, DateOnly date, CancellationToken ct = default)
    {
        var church = await ChurchAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Oslo").DateTime);
        if (date.Year < 2020 || date > today) throw new GroupException("Velg en gyldig eksportdato, senest i dag.");
        SamvirkData data;
        try { data = SamvirkParser.Parse(bytes, filename); }
        catch (InvalidDataException ex) { throw new GroupException(ex.Message); }
        if (data.People.Any(p => p.ChurchId != church)) throw new GroupException("Filen inneholder en annen menighet enn konfigurert for denne importen.");
        await using var db = await factory.OpenForClubAsync(clubs, ct);
        var existing = await db.SamvirkImports.FirstOrDefaultAsync(i => i.Hash == data.Hash && i.SnapshotDate == date, ct);
        if (existing is not null && existing.ConfirmedAt is null) existing.UploadedAt = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new() { Id = Guid.NewGuid(), SnapshotDate = date, UploadedAt = DateTimeOffset.UtcNow, Hash = data.Hash, DataJson = JsonSerializer.Serialize(data.People) };
            db.SamvirkImports.Add(existing);
        }
        await db.SaveChangesAsync(ct);
        return new(existing.Id, date, data.People.Length, data.People.SelectMany(p => p.Months).Select(m => m.Month.ToString("yyyy-MM")).Distinct().Order().ToArray(), church, existing.ConfirmedAt is not null);
    }
    public async Task ConfirmAsync(Guid id, CancellationToken ct = default)
    {
        await ChurchAsync(ct);
        await using var db = await factory.OpenForClubAsync(clubs, ct);
        var import = await db.SamvirkImports.FindAsync([id], ct) ?? throw new GroupException("Importen finnes ikke.");
        if (import.ConfirmedAt is not null) return;
        if (DateTimeOffset.UtcNow - import.UploadedAt > TimeSpan.FromDays(1)) throw new GroupException("Forhåndsvisningen er utløpt. Last opp på nytt med ny eksportdato eller kontakt administrator.");
        import.ConfirmedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    public async Task<SamvirkSnapshot?> LatestAsync(CancellationToken ct = default)
    {
        await ChurchAsync(ct);
        await using var db = await factory.OpenForClubAsync(clubs, ct);
        // DateTimeOffset sorting is not supported by SQLite: snapshot date first, revisions in memory.
        var date = await db.SamvirkImports.Where(i => i.ConfirmedAt != null).MaxAsync(i => (DateOnly?)i.SnapshotDate, ct);
        if (date is null) return null;
        var imports = await db.SamvirkImports.AsNoTracking().Where(i => i.ConfirmedAt != null && i.SnapshotDate == date).ToListAsync(ct);
        var latest = imports.OrderByDescending(i => i.SnapshotDate).ThenByDescending(i => i.ConfirmedAt).FirstOrDefault();
        if (latest is null) return null;
        var people = JsonSerializer.Deserialize<SamvirkPerson[]>(latest.DataJson)!;
        if (await clubs.GetSelectedAsync(ct) is { } scope)
        {
            var ids = scope.Club.Members.Select(m => m.Id).ToHashSet();
            people = people.Where(p => ids.Contains(p.PersonId)).ToArray();
        }
        return new(latest.SnapshotDate, latest.ConfirmedAt!.Value, people);
    }
}
