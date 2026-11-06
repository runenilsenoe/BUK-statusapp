using System.Globalization;

namespace BukStatus.Core.Pot;

/// <summary>
/// Settings for the pot screen ("Pott"), taken over from the old PACEFEST and MyBlazorProgressApp screens.
/// There is a single row (Id 1).
/// </summary>
public class PotSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Heading for the combined score, e.g. the region ("SØRLANDET").</summary>
    public string Title { get; set; } = "Samlet";

    /// <summary>Optional line under the heading, e.g. the donation site.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Samvirk's ajour share in percent (0-100). It's not in the BCC API, so it's typed in.</summary>
    public decimal SamvirkPercent { get; set; }

    /// <summary>
    /// Extra contributions (kr) typed in by hand: money collected outside the members' own targets, e.g. donations
    /// from people without a contribution plan. Used in manual mode, and as a fallback when fetching fails.
    /// </summary>
    public decimal ExtraContributions { get; set; }

    /// <summary>Goals for the combined score, in percent, separated by semicolons (e.g. "70;75").</summary>
    public string Milestones { get; set; } = "70;75";

    /// <summary>
    /// Whether the big screen at /skjerm is open without login. It shows totals and anonymous bars only, no names or
    /// amounts per person.
    /// </summary>
    public bool PublicScreenEnabled { get; set; }

    public ExtraContributionsMode ExtraMode { get; set; } = ExtraContributionsMode.Manual;

    /// <summary>BCC org id to read contributions for, in automatic mode.</summary>
    public int? ExtraOrgId { get; set; }

    /// <summary>Only contributions whose description contains this count (e.g. "Donation"). Empty counts all.</summary>
    public string? ExtraDescriptionFilter { get; set; }

    /// <summary>Start of the period; defaults to 1 January this year.</summary>
    public DateOnly? ExtraFrom { get; set; }

    /// <summary>End of the period (inclusive); defaults to today.</summary>
    public DateOnly? ExtraTo { get; set; }

    /// <summary>
    /// People whose contributions never count as extra, separated by semicolons (e.g. former members). Everyone on a
    /// team or in a group is always excluded.
    /// </summary>
    public string? ExtraExcludedPersonIds { get; set; }

    public IReadOnlyList<decimal> MilestoneList =>
        Split(Milestones)
            .Select(m => decimal.TryParse(m, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : -1)
            .Where(v => v is > 0 and <= 100)
            .Order()
            .ToList();

    public IReadOnlyList<int> ExcludedPersonIdList =>
        Split(ExtraExcludedPersonIds)
            .Select(v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

    public ExtraContributionsQuery ToQuery(IEnumerable<int> memberIds, DateOnly today) => new(
        ExtraOrgId,
        string.IsNullOrWhiteSpace(ExtraDescriptionFilter) ? null : ExtraDescriptionFilter.Trim(),
        ExtraFrom ?? new DateOnly(today.Year, 1, 1),
        ExtraTo ?? today,
        memberIds.Concat(ExcludedPersonIdList).ToHashSet());

    internal static string[] Split(string? value) =>
        (value ?? "").Split([';', ',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
