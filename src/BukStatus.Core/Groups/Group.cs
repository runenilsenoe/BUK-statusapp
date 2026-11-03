namespace BukStatus.Core.Groups;

public class Group
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<GroupMember> Members { get; set; } = [];
}

/// <summary>A person (by BCC person id) in a group. A person can be in several groups.</summary>
public class GroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int PersonId { get; set; }

    /// <summary>
    /// Name typed in when adding the member. The Contributions API only returns person ids; looking names up
    /// would need the Core API and the persons.name#read scope.
    /// </summary>
    public string? DisplayName { get; set; }
}
