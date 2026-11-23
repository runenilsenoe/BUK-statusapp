using BukStatus.Core.Administration;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.People;

public enum WorkPreference { CommittedJob = 1, EmployedSaver = 2, BukVolunteerJob = 3 }
public static class WorkPreferences
{
    public static readonly IReadOnlyDictionary<WorkPreference, string> Labels = new Dictionary<WorkPreference, string>
    {
        [WorkPreference.CommittedJob] = "Jeg vil gjerne ha fast jobb for å dekke samvirken min, og jeg er villig til å forplikte meg",
        [WorkPreference.EmployedSaver] = "Jeg har jobb og må passe på å spare fast i samvirk og betale for meg i BUK",
        [WorkPreference.BukVolunteerJob] = "Jeg klarer samvirk, men vil gjerne ha fast dugnadsjobb for å jobbe inn til BUK"
    };
}
public class PersonFollowUp
{
    public int ClubId { get; set; }
    public int PersonId { get; set; }
    public string Note { get; set; } = "";
    public WorkPreference? Preference { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public interface IPersonDirectory
{
    Task<ClubScope> GetAsync(CancellationToken ct = default);
}
public record PersonOverview(ClubMember Member, PersonFollowUp FollowUp);

public sealed class PersonService(IDbContextFactory<BukStatusDbContext> factory, IPersonDirectory directory, IWriteAccess access)
{
    public async Task<IReadOnlyList<PersonOverview>> GetAllAsync(CancellationToken ct = default)
    {
        await access.RequireAsync(ct);
        var scope = await directory.GetAsync(ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var saved = await db.PersonFollowUps.AsNoTracking().Where(p => p.ClubId == scope.Club.Id).ToDictionaryAsync(p => p.PersonId, ct);
        return scope.Club.Members.OrderBy(p => p.DisplayName).Select(p => new PersonOverview(p,
            saved.GetValueOrDefault(p.Id) ?? new() { ClubId = scope.Club.Id, PersonId = p.Id })).ToArray();
    }
    public async Task SaveAsync(int personId, string? note, WorkPreference? preference, CancellationToken ct = default)
    {
        await access.RequireAsync(ct);
        var scope = await directory.GetAsync(ct);
        if (!scope.Club.Members.Any(p => p.Id == personId)) throw new GroupException("Personen finnes ikke i valgt klubb.");
        if (preference.HasValue && !Enum.IsDefined(preference.Value)) throw new GroupException("Ugyldig valg.");
        note = note?.Trim() ?? "";
        if (note.Length > 5000) throw new GroupException("Notatet kan ha maksimalt 5000 tegn.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.PersonFollowUps.FindAsync([scope.Club.Id, personId], ct);
        if (row is null) { row = new() { ClubId = scope.Club.Id, PersonId = personId }; db.PersonFollowUps.Add(row); }
        row.Note = note; row.Preference = preference; row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
