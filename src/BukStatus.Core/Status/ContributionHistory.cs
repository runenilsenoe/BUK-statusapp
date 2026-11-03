using BccCode.Contributions.Api.Client;
using BccCode.Platform;
using Microsoft.Extensions.Caching.Memory;

namespace BukStatus.Core.Status;

/// <summary>One registered contribution (donation, dugnad pay etc.) for a person.</summary>
public record ContributionEntry(int PersonId, DateOnly Date, decimal Amount, string? Description);

public interface IContributionHistoryService
{
    /// <summary>The persons' contributions in the current BCC season, keyed by person id.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<ContributionEntry>>> GetContributionsAsync(
        IEnumerable<int> personIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads contributions per person from the BCC Contributions API (memberClubContributions). The API has no
/// batch endpoint for this, so calls run a few at a time and results are cached for a while.
/// </summary>
public class BccContributionHistoryService(IContributionsApiClient client, IMemoryCache cache) : IContributionHistoryService
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const int MaxParallelCalls = 6;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<ContributionEntry>>> GetContributionsAsync(
        IEnumerable<int> personIds, CancellationToken cancellationToken = default)
    {
        var result = new System.Collections.Concurrent.ConcurrentDictionary<int, IReadOnlyList<ContributionEntry>>();
        await Parallel.ForEachAsync(personIds.Distinct(),
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelCalls, CancellationToken = cancellationToken },
            async (personId, _) => result[personId] = await GetForPersonAsync(personId));
        return result;
    }

    private async Task<IReadOnlyList<ContributionEntry>> GetForPersonAsync(int personId)
    {
        var key = $"contributions:{personId}";
        if (cache.TryGetValue(key, out IReadOnlyList<ContributionEntry>? cached) && cached is not null)
            return cached;

        List<ContributionEntry> entries;
        try
        {
            var contributions = await client.MemberStatuses.GetContributions(personId) ?? [];
            entries = contributions
                .Select(c => new ContributionEntry(c.PersonID, ToNorwegianDate(c.ContributionDate), c.Amount, c.Description))
                .ToList();
        }
        catch (ApiRequestException ex) when (ex.StatusCode == 404)
        {
            entries = [];
        }

        cache.Set(key, (IReadOnlyList<ContributionEntry>)entries, CacheDuration);
        return entries;
    }

    private static readonly TimeZoneInfo Norway = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    /// <summary>The API returns UTC; weeks and months in the reports follow Norwegian dates.</summary>
    private static DateOnly ToNorwegianDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Norway));
}

/// <summary>Made-up but stable contributions for local development (BccApi:UseFakeData=true).</summary>
public class FakeContributionHistoryService(TimeProvider time) : IContributionHistoryService
{
    public Task<IReadOnlyDictionary<int, IReadOnlyList<ContributionEntry>>> GetContributionsAsync(
        IEnumerable<int> personIds, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        IReadOnlyDictionary<int, IReadOnlyList<ContributionEntry>> result = personIds.Distinct()
            .ToDictionary(id => id, id => Create(id, today));
        return Task.FromResult(result);
    }

    private static IReadOnlyList<ContributionEntry> Create(int personId, DateOnly today)
    {
        var random = new Random(personId * 7919);
        var entries = new List<ContributionEntry>();
        for (var date = new DateOnly(today.Year, 1, 1).AddDays(random.Next(0, 10)); date <= today; date = date.AddDays(random.Next(5, 21)))
        {
            var dugnad = random.NextDouble() < 0.4;
            entries.Add(new ContributionEntry(personId, date,
                dugnad ? random.Next(2, 9) * 150m : random.Next(5, 40) * 100m,
                dugnad ? "Dugnad" : "Contribution from Donation BUK"));
        }
        return entries;
    }
}
