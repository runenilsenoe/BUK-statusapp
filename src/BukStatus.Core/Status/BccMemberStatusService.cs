using BccCode.Contributions.Api.Client;
using Microsoft.Extensions.Caching.Memory;

namespace BukStatus.Core.Status;

/// <summary>
/// Reads member statuses from the BCC Contributions API. Statuses are cached briefly so page loads and bot
/// commands don't each hit the API, and lookups are split into batches since the API caps a batch at 200.
/// </summary>
public class BccMemberStatusService(IContributionsApiClient client, IMemoryCache cache) : IMemberStatusService
{
    private const int MaxBatchSize = 200;
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyDictionary<int, MemberStatusSnapshot>> GetStatusesAsync(
        IEnumerable<int> personIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<int, MemberStatusSnapshot>();
        var missing = new List<int>();

        foreach (var personId in personIds.Distinct())
        {
            if (cache.TryGetValue(CacheKey(personId), out MemberStatusSnapshot? cached) && cached is not null)
                result[personId] = cached;
            else
                missing.Add(personId);
        }

        foreach (var batch in missing.Chunk(MaxBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statuses = await client.MemberStatuses.GetBatch(batch) ?? [];
            foreach (var status in statuses)
            {
                var snapshot = new MemberStatusSnapshot(
                    status.PersonID,
                    status.TotalContribution,
                    status.TotalAksjonTarget,
                    status.AjourTarget,
                    status.AjourUntil == default ? null : status.AjourUntil,
                    status.Score);
                cache.Set(CacheKey(status.PersonID), snapshot, CacheDuration);
                result[status.PersonID] = snapshot;
            }
        }

        return result;
    }

    private static string CacheKey(int personId) => $"member-status:{personId}";
}
