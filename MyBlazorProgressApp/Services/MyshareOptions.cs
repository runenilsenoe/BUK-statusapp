namespace MyBlazorProgressApp.Services;

/// <summary>
/// Settings for the myshare admin API and the BCC Contributions API, bound from the "Myshare" section.
/// Real values live in appsettings.Development.json (not committed) or environment variables.
/// </summary>
public class MyshareOptions
{
    public const string SectionName = "Myshare";

    /// <summary>myshare admin API (proposals and club transactions).</summary>
    public string BaseUrl { get; set; } = "";
    public string Bearer { get; set; } = "";

    /// <summary>BCC Contributions API (member status).</summary>
    public string ContributionsBaseUrl { get; set; } = "";
    public string ContributionsBearer { get; set; } = "";

    public int ClubId { get; set; }
    public int TeamId { get; set; }
    public int SeasonId { get; set; }

    /// <summary>Persons whose targets should not count.</summary>
    public List<int> ExcludedPersonIds { get; set; } = [];

    /// <summary>Period to read club transactions for.</summary>
    public DateTime TransactionsFrom { get; set; }
    public DateTime TransactionsTo { get; set; }

    /// <summary>Transactions whose description contains this count as contributions outside targets.</summary>
    public string DonationDescription { get; set; } = "";

    public bool IsValid =>
        Uri.IsWellFormedUriString(BaseUrl, UriKind.Absolute) && !string.IsNullOrWhiteSpace(Bearer)
        && Uri.IsWellFormedUriString(ContributionsBaseUrl, UriKind.Absolute) && !string.IsNullOrWhiteSpace(ContributionsBearer)
        && ClubId > 0 && TeamId > 0 && SeasonId > 0
        && TransactionsTo > TransactionsFrom && !string.IsNullOrWhiteSpace(DonationDescription);
}
