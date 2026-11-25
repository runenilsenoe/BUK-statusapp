using System.Globalization;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using BukStatus.Core.Pot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace BukStatus.Core.Transactions;

public record TransactionResult(IReadOnlyList<ExtraContribution> Rows, string Source, DateTimeOffset FetchedAt);
public sealed class TransactionService(IEnumerable<IExtraContributionsSource> sources, IClubContext clubs,
    IDbContextFactory<BukStatusDbContext> factory, IMemoryCache cache, BukStatus.Core.Administration.IWriteAccess access, IClubOrganisationResolver? organisations = null, IConfiguration? config = null)
{
    private readonly Guid cachePartition = Guid.NewGuid();
    public async Task<TransactionResult> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await access.RequireAsync(ct);
        return await ReadAsync(from, to, ct);
    }

    // The public overview may read aggregates only when its existing public-screen switch is enabled.
    // Raw transaction endpoints always use GetAsync and require administrator access.
    internal async Task<TransactionResult> GetForOverviewAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var db = await factory.OpenForClubAsync(clubs, ct);
        if (!clubs.Enabled && await db.PotSettings.AnyAsync(s => s.PublicScreenEnabled, ct))
            return await ReadAsync(from, to, ct);
        return await GetAsync(from, to, ct);
    }

    private async Task<TransactionResult> ReadAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 3660 || from.Year < 2000 || to.Year > 2099)
            throw new GroupException("Velg en gyldig periode på høyst ti år.");
        // Access is verified before consulting a cache shared across requests.
        var scope = await clubs.GetSelectedAsync(ct);
        if (clubs.Enabled && scope is null) throw new UnauthorizedAccessException();
        await using var db = await factory.OpenForClubAsync(clubs, ct);
        var settings = await db.PotSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PotSettings();
        // Club mode requires a verified club mapping. Legacy mode is restricted to the configured club
        // after the administrator check above; it does not need a browser's MyShare token.
        var myshareClubId = config?.GetValue<int?>("Myshare:ClubId");
        var legacyClubId = config?.GetValue<int?>("ClubAccess:LegacyClubId");
        var clubId = scope?.Club.Id ?? (myshareClubId is > 0 ? myshareClubId.Value : legacyClubId is > 0 ? legacyClubId.Value : 4050);
        IReadOnlyList<int> orgIds = organisations is null ? [] : await organisations.GetOrgIdsAsync(clubId, ct);
        if (orgIds.Count == 0 && scope is null && settings.ExtraOrgId is > 0) orgIds = [settings.ExtraOrgId.Value];
        var query = new ExtraContributionsQuery(orgIds.Count > 0 ? orgIds[0] : settings.ExtraOrgId,
            null, from, to, new HashSet<int>(), scope?.Club.Id ?? (organisations is null ? null : clubId), scope?.Club.TeamId, IncludeUnassigned: true);
        var eligible = sources.Where(s => s.CanHandle(query) && (scope is null || s is MyshareExtraContributionsSource || orgIds.Count > 0))
            .OrderBy(s => s is MyshareExtraContributionsSource ? 1 : 0).ToArray();
        var source = eligible.FirstOrDefault() ?? throw new GroupException("Ingen transaksjonskilde er tilgjengelig. Kontroller klubbkoblingen og lesetilgangen til Contributions.");
        var key = $"transactions:{cachePartition}:{clubId}:{scope?.Club.TeamId}:{string.Join(',', orgIds)}:{query.OrgId}:{source.Name}:{from}:{to}";
        if (!cache.TryGetValue(key, out TransactionResult? result) || result is null)
        {
            var rows = new List<ExtraContribution>();
            try
            {
                if (source is MyshareExtraContributionsSource || orgIds.Count == 0) rows.AddRange(await source.GetAsync(query, ct));
                else foreach (var orgId in orgIds) rows.AddRange(await source.GetAsync(query with { OrgId = orgId }, ct));
            }
            catch (MyshareDirectoryException) { throw; }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw new GroupException("Kunne ikke hente transaksjoner. Kontroller API-tilgang og prøv igjen."); }
            result = new(rows.Where(r => r.Date >= from && r.Date <= to).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToArray(), source.Name, DateTimeOffset.UtcNow);
            // Leave headroom for the overview's five-second refresh.
            cache.Set(key, result, TimeSpan.FromSeconds(4));
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
