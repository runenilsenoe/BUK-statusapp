using BukStatus.Core.Status;

namespace BukStatus.Core.Groups;

public class GroupStatusService(GroupService groups, IMemberStatusService statuses)
{
    public async Task<GroupStatus?> GetAsync(int groupId, CancellationToken cancellationToken = default)
    {
        var group = await groups.GetAsync(groupId, cancellationToken);
        if (group is null)
            return null;

        var memberStatuses = await statuses.GetStatusesAsync(group.Members.Select(m => m.PersonId), cancellationToken);
        return Build(group, memberStatuses);
    }

    /// <summary>All groups, fetching statuses for everyone in one go (a person in several groups is fetched once).</summary>
    public async Task<List<GroupStatus>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var allGroups = await groups.GetAllAsync(cancellationToken);
        var personIds = allGroups.SelectMany(g => g.Members).Select(m => m.PersonId);
        var memberStatuses = await statuses.GetStatusesAsync(personIds, cancellationToken);
        return allGroups.Select(g => Build(g, memberStatuses)).ToList();
    }

    public static GroupStatus Build(Group group, IReadOnlyDictionary<int, MemberStatusSnapshot> memberStatuses)
    {
        var members = group.Members
            .Select(m => new GroupMemberStatus(m.PersonId, m.DisplayName, memberStatuses.GetValueOrDefault(m.PersonId)))
            .OrderBy(m => m.Status is null ? 2 : m.Status.IsOnTrack ? 1 : 0) // not ajour first, then ajour, then unknown
            .ThenByDescending(m => m.Status?.RemainingForOnTrack ?? 0)
            .ThenBy(m => m.DisplayName ?? m.PersonId.ToString())
            .ToList();
        return new GroupStatus(group.Id, group.Name, members);
    }
}
