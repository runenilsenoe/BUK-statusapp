namespace BukStatus.Core.Status;

public interface IMemberStatusService
{
    /// <summary>
    /// Statuses for the given persons, keyed by person id. Persons the API has no status for are left out.
    /// </summary>
    Task<IReadOnlyDictionary<int, MemberStatusSnapshot>> GetStatusesAsync(
        IEnumerable<int> personIds, CancellationToken cancellationToken = default);
}
