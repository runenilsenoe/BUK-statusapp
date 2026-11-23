using System.Net;
using BukStatus.Core.Clubs;
using BukStatus.Core.Pot;
using Microsoft.Extensions.Options;

namespace BukStatus.Core.Tests;

public class MyshareDirectoryTests
{
    [Fact]
    public async Task Uses_only_authorized_teams_and_parses_full_roster()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return request.RequestUri.AbsolutePath.EndsWith("Teams")
                ? """{"statusCode":200,"data":[{"id":10016}]}"""
                : """{"statusCode":200,"data":[{"id":4050,"teamId":10016,"name":"BUK","members":[{"id":101,"displayName":"Test Person","firstName":"Test","lastName":"Person","birthDate":"2000-11-13T00:00:00","age":25}]}]}""";
        }));
        var directory = new MyshareDirectory(http, Options.Create(new MyshareOptions { BaseUrl = "https://example.test/api/", Bearer = "dummy" }));
        var clubs = await directory.GetClubsAsync(21410);
        Assert.Equal(new[] { "/api/Teams?userId=21410", "/api/Clubs/10016" }, paths);
        var member = Assert.Single(Assert.Single(clubs).Members);
        Assert.Equal(25, member.Age(new DateOnly(2026, 11, 12)));
        Assert.Equal(26, member.Age(new DateOnly(2026, 11, 13)));
    }
    [Fact]
    public async Task Rejects_club_returned_under_a_different_team()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("Teams")
            ? """{"statusCode":200,"data":[{"id":10016}]}"""
            : """{"statusCode":200,"data":[{"id":4050,"teamId":999,"name":"Other","members":[]}]}"""));
        var directory = new MyshareDirectory(http, Options.Create(new MyshareOptions { BaseUrl = "https://example.test/api/", Bearer = "dummy" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.GetClubsAsync(21410));
    }
    [Fact]
    public async Task Unauthorized_upstream_fails_instead_of_returning_empty_permissions()
    {
        using var http = new HttpClient(new Handler(_ => "", HttpStatusCode.Unauthorized));
        var directory = new MyshareDirectory(http, Options.Create(new MyshareOptions { BaseUrl = "https://example.test/api/", Bearer = "dummy" }));
        await Assert.ThrowsAsync<MyshareDirectoryException>(() => directory.GetClubsAsync(21410));
    }
    [Fact]
    public async Task Uses_session_access_token_without_requiring_a_configured_static_bearer()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("synthetic-session-token",request.Headers.Authorization!.Parameter);
            return "{\"statusCode\":200,\"data\":[]}";
        }));
        var directory = new MyshareDirectory(http,Options.Create(new MyshareOptions { BaseUrl="https://synthetic.example/api",Bearer="must-not-be-used" }),new Tokens());
        Assert.Empty(await directory.GetClubsAsync(21410));
    }
    [Fact]
    public async Task Campaign_reads_current_season_with_team_header_and_preserves_false_flags()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal("10016", Assert.Single(request.Headers.GetValues("TeamId")));
            Assert.Equal("synthetic-session-token", request.Headers.Authorization!.Parameter);
            return request.RequestUri.AbsolutePath.Contains("ClubSeasons")
                ? """{"statusCode":200,"data":[{"id":9,"startDateLocal":"2025-01-01","endDateLocal":"2025-12-31"},{"id":10,"startDateLocal":"2026-01-01","endDateLocal":"2026-12-31"}]}"""
                : """{"statusCode":200,"data":[{"personId":1,"name":"Api Name","isCountedInAksjon":false,"targetAmount":12000},{"personId":2,"name":"No Plan","isCountedInAksjon":true,"targetAmount":0}]}""";
        }));
        var directory = new MyshareDirectory(http, Options.Create(new MyshareOptions { BaseUrl = "https://synthetic.example/api" }), new Tokens());
        var rows = await directory.GetCampaignAsync(new(new(4050, 10016, "BUK", []), 4050), new DateOnly(2026, 11, 23));
        Assert.Equal(["/api/ClubSeasons/", "/api/ClubMembersStatus/?clubSeasonId=10&clubId=4050"], paths);
        Assert.False(rows![0].IsCountedInAksjon); Assert.Equal("Api Name", rows[0].Name);
        Assert.Equal(0m, rows[1].TargetAmount);
    }
    [Fact]
    public async Task Ambiguous_season_and_upstream_failure_are_not_treated_as_empty_roster()
    {
        using var http = new HttpClient(new Handler(_ => """{"statusCode":200,"data":[]}"""));
        var directory = new MyshareDirectory(http, Options.Create(new MyshareOptions { BaseUrl = "https://synthetic.example/api", Bearer = "dummy" }));
        await Assert.ThrowsAsync<MyshareDirectoryException>(() => directory.GetCampaignAsync(new(new(4050, 10016, "BUK", []), 4050), new DateOnly(2026, 11, 23)));
    }
    private sealed class Tokens : IMyshareTokenProvider
    {
        public bool CanUse => true;
        public Task<string> GetAsync(CancellationToken ct=default)=>Task.FromResult("synthetic-session-token");
    }
    private sealed class Handler(Func<HttpRequestMessage, string> body, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body(request)) });
    }
}
