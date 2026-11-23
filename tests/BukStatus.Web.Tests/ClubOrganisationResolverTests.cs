using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using BccCode.Contributions.Api.Client;
using BccCode.Platform;
using BukStatus.Core.Bcc;
using BukStatus.Core.Transactions;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Tests;

public class ClubOrganisationResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reads_mapping_with_contributions_credentials_and_filters_exact_club(bool envelope)
    {
        using var handler = new Handler(envelope);
        var factory = new Factory(handler);
        var tokens = Proxy.Make<IAuthHeaderProvider>((method, args) =>
        {
            Assert.Equal("GetAuthHeaderAsync", method.Name);
            Assert.Equal(["contributions#read"], (string[])args![0]!);
            return Task.FromResult(new AuthenticationHeaderValue("Bearer", "synthetic"));
        });
        var contributions = Proxy.Make<IContributionsClient>((method, _) => method.Name switch
        {
            "get_ClientFactory" => factory,
            "get_TokenProvider" => tokens,
            _ => throw new InvalidOperationException(method.Name)
        });
        var client = Proxy.Make<IContributionsApiClient>((method, _) => method.Name == "get_Contributions"
            ? contributions : throw new InvalidOperationException(method.Name));
        var resolver = new ClubOrganisationResolver(client, Options.Create(new BccApiOptions()));
        Assert.Equal([49, 50], await resolver.GetOrgIdsAsync(4050));
        Assert.Equal(1, handler.Calls);
    }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
        public static T Make<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
        { var value = Create<T, Proxy>(); ((Proxy)(object)value).Call = call; return value; }
    }
    sealed class Factory(Handler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    sealed class Handler(bool envelope) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("https://api.bcc.no/contributions/ClubOrgMapping?limit=1000&offset=0", request.RequestUri!.AbsoluteUri);
            Assert.Equal("synthetic", request.Headers.Authorization!.Parameter);
            const string rows = """[{"clubID":4050,"orgID":49},{"clubId":6817,"orgId":999},{"clubId":4050,"orgId":50},{"clubId":4050,"orgId":49}]""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope ? "{\"data\":" + rows + "}" : rows) });
        }
    }
}
