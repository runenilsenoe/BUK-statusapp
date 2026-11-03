using BukStatus.Core.Groups;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Teams;

/// <summary>Teams, their members and the monthly target curve.</summary>
public class TeamService(IDbContextFactory<BukStatusDbContext> dbFactory)
{
    public async Task<List<Team>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Teams.AsNoTracking()
            .Include(t => t.Members)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Team?> GetAsync(int teamId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Teams.AsNoTracking()
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);
    }

    public async Task<Team> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        name = RequireName(name);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Teams.AnyAsync(t => t.Name == name, cancellationToken))
            throw new GroupException($"Det finnes allerede et lag som heter «{name}».");

        var sortOrder = await db.Teams.Select(t => (int?)t.SortOrder).MaxAsync(cancellationToken) ?? 0;
        var team = new Team { Name = name, SortOrder = sortOrder + 1 };
        db.Teams.Add(team);
        await db.SaveChangesAsync(cancellationToken);
        return team;
    }

    public async Task UpdateAsync(int teamId, string name, string? leaderName, decimal bonusPercent,
        CancellationToken cancellationToken = default)
    {
        name = RequireName(name);
        if (bonusPercent is < 0 or > 5)
            throw new GroupException("Tillegg må være mellom 0 og 500 %.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var team = await db.Teams.FindAsync([teamId], cancellationToken) ?? throw NotFound(teamId);
        if (await db.Teams.AnyAsync(t => t.Name == name && t.Id != teamId, cancellationToken))
            throw new GroupException($"Det finnes allerede et lag som heter «{name}».");

        team.Name = name;
        team.LeaderName = string.IsNullOrWhiteSpace(leaderName) ? null : leaderName.Trim();
        team.BonusPercent = bonusPercent;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int teamId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Teams.Where(t => t.Id == teamId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddMemberAsync(int teamId, int personId, string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (personId <= 0)
            throw new GroupException("Person-ID må være et positivt tall.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Teams.AnyAsync(t => t.Id == teamId, cancellationToken))
            throw NotFound(teamId);

        var existing = await db.TeamMembers.FirstOrDefaultAsync(m => m.PersonId == personId, cancellationToken);
        if (existing is not null)
        {
            var otherTeam = await db.Teams.Where(t => t.Id == existing.TeamId).Select(t => t.Name).FirstAsync(cancellationToken);
            throw new GroupException($"Person {personId} er allerede på laget «{otherTeam}». Fjern personen der først.");
        }

        db.TeamMembers.Add(new TeamMember { TeamId = teamId, PersonId = personId, DisplayName = Clean(displayName) });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(int teamId, int personId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.TeamMembers.Where(m => m.TeamId == teamId && m.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
    }

    public record ImportResult(int Added, int Moved, int Unchanged, int TeamsCreated, IReadOnlyList<string> Errors);

    /// <summary>
    /// Imports team assignments, one person per line: person id, name, team, separated by tab (pasted from
    /// Excel), semicolon or comma. Missing teams are created and people already on another team are moved.
    /// A header line and blank lines are skipped.
    /// </summary>
    public async Task<ImportResult> ImportAsync(string text, CancellationToken cancellationToken = default)
    {
        var rows = new List<(int PersonId, string? Name, string Team)>();
        var errors = new List<string>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split(line.Contains('\t') ? '\t' : line.Contains(';') ? ';' : ',').Select(p => p.Trim()).ToArray();
            if (!int.TryParse(parts[0], out var personId))
            {
                if (lineNumber > 1 || !parts[0].Contains("ID", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Linje {lineNumber}: «{parts[0]}» er ikke en person-ID.");
                continue;
            }
            if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[2]))
            {
                errors.Add($"Linje {lineNumber}: mangler lag.");
                continue;
            }
            rows.Add((personId, Clean(parts[1]), parts[2]));
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var teams = await db.Teams.ToDictionaryAsync(t => t.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var members = await db.TeamMembers.ToDictionaryAsync(m => m.PersonId, cancellationToken);
        int added = 0, moved = 0, unchanged = 0, created = 0;
        var nextSort = teams.Count == 0 ? 1 : teams.Values.Max(t => t.SortOrder) + 1;

        foreach (var (personId, name, teamName) in rows)
        {
            if (!teams.TryGetValue(teamName, out var team))
            {
                team = new Team { Name = teamName, SortOrder = nextSort++ };
                db.Teams.Add(team);
                await db.SaveChangesAsync(cancellationToken);
                teams[teamName] = team;
                created++;
            }

            if (members.TryGetValue(personId, out var member))
            {
                if (member.TeamId == team.Id)
                    unchanged++;
                else
                {
                    member.TeamId = team.Id;
                    moved++;
                }
                member.DisplayName = name ?? member.DisplayName;
            }
            else
            {
                member = new TeamMember { TeamId = team.Id, PersonId = personId, DisplayName = name };
                db.TeamMembers.Add(member);
                members[personId] = member;
                added++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ImportResult(added, moved, unchanged, created, errors);
    }

    /// <summary>Cumulative target per month (1-12) for the year. Months without a value are left out.</summary>
    public async Task<IReadOnlyDictionary<int, decimal>> GetMonthlyTargetsAsync(int year, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.MonthlyTargets.AsNoTracking()
            .Where(t => t.Year == year)
            .ToDictionaryAsync(t => t.Month, t => t.CumulativePercent, cancellationToken);
    }

    public async Task SaveMonthlyTargetsAsync(int year, IReadOnlyDictionary<int, decimal> cumulativeByMonth,
        CancellationToken cancellationToken = default)
    {
        decimal previous = 0;
        foreach (var month in Enumerable.Range(1, 12))
        {
            if (!cumulativeByMonth.TryGetValue(month, out var value))
                throw new GroupException($"Mangler mål for måned {month}.");
            if (value < previous || value > 1)
                throw new GroupException("Månedsmålene må øke måned for måned og ikke overstige 100 %.");
            previous = value;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.MonthlyTargets.Where(t => t.Year == year).ToDictionaryAsync(t => t.Month, cancellationToken);
        foreach (var (month, value) in cumulativeByMonth)
        {
            if (existing.TryGetValue(month, out var target))
                target.CumulativePercent = value;
            else
                db.MonthlyTargets.Add(new MonthlyTarget { Year = year, Month = month, CumulativePercent = value });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new GroupException("Laget må ha et navn.") : name.Trim();

    private static GroupException NotFound(int teamId) => new($"Fant ikke lag {teamId}.");
}
