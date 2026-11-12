using BukStatus.Core.Clubs;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Groups;

/// <summary>
/// Creates groups and manages their members. Uses a context factory so it works from Blazor Server components,
/// where one scope lives as long as the circuit.
/// </summary>
public class GroupService(IDbContextFactory<BukStatusDbContext> dbFactory, IClubContext? clubContext = null, BukStatus.Core.Administration.IWriteAccess? writeAccess = null)
{
    public async Task<List<Group>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
        var result = await db.Groups.AsNoTracking()
            .Include(g => g.Members)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);
        foreach (var item in result) await RefreshMembers(item, cancellationToken);
        return result;
    }

    public async Task<Group?> GetAsync(int groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
        var result = await db.Groups.AsNoTracking()
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (result is not null) await RefreshMembers(result, cancellationToken);
        return result;
    }

    public async Task<Group> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (writeAccess is not null) await writeAccess.RequireAsync(cancellationToken);
        name = RequireName(name);
        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
        if (await db.Groups.AnyAsync(g => g.Name == name, cancellationToken))
            throw new GroupException($"Det finnes allerede en gruppe som heter «{name}».");

        var group = new Group { Name = name };
        db.Groups.Add(group);
        await db.SaveChangesAsync(cancellationToken);
        return group;
    }

    public async Task RenameAsync(int groupId, string name, CancellationToken cancellationToken = default)
    {
        if (writeAccess is not null) await writeAccess.RequireAsync(cancellationToken);
        name = RequireName(name);
        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
        var group = await db.Groups.FindAsync([groupId], cancellationToken) ?? throw NotFound(groupId);
        if (await db.Groups.AnyAsync(g => g.Name == name && g.Id != groupId, cancellationToken))
            throw new GroupException($"Det finnes allerede en gruppe som heter «{name}».");

        group.Name = name;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int groupId, CancellationToken cancellationToken = default)
    {
        if (writeAccess is not null) await writeAccess.RequireAsync(cancellationToken);
        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
        await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<GroupMember> AddMemberAsync(int groupId, int personId, string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (writeAccess is not null) await writeAccess.RequireAsync(cancellationToken);
        displayName = await ClubData.RequireMemberAsync(clubContext, personId, displayName, cancellationToken);
        if (personId <= 0)
            throw new GroupException("Person-ID må være et positivt tall.");

        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
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
        if (writeAccess is not null) await writeAccess.RequireAsync(cancellationToken);
        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
        await db.GroupMembers
            .Where(m => m.GroupId == groupId && m.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task RefreshMembers(Group item, CancellationToken ct)
    {
        var scope = clubContext is null ? null : await clubContext.GetSelectedAsync(ct);
        if (scope is null) return;
        var members = scope.Club.Members.ToDictionary(m => m.Id);
        item.Members.RemoveAll(m => !members.ContainsKey(m.PersonId));
        foreach (var member in item.Members) member.DisplayName = members[member.PersonId].DisplayName;
    }

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new GroupException("Gruppen må ha et navn.") : name.Trim();

    private static GroupException NotFound(int groupId) => new($"Fant ikke gruppe {groupId}.");
}

/// <summary>A user-facing validation error, e.g. a duplicate group name.</summary>
public class GroupException(string message) : Exception(message);
