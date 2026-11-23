using BukStatus.Core.Clubs;
using BukStatus.Core.Samvirk;
using System.Globalization;
using BukStatus.Core.Groups;
using BukStatus.Core.Status;
using BukStatus.Core.Teams;
using Microsoft.EntityFrameworkCore;

namespace BukStatus.Core.Pot;

/// <summary>Resolves a complete authorized club roster, also for the legacy site's signed-in users.</summary>
public interface IPotRoster
{
    Task<ClubScope?> GetAsync(CancellationToken ct = default);
}

/// <summary>Who the pot screen covers: all club members, one team, or one group.</summary>
public record PotSource(string Key, string Name)
{
    public const string AllTeamsKey = "lag";

    public static string TeamKey(int teamId) => $"lag-{teamId.ToString(CultureInfo.InvariantCulture)}";
    public static string GroupKey(int groupId) => $"gruppe-{groupId.ToString(CultureInfo.InvariantCulture)}";
}

/// <param name="Extra">The real extra contributions and where they came from.</param>
/// <param name="Simulated">The made-up amount added on top of the real extra contributions.</param>
public record PotScreen(PotSource Source, PotSettings Settings, ExtraContributionsResult Extra, decimal Simulated, PotResult Result)
{
    public SamvirkProgress? Samvirk { get; init; }
    public decimal? SamvirkPercent => Samvirk is null ? Settings.SamvirkPercent : Samvirk.Percent;
    public decimal? CombinedPercent => Result.Count > 0 && SamvirkPercent is { } percent ? Result.CombinedPercent(percent) : null;
}

/// <summary>Settings for and data behind the pot screen.</summary>
public class PotService(
    IDbContextFactory<BukStatusDbContext> dbFactory,
    TeamService teams,
    GroupService groups,
    IMemberStatusService statuses,
    ExtraContributionsService extraContributions,
    TimeProvider time, IClubContext? clubContext = null, BukStatus.Core.Administration.IWriteAccess? writeAccess = null, IPotRoster? roster = null, IMyshareDirectory? myshare = null)
{
    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    public async Task<PotSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
        return await db.PotSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new PotSettings();
    }

    public async Task SaveSettingsAsync(PotSettings settings, CancellationToken cancellationToken = default)
    {
        if (writeAccess is not null) await writeAccess.RequireAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.Title))
            throw new GroupException("Skjermen må ha en overskrift.");
        if (settings.SamvirkPercent is < 0 or > 100)
            throw new GroupException("Samvirk må være mellom 0 og 100 %.");
        if (settings.ExtraContributions < 0)
            throw new GroupException("Ekstrabidrag kan ikke være negativt.");

        var milestones = PotSettings.Split(settings.Milestones);
        if (milestones.Any(m => !decimal.TryParse(m, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) || v is <= 0 or > 100))
            throw new GroupException("Målene må være tall mellom 1 og 100, skilt med semikolon (f.eks. 70;75).");

        var excluded = PotSettings.Split(settings.ExtraExcludedPersonIds);
        if (excluded.Any(v => !int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0))
            throw new GroupException("Unntatte personer må være person-ID-er skilt med semikolon.");
        if (settings.ExtraOrgId is <= 0)
            throw new GroupException("Org-ID må være et positivt tall.");
        if (settings.ExtraFrom is { } from && settings.ExtraTo is { } to && to < from)
            throw new GroupException("Til-datoen kan ikke være før fra-datoen.");

        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
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
        row.PublicScreenEnabled = settings.PublicScreenEnabled;
        row.ExtraMode = settings.ExtraMode;
        row.ExtraOrgId = settings.ExtraOrgId;
        row.ExtraDescriptionFilter = string.IsNullOrWhiteSpace(settings.ExtraDescriptionFilter) ? null : settings.ExtraDescriptionFilter.Trim();
        row.ExtraFrom = settings.ExtraFrom;
        row.ExtraTo = settings.ExtraTo;
        row.ExtraExcludedPersonIds = excluded.Length == 0 ? null : string.Join(';', excluded);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>All club members first, then each team, then each group.</summary>
    public async Task<List<PotSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var sources = new List<PotSource> { new(PotSource.AllTeamsKey, scope is null ? "Alle registrerte personer" : $"Alle i {scope.Club.Name}") };
        sources.AddRange((await teams.GetAllAsync(cancellationToken)).Select(t => new PotSource(PotSource.TeamKey(t.Id), $"Lag: {t.Name}")));
        sources.AddRange((await groups.GetAllAsync(cancellationToken)).Select(g => new PotSource(PotSource.GroupKey(g.Id), $"Gruppe: {g.Name}")));
        return sources;
    }

    /// <param name="sourceKey">See <see cref="PotSource"/>; null or unknown means all club members.</param>
    /// <param name="simulated">Made-up extra on top of the real extra contributions ("Simulere pott").</param>
    public async Task<PotScreen> GetScreenAsync(string? sourceKey, decimal simulated, PotStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var (source, members) = await GetMembersAsync(sourceKey, cancellationToken);
        var scope = await GetScopeAsync(cancellationToken);
        var campaign = scope is null || myshare is null ? null : await myshare.GetCampaignAsync(scope, Today, cancellationToken);
        var campaignById = campaign?.ToDictionary(p => p.PersonId);
        var rosterById = scope?.Club.Members.ToDictionary(m => m.Id);
        await using var centralDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var excluded = scope is null ? new HashSet<int>() : (await centralDb.PersonFollowUps.AsNoTracking()
            .Where(p => p.ClubId == scope.Club.Id && p.ExcludedFromCampaign).Select(p => p.PersonId).ToListAsync(cancellationToken)).ToHashSet();
        members = members.Where(m => !excluded.Contains(m.PersonId)
            && (campaignById is null || campaignById.TryGetValue(m.PersonId, out var participant)
                && participant.IsCountedInAksjon == true))
            .Select(m => (m.PersonId, DisplayName: FirstName(rosterById?.GetValueOrDefault(m.PersonId)?.DisplayName,
                campaignById?.GetValueOrDefault(m.PersonId)?.Name, m.DisplayName))).ToList();
        var samvirkMemberIds = members.Select(m => m.PersonId).ToArray();
        var statusTask = statuses.GetStatusesAsync(members.Select(m => m.PersonId), cancellationToken);
        var extraTask = GetExtraContributionsAsync(settings, cancellationToken);
        var memberStatuses = await statusTask;
        var extra = await extraTask;

        simulated = Math.Max(0, simulated);
        var result = PotSimulator.Simulate(
            members.Where(m => campaignById is null || campaignById.GetValueOrDefault(m.PersonId)?.TargetAmount > 0).Select(m => new PotMember(m.PersonId, m.DisplayName, memberStatuses.GetValueOrDefault(m.PersonId))),
            extra.Amount + simulated,
            strategy);
        await using var db = await dbFactory.OpenForClubAsync(clubContext, cancellationToken);
        var snapshot = await SamvirkService.ReadLatestAsync(db, cancellationToken);
        return new PotScreen(source, settings, extra, simulated, result)
        {
            Samvirk = snapshot is null ? null : SamvirkProgress.Build(snapshot, samvirkMemberIds)
        };
    }

    /// <summary>
    /// The real extra contributions. Everyone in the club counts as a member, so their own
    /// contributions are never extra, whichever source the screen shows.
    /// </summary>
    public async Task<ExtraContributionsResult> GetExtraContributionsAsync(PotSettings settings, CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(cancellationToken);
        return await extraContributions.GetAsync(settings, await GetAllMemberIdsAsync(cancellationToken), Today, cancellationToken,
            scope?.Club.Id, scope?.Club.TeamId);
    }

    /// <summary>The extra-contribution sources in the order they're tried, and whether each is set up.</summary>
    public async Task<IReadOnlyList<(string Name, bool Available)>> DescribeExtraSourcesAsync(PotSettings settings,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetScopeAsync(cancellationToken);
        return extraContributions.Describe(settings.ToQuery(await GetAllMemberIdsAsync(cancellationToken), Today)
            with { ClubId = scope?.Club.Id, TeamId = scope?.Club.TeamId });
    }

    private async Task<HashSet<int>> GetAllMemberIdsAsync(CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        if (scope is not null) return scope.Club.Members.Select(m => m.Id).ToHashSet();
        var ids = (await teams.GetAllAsync(cancellationToken)).SelectMany(t => t.Members).Select(m => m.PersonId).ToHashSet();
        ids.UnionWith((await groups.GetAllAsync(cancellationToken)).SelectMany(g => g.Members).Select(m => m.PersonId));
        return ids;
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

        var scope = await GetScopeAsync(cancellationToken);
        if (scope is not null)
            return (new PotSource(PotSource.AllTeamsKey, scope.Club.Name),
                scope.Club.Members.Select(m => (m.Id, (string?)m.DisplayName)).ToList());

        // Local-only development / anonymous legacy screen: include group-only people too.
        var all = (await teams.GetAllAsync(cancellationToken)).SelectMany(t => t.Members).Select(m => (m.PersonId, m.DisplayName))
            .Concat((await groups.GetAllAsync(cancellationToken)).SelectMany(g => g.Members).Select(m => (m.PersonId, m.DisplayName)));
        return (new PotSource(PotSource.AllTeamsKey, "Alle registrerte personer"),
            all.DistinctBy(m => m.PersonId).ToList());
    }

    private async Task<ClubScope?> GetScopeAsync(CancellationToken ct)
    {
        var selected = clubContext is null ? null : await clubContext.GetSelectedAsync(ct);
        return selected ?? (roster is null ? null : await roster.GetAsync(ct));
    }

    private static string? FirstName(params string?[] names) => names.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

    private static int? TryParseId(string? key, string prefix) =>
        key is not null && key.StartsWith(prefix, StringComparison.Ordinal)
        && int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
