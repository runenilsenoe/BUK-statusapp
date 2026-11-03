using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Groups;

/// <summary>
/// Creates groups and manages their members. Uses a context factory so it works from Blazor Server components,
/// where one scope lives as long as the circuit.
/// </summary>
public class GroupService(IDbContextFactory<BukStatusDbContext> dbFactory)
{
    public async Task<List<Group>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Groups.AsNoTracking()
            .Include(g => g.Members)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Group?> GetAsync(int groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Groups.AsNoTracking()
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
    }

    public async Task<Group> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        name = RequireName(name);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Groups.AnyAsync(g => g.Name == name, cancellationToken))
            throw new GroupException($"Det finnes allerede en gruppe som heter «{name}».");

        var group = new Group { Name = name };
        db.Groups.Add(group);
        await db.SaveChangesAsync(cancellationToken);
        return group;
    }

    public async Task RenameAsync(int groupId, string name, CancellationToken cancellationToken = default)
    {
        name = RequireName(name);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var group = await db.Groups.FindAsync([groupId], cancellationToken) ?? throw NotFound(groupId);
        if (await db.Groups.AnyAsync(g => g.Name == name && g.Id != groupId, cancellationToken))
            throw new GroupException($"Det finnes allerede en gruppe som heter «{name}».");

        group.Name = name;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<GroupMember> AddMemberAsync(int groupId, int personId, string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (personId <= 0)
            throw new GroupException("Person-ID må være et positivt tall.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Groups.AnyAsync(g => g.Id == groupId, cancellationToken))
            throw NotFound(groupId);
        if (await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.PersonId == personId, cancellationToken))
            throw new GroupException($"Person {personId} er allerede med i gruppen.");

        var member = new GroupMember
        {
            GroupId = groupId,
            PersonId = personId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
        };
        db.GroupMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task RemoveMemberAsync(int groupId, int personId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.GroupMembers
            .Where(m => m.GroupId == groupId && m.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new GroupException("Gruppen må ha et navn.") : name.Trim();

    private static GroupException NotFound(int groupId) => new($"Fant ikke gruppe {groupId}.");
}

/// <summary>A user-facing validation error, e.g. a duplicate group name.</summary>
public class GroupException(string message) : Exception(message);
