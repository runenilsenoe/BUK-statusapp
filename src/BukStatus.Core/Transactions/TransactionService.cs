using System.Globalization;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using BukStatus.Core.Pot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BukStatus.Core.Transactions;

public record TransactionResult(IReadOnlyList<ExtraContribution> Rows, string Source, DateTimeOffset FetchedAt);
public sealed class TransactionService(IEnumerable<IExtraContributionsSource> sources, IClubContext clubs,
    IDbContextFactory<BukStatusDbContext> factory, IMemoryCache cache, BukStatus.Core.Administration.IWriteAccess access)
{
    public async Task<TransactionResult> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await access.RequireAsync(ct);
        if (to < from || to.DayNumber - from.DayNumber > 3660 || from.Year < 2000 || to.Year > 2099)
            throw new GroupException("Velg en gyldig periode på høyst ti år.");
        // Access is verified before consulting a cache shared across requests.
        var scope = await clubs.GetSelectedAsync(ct);
        if (clubs.Enabled && scope is null) throw new UnauthorizedAccessException();
        await using var db = await factory.OpenForClubAsync(clubs, ct);
        var settings = await db.PotSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PotSettings();
        var query = new ExtraContributionsQuery(settings.ExtraOrgId, null, from, to, new HashSet<int>(), scope?.Club.Id, scope?.Club.TeamId, IncludeUnassigned: true);
        var eligible = sources.Where(s => s.CanHandle(query) && (scope is null || s is MyshareExtraContributionsSource))
            .OrderBy(s => s is MyshareExtraContributionsSource ? 0 : 1).ToArray();
        var source = eligible.FirstOrDefault() ?? throw new GroupException("Ingen transaksjonskilde er konfigurert for klubben. Kontroller MyShare-tilgangen eller BCC org-ID under Pott-innstillinger.");
        var key = $"transactions:{scope?.Club.Id}:{scope?.Club.TeamId}:{query.OrgId}:{source.Name}:{from}:{to}";
        if (!cache.TryGetValue(key, out TransactionResult? result) || result is null)
        {
            IReadOnlyList<ExtraContribution> rows;
            try { rows = await source.GetAsync(query, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw new GroupException("Kunne ikke hente transaksjoner. Kontroller API-tilgang og prøv igjen."); }
            result = new(rows.Where(r => r.Date >= from && r.Date <= to).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToArray(), source.Name, DateTimeOffset.UtcNow);
            cache.Set(key, result, TimeSpan.FromMinutes(10));
        }
        var names = scope?.Club.Members.ToDictionary(m => m.Id, m => m.DisplayName);
        return result with { Rows = result.Rows.Select(r => r with { PersonName = r.PersonName ?? names?.GetValueOrDefault(r.PersonId) }).ToArray() };
    }
    public static IReadOnlyList<ExtraContribution> Search(IEnumerable<ExtraContribution> rows, string? text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return rows.Where(r => words.All(word => new[] { r.PersonName, r.Description, r.PersonId.ToString(), r.Id?.ToString(), r.Date.ToString("dd.MM.yyyy"), r.Date.ToString("yyyy-MM-dd"), r.Amount.ToString(CultureInfo.GetCultureInfo("nb-NO")), r.Amount.ToString(CultureInfo.InvariantCulture) }
            .Any(value => value?.Contains(word, StringComparison.OrdinalIgnoreCase) == true))).ToArray();
    }
}
