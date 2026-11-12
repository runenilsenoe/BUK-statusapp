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
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.GetClubsAsync(21410));
    }
    private sealed class Handler(Func<HttpRequestMessage, string> body, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body(request)) });
    }
}
