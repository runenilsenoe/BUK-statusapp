using System.Text.Json;
using BccCode.Contributions.Api.Client;
using BccCode.Platform;
using BukStatus.Core.Groups;
using BukStatus.Core.Bcc;
using Microsoft.Extensions.Options;

namespace BukStatus.Core.Transactions;

public interface IClubOrganisationResolver
{
    Task<IReadOnlyList<int>> GetOrgIdsAsync(int clubId, CancellationToken ct = default);
}

public sealed class ClubOrganisationResolver(IContributionsApiClient client, IOptions<BccApiOptions> options) : IClubOrganisationResolver
{
    public async Task<IReadOnlyList<int>> GetOrgIdsAsync(int clubId, CancellationToken ct = default)
    {
        if (clubId <= 0) throw new GroupException("Velg en gyldig klubb.");
        var api = (IHttpApiClient)client.Contributions;
        var oauth = new OAuthOptions().WithEnvironmentDefaults(options.Value.AppEnvironment);
        var root = new Uri("https://" + oauth.Audience);
        var orgs = new HashSet<int>();
        using var http = api.ClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        for (var page = 0; page < 50; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(root, $"/contributions/ClubOrgMapping?limit=1000&offset={page * 1000}"));
            request.Headers.Authorization = await api.TokenProvider.GetAuthHeaderAsync([ContributionsApiScope.ContributionsRead.Value]);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new GroupException("Kunne ikke hente klubbkoblingen fra Contributions. Kontroller BCC-klientens lesetilgang.");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var data = document.RootElement;
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var wrapped)) data = wrapped;
            if (data.ValueKind != JsonValueKind.Array) throw new GroupException("Contributions returnerte en ukjent klubbkobling.");
            foreach (var row in data.EnumerateArray())
            {
                int? Number(string name) => row.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value is var value
                    && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
                if (Number("clubId") == clubId && Number("orgId") is > 0 and var org) orgs.Add(org);
            }
            if (data.GetArrayLength() < 1000) return orgs.Order().ToArray();
        }
        throw new GroupException("For mange klubbkoblinger fra Contributions.");
    }
}
