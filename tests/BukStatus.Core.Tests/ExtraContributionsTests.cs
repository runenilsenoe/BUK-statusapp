using System.Net;
using System.Text;
using BukStatus.Core.Pot;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BukStatus.Core.Tests;

public class ExtraContributionsTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static PotSettings Automatic(string? filter = null) => new()
    {
        ExtraMode = ExtraContributionsMode.Automatic,
        ExtraOrgId = 5,
        ExtraDescriptionFilter = filter,
        ExtraContributions = 777,
        ExtraExcludedPersonIds = "3",
    };

    private static ExtraContributionsService Service(params IExtraContributionsSource[] sources) =>
        new(sources, new MemoryCache(new MemoryCacheOptions()), NullLogger<ExtraContributionsService>.Instance);

    private static readonly ExtraContribution[] Contributions =
    [
        new(1, new DateOnly(2026, 3, 1), 1_000, "Donation BUK"),
        new(2, new DateOnly(2026, 3, 2), 500, "Dugnad"),
        new(3, new DateOnly(2026, 3, 3), 2_000, "Donation BUK"),    // excluded in settings
        new(4, new DateOnly(2026, 3, 4), 4_000, "donation buk"),    // member (on a team)
        new(5, new DateOnly(2025, 12, 31), 8_000, "Donation BUK"),  // before the period
    ];

    [Fact]
    public async Task Manual_mode_uses_the_typed_in_amount()
    {
        var source = new StubSource("BCC", Contributions);
        var result = await Service(source).GetAsync(new PotSettings { ExtraContributions = 777 }, new HashSet<int>(), Today);

        Assert.Equal(777m, result.Amount);
        Assert.False(result.Automatic);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Sums_the_period_without_members_and_excluded_people()
    {
        var result = await Service(new StubSource("BCC", Contributions)).GetAsync(Automatic(), new HashSet<int> { 4 }, Today);

        Assert.Equal(1_500m, result.Amount);
        Assert.Equal(2, result.Count);
        Assert.Equal("BCC", result.Source);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Description_filter_ignores_case()
    {
        var result = await Service(new StubSource("BCC", Contributions)).GetAsync(Automatic("donation"), new HashSet<int>(), Today);

        Assert.Equal(5_000m, result.Amount); // persons 1 and 4
    }

    [Fact]
    public async Task Falls_back_to_myshare_when_the_bcc_api_fails()
    {
        var result = await Service(new StubSource("BCC", fail: true), new StubSource("myshare", Contributions))
            .GetAsync(Automatic(), new HashSet<int>(), Today);

        Assert.Equal("myshare", result.Source);
        Assert.Equal(5_500m, result.Amount);
        Assert.Contains("BCC", result.Warning);
    }

    [Fact]
    public async Task Skips_sources_that_are_not_set_up()
    {
        var bcc = new StubSource("BCC", Contributions, canHandle: false);
        var result = await Service(bcc, new StubSource("myshare", [])).GetAsync(Automatic(), new HashSet<int>(), Today);

        Assert.Equal("myshare", result.Source);
        Assert.Equal(0, bcc.Calls);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Uses_the_typed_in_amount_when_every_source_fails()
    {
        var result = await Service(new StubSource("BCC", fail: true), new StubSource("myshare", fail: true))
            .GetAsync(Automatic(), new HashSet<int>(), Today);

        Assert.Equal(777m, result.Amount);
        Assert.False(result.Automatic);
        Assert.Contains("BCC eller myshare", result.Warning);
    }

    [Fact]
    public async Task Results_are_cached()
    {
        var source = new StubSource("BCC", Contributions);
        var service = Service(source);
        await service.GetAsync(Automatic(), new HashSet<int>(), Today);
        await service.GetAsync(Automatic(), new HashSet<int>(), Today);

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Myshare_reads_club_transactions_with_the_bearer_token()
    {
        const string json = """
            {"status":"OK","statusCode":200,"data":[
              {"id":1,"description":"Donation BUK","transactionDate":"2026-05-01T10:00:00","userId":42,"amount":250.5},
              {"id":2,"description":"Uten bruker","transactionDate":"2026-05-02T10:00:00","userId":null,"amount":99}
            ]}
            """;
        var handler = new StubHandler(json);
        var source = new MyshareExtraContributionsSource(new StubFactory(handler), Options.Create(new MyshareOptions
        {
            BaseUrl = "https://myshare.example/api/",
            Bearer = "secret",
            ClubId = 7,
        }));
        var query = Automatic().ToQuery([], Today);

        Assert.True(source.CanHandle(query));
        var result = await source.GetAsync(query, CancellationToken.None);

        var contribution = Assert.Single(result);
        Assert.Equal(new ExtraContribution(42, new DateOnly(2026, 5, 1), 250.5m, "Donation BUK"), contribution);
        Assert.Equal("Bearer secret", handler.Request!.Headers.Authorization!.ToString());
        Assert.StartsWith("https://myshare.example/api/ClubTransactions/?clubId=7&fromDate=2026-01-01", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public void Myshare_is_skipped_without_configuration()
    {
        var source = new MyshareExtraContributionsSource(new StubFactory(new StubHandler("{}")), Options.Create(new MyshareOptions()));
        Assert.False(source.CanHandle(Automatic().ToQuery([], Today)));
    }

    private sealed class StubSource(string name, IReadOnlyList<ExtraContribution>? data = null, bool fail = false, bool canHandle = true)
        : IExtraContributionsSource
    {
        public int Calls { get; private set; }
        public string Name => name;
        public bool CanHandle(ExtraContributionsQuery query) => canHandle;

        public Task<IReadOnlyList<ExtraContribution>> GetAsync(ExtraContributionsQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return fail ? throw new HttpRequestException("nede") : Task.FromResult(data ?? []);
        }
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
