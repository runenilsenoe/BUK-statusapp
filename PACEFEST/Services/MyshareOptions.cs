namespace PACEFEST.Services;

/// <summary>
/// Settings for the myshare admin API, bound from the "Myshare" section. Real values live in
/// appsettings.Development.json (not committed) or environment variables (Myshare__ClubId, ...).
/// </summary>
public class MyshareOptions
{
    public const string SectionName = "Myshare";

    public string BaseUrl { get; set; } = "";
    public string Bearer { get; set; } = "";
    public int ClubId { get; set; }
    public int TeamId { get; set; }
    public int SeasonId { get; set; }

    /// <summary>Persons whose targets should not count.</summary>
    public List<int> ExcludedPersonIds { get; set; } = [];

    /// <summary>Former members whose donations should not count as bonus contributions.</summary>
    public List<int> PreviouslyActiveUserIds { get; set; } = [];

    /// <summary>Period to read club transactions for.</summary>
    public DateTime TransactionsFrom { get; set; }
    public DateTime TransactionsTo { get; set; }

    /// <summary>Transactions whose description contains this count as bonus contributions.</summary>
    public string DonationDescription { get; set; } = "";

    public bool IsValid =>
        Uri.IsWellFormedUriString(BaseUrl, UriKind.Absolute) && !string.IsNullOrWhiteSpace(Bearer)
        && ClubId > 0 && TeamId > 0 && SeasonId > 0
        && TransactionsTo > TransactionsFrom && !string.IsNullOrWhiteSpace(DonationDescription);
}
