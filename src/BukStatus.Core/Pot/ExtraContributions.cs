using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BccCode.Contributions.Api.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BukStatus.Core.Pot;

/// <summary>Where the pot's extra contributions come from.</summary>
public enum ExtraContributionsMode
{
    /// <summary>The amount typed in under the settings.</summary>
    Manual,

    /// <summary>Fetched from the BCC API, with myshare as backup and the typed-in amount as last resort.</summary>
    Automatic,
}

/// <summary>One contribution that may count towards the pot.</summary>
public record ExtraContribution(int PersonId, DateOnly Date, decimal Amount, string? Description);

/// <param name="OrgId">BCC org id whose contributions to read (BCC API).</param>
/// <param name="DescriptionFilter">Only contributions whose description contains this count. Null counts all.</param>
/// <param name="To">Inclusive.</param>
/// <param name="ExcludedPersonIds">Members and others whose contributions are not extra.</param>
public record ExtraContributionsQuery(
    int? OrgId,
    string? DescriptionFilter,
    DateOnly From,
    DateOnly To,
    IReadOnlySet<int> ExcludedPersonIds);

/// <param name="Source">Human-readable source, shown on the page.</param>
/// <param name="Warning">Set when a source failed and a backup was used.</param>
public record ExtraContributionsResult(decimal Amount, int Count, string Source, bool Automatic, string? Warning);

/// <summary>A place to read contributions from. Sources are tried in registration order.</summary>
public interface IExtraContributionsSource
{
    string Name { get; }

    /// <summary>Whether the source has what it needs (credentials, ids) for the query.</summary>
    bool CanHandle(ExtraContributionsQuery query);

    Task<IReadOnlyList<ExtraContribution>> GetAsync(ExtraContributionsQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Reads extra contributions from the first source that works, filters and sums them. Results are cached briefly
/// so the pot screen's refresh doesn't hit the APIs every 30 seconds.
/// </summary>
public class ExtraContributionsService(
    IEnumerable<IExtraContributionsSource> sources,
    IMemoryCache cache,
    ILogger<ExtraContributionsService> logger)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private readonly List<IExtraContributionsSource> _sources = sources.ToList();

    /// <summary>The sources in the order they're tried, and whether each can handle the query.</summary>
    public IReadOnlyList<(string Name, bool Available)> Describe(ExtraContributionsQuery query) =>
        _sources.Select(s => (s.Name, s.CanHandle(query))).ToList();

    public async Task<ExtraContributionsResult> GetAsync(PotSettings settings, IReadOnlySet<int> memberIds,
        DateOnly today, CancellationToken cancellationToken = default)
    {
        if (settings.ExtraMode == ExtraContributionsMode.Manual)
            return Manual(settings, null);

        var query = settings.ToQuery(memberIds, today);
        var key = CacheKey(query);
        if (cache.TryGetValue(key, out ExtraContributionsResult? cached) && cached is not null)
            return cached;

        var failures = new List<string>();
        foreach (var source in _sources.Where(s => s.CanHandle(query)))
        {
            try
            {
                var all = await source.GetAsync(query, cancellationToken);
                var counted = Filter(all, query);
                var result = new ExtraContributionsResult(counted.Sum(c => c.Amount), counted.Count, source.Name, true,
                    failures.Count > 0 ? $"Brukte {source.Name} fordi {string.Join(" og ", failures)} feilet." : null);
                cache.Set(key, result, CacheDuration);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not read extra contributions from {Source}", source.Name);
                failures.Add(source.Name);
            }
        }

        var warning = failures.Count > 0
            ? $"Klarte ikke å hente ekstrabidrag fra {string.Join(" eller ", failures)}. Viser beløpet som er lagt inn manuelt."
            : "Ingen kilde for ekstrabidrag er satt opp. Viser beløpet som er lagt inn manuelt.";
        var fallback = Manual(settings, warning);
        // Cache failures briefly as well, so a down API isn't hammered.
        cache.Set(key, fallback, CacheDuration);
        return fallback;
    }

    /// <summary>Contributions in the period, matching the description filter, from people who aren't excluded.</summary>
    public static List<ExtraContribution> Filter(IEnumerable<ExtraContribution> contributions, ExtraContributionsQuery query) =>
        contributions
            .Where(c => c.Date >= query.From && c.Date <= query.To)
            .Where(c => !query.ExcludedPersonIds.Contains(c.PersonId))
            .Where(c => string.IsNullOrWhiteSpace(query.DescriptionFilter)
                        || (c.Description?.Contains(query.DescriptionFilter.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

    private static ExtraContributionsResult Manual(PotSettings settings, string? warning) =>
        new(settings.ExtraContributions, 0, "lagt inn manuelt", false, warning);

    private static string CacheKey(ExtraContributionsQuery q) =>
        $"extra:{q.OrgId}:{q.DescriptionFilter}:{q.From:O}:{q.To:O}:{string.Join(',', q.ExcludedPersonIds.Order())}";
}

/// <summary>Contributions to an org from the BCC Contributions API (the contributions endpoint).</summary>
public class BccExtraContributionsSource(IContributionsApiClient client) : IExtraContributionsSource
{
    private const int PageSize = 1000;
    private const int MaxPages = 50;

    public string Name => "BCC-API-et";

    public bool CanHandle(ExtraContributionsQuery query) => query.OrgId is > 0;

    public async Task<IReadOnlyList<ExtraContribution>> GetAsync(ExtraContributionsQuery query, CancellationToken cancellationToken)
    {
        var orgId = query.OrgId!.Value;
        // A day of margin on each side; dates are compared in Norwegian time afterwards.
        var from = query.From.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = query.To.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var result = new List<ExtraContribution>();
        for (var page = 0; page < MaxPages; page++)
        {
            var batch = await client.Contributions.QueryAsync(
                c => c.OrgID == orgId && c.ContributionDate >= from && c.ContributionDate < to,
                limit: PageSize, offset: page * PageSize, sort: "contributionDate",
                cancellationToken: cancellationToken);
            var data = batch?.Data ?? [];
            result.AddRange(data.Select(c => new ExtraContribution(c.PersonID, Dates.ToNorwegianDate(c.ContributionDate), c.Amount, c.Description)));
            if (data.Count < PageSize)
                break;
        }
        return result;
    }
}

/// <summary>
/// Settings for the myshare admin API, bound from the "Myshare" section. Backup for the BCC API, as used by the old
/// PACEFEST screen. Keep the bearer token out of appsettings.json: use Myshare__Bearer.
/// </summary>
public class MyshareOptions
{
    public const string SectionName = "Myshare";

    public string BaseUrl { get; set; } = "";
    public string Bearer { get; set; } = "";
    public int ClubId { get; set; }

    public bool IsConfigured =>
        Uri.IsWellFormedUriString(BaseUrl, UriKind.Absolute) && !string.IsNullOrWhiteSpace(Bearer) && ClubId > 0;
}

/// <summary>Club transactions from the myshare admin API (ClubTransactions), the old PACEFEST source.</summary>
public class MyshareExtraContributionsSource(IHttpClientFactory httpClientFactory, IOptions<MyshareOptions> options) : IExtraContributionsSource
{
    public const string HttpClientName = "myshare";

    private readonly MyshareOptions _options = options.Value;

    public string Name => "myshare";

    public bool CanHandle(ExtraContributionsQuery query) => _options.IsConfigured;

    public async Task<IReadOnlyList<ExtraContribution>> GetAsync(ExtraContributionsQuery query, CancellationToken cancellationToken)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/ClubTransactions/?clubId={_options.ClubId.ToString(CultureInfo.InvariantCulture)}"
                  + $"&fromDate={QueryDate(query.From.ToDateTime(TimeOnly.MinValue))}"
                  + $"&toDate={QueryDate(query.To.ToDateTime(new TimeOnly(23, 59, 59)))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Bearer);
        using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<MyshareResponse>(cancellationToken)
                   ?? throw new InvalidOperationException("Tomt svar fra myshare.");
        if (body.StatusCode is not null and not 200)
            throw new InvalidOperationException($"myshare svarte med status {body.StatusCode}.");

        return (body.Data ?? [])
            .Where(t => t.UserId is not null && t.Amount is not null && t.TransactionDate is not null)
            .Select(t => new ExtraContribution(t.UserId!.Value, DateOnly.FromDateTime(t.TransactionDate!.Value), t.Amount!.Value, t.Description))
            .ToList();
    }

    private static string QueryDate(DateTime date) =>
        Uri.EscapeDataString(date.ToString("yyyy-MM-dd H:mm:ss", CultureInfo.InvariantCulture));

    private sealed class MyshareResponse
    {
        public int? StatusCode { get; set; }
        public List<MyshareTransaction>? Data { get; set; }
    }

    private sealed class MyshareTransaction
    {
        public string? Description { get; set; }
        public DateTime? TransactionDate { get; set; }
        public int? UserId { get; set; }
        public decimal? Amount { get; set; }
    }
}

/// <summary>Made-up donations from non-members for local development (BccApi:UseFakeData=true).</summary>
public class FakeExtraContributionsSource : IExtraContributionsSource
{
    public string Name => "testdata";

    public bool CanHandle(ExtraContributionsQuery query) => true;

    public Task<IReadOnlyList<ExtraContribution>> GetAsync(ExtraContributionsQuery query, CancellationToken cancellationToken)
    {
        var random = new Random(query.From.DayNumber);
        var list = new List<ExtraContribution>();
        for (var date = query.From; date <= query.To; date = date.AddDays(random.Next(2, 9)))
            list.Add(new ExtraContribution(900_000 + random.Next(1, 40), date, random.Next(5, 60) * 100m, "Donation BUK"));
        return Task.FromResult<IReadOnlyList<ExtraContribution>>(list);
    }
}

internal static class Dates
{
    private static readonly TimeZoneInfo Norway = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    /// <summary>The BCC API returns UTC; the period is in Norwegian dates.</summary>
    public static DateOnly ToNorwegianDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Norway));
}
