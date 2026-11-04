using System.Globalization;
using BukStatus.Core.Groups;
using BukStatus.Core.Status;
using BukStatus.Core.Teams;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Pot;

/// <summary>Who the pot screen covers: everyone on a team, one team, or one group.</summary>
public record PotSource(string Key, string Name)
{
    public const string AllTeamsKey = "lag";

    public static string TeamKey(int teamId) => $"lag-{teamId.ToString(CultureInfo.InvariantCulture)}";
    public static string GroupKey(int groupId) => $"gruppe-{groupId.ToString(CultureInfo.InvariantCulture)}";
}

/// <param name="Simulated">The made-up amount added on top of the real extra contributions.</param>
public record PotScreen(PotSource Source, PotSettings Settings, decimal Simulated, PotResult Result)
{
    public decimal CombinedPercent => Result.CombinedPercent(Settings.SamvirkPercent);
}

/// <summary>Settings for and data behind the pot screen.</summary>
public class PotService(
    IDbContextFactory<BukStatusDbContext> dbFactory,
    TeamService teams,
    GroupService groups,
    IMemberStatusService statuses)
{
    public async Task<PotSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.PotSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new PotSettings();
    }

    public async Task SaveSettingsAsync(PotSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Title))
            throw new GroupException("Skjermen må ha en overskrift.");
        if (settings.SamvirkPercent is < 0 or > 100)
            throw new GroupException("Samvirk må være mellom 0 og 100 %.");
        if (settings.ExtraContributions < 0)
            throw new GroupException("Ekstrabidrag kan ikke være negativt.");

        var milestones = settings.Milestones.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (milestones.Any(m => !decimal.TryParse(m, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) || v is <= 0 or > 100))
            throw new GroupException("Målene må være tall mellom 1 og 100, skilt med semikolon (f.eks. 70;75).");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PotSettings.FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new PotSettings();
            db.PotSettings.Add(row);
        }

        row.Title = settings.Title.Trim();
        row.Subtitle = string.IsNullOrWhiteSpace(settings.Subtitle) ? null : settings.Subtitle.Trim();
        row.SamvirkPercent = settings.SamvirkPercent;
        row.ExtraContributions = settings.ExtraContributions;
        row.Milestones = string.Join(';', milestones);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>All teams first, then each team, then each group.</summary>
    public async Task<List<PotSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        var sources = new List<PotSource> { new(PotSource.AllTeamsKey, "Alle lag") };
        sources.AddRange((await teams.GetAllAsync(cancellationToken)).Select(t => new PotSource(PotSource.TeamKey(t.Id), $"Lag: {t.Name}")));
        sources.AddRange((await groups.GetAllAsync(cancellationToken)).Select(g => new PotSource(PotSource.GroupKey(g.Id), $"Gruppe: {g.Name}")));
        return sources;
    }

    /// <param name="sourceKey">See <see cref="PotSource"/>; null or unknown means all teams.</param>
    /// <param name="simulated">Made-up extra on top of the real extra contributions ("Simulere pott").</param>
    public async Task<PotScreen> GetScreenAsync(string? sourceKey, decimal simulated, PotStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var (source, members) = await GetMembersAsync(sourceKey, cancellationToken);
        var memberStatuses = await statuses.GetStatusesAsync(members.Select(m => m.PersonId), cancellationToken);

        simulated = Math.Max(0, simulated);
        var result = PotSimulator.Simulate(
            members.Select(m => new PotMember(m.PersonId, m.DisplayName, memberStatuses.GetValueOrDefault(m.PersonId))),
            settings.ExtraContributions + simulated,
            strategy);
        return new PotScreen(source, settings, simulated, result);
    }

    private async Task<(PotSource, List<(int PersonId, string? DisplayName)>)> GetMembersAsync(
        string? sourceKey, CancellationToken cancellationToken)
    {
        if (TryParseId(sourceKey, "gruppe-") is { } groupId && await groups.GetAsync(groupId, cancellationToken) is { } group)
            return (new PotSource(PotSource.GroupKey(group.Id), group.Name),
                group.Members.Select(m => (m.PersonId, m.DisplayName)).ToList());

        if (TryParseId(sourceKey, "lag-") is { } teamId && await teams.GetAsync(teamId, cancellationToken) is { } team)
            return (new PotSource(PotSource.TeamKey(team.Id), team.Name),
                team.Members.Select(m => (m.PersonId, m.DisplayName)).ToList());

        var all = await teams.GetAllAsync(cancellationToken);
        return (new PotSource(PotSource.AllTeamsKey, "Alle lag"),
            all.SelectMany(t => t.Members).Select(m => (m.PersonId, m.DisplayName)).ToList());
    }

    private static int? TryParseId(string? key, string prefix) =>
        key is not null && key.StartsWith(prefix, StringComparison.Ordinal)
        && int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
