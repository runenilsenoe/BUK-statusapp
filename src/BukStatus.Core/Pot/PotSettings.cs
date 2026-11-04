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
    /// Extra contributions (kr) collected outside the members' own targets, e.g. donations from people without a
    /// contribution plan. The pot is spread over the members who aren't ajour.
    /// </summary>
    public decimal ExtraContributions { get; set; }

    /// <summary>Goals for the combined score, in percent, separated by semicolons (e.g. "70;75").</summary>
    public string Milestones { get; set; } = "70;75";

    public IReadOnlyList<decimal> MilestoneList =>
        Milestones.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => decimal.TryParse(m, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1)
            .Where(v => v is > 0 and <= 100)
            .Order()
            .ToList();
}
