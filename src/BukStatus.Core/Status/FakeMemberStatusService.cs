namespace BukStatus.Core.Status;

/// <summary>
/// Made-up but stable statuses (derived from the person id) for local development without BCC credentials.
/// Enabled with BccApi:UseFakeData=true.
/// </summary>
public class FakeMemberStatusService : IMemberStatusService
{
    public Task<IReadOnlyDictionary<int, MemberStatusSnapshot>> GetStatusesAsync(
        IEnumerable<int> personIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<int, MemberStatusSnapshot> result = personIds
            .Distinct()
            .ToDictionary(id => id, Create);
        return Task.FromResult(result);
    }

    private static MemberStatusSnapshot Create(int personId)
    {
        var random = new Random(personId);
        var totalTarget = random.Next(10, 80) * 1000m;
        var ajourTarget = Math.Round(totalTarget * 0.6m);
        var contribution = Math.Round(totalTarget * (decimal)random.NextDouble() * 0.9m / 100) * 100;
        return new MemberStatusSnapshot(
            personId,
            contribution,
            totalTarget,
            ajourTarget,
            DateOnly.FromDateTime(DateTime.Today.AddDays(random.Next(-60, 60))),
            Math.Round(contribution / totalTarget * 100, 1));
    }
}
