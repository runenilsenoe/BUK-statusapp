using System.Net.Http.Headers;
using System.Text.Json;
using BccCode.Platform;
using BukStatus.Core.Bcc;
using BukStatus.Web.Auth;

namespace BukStatus.Web.Diagnostics;

public record ApiProbe(string Name, string Path, string Scope);
public record ApiProbeResult(string Name, string Path, string Scope, string Result, int? HttpStatus, int? Count, string Summary);

/// <summary>Read-only, bounded probes; tokens and upstream error bodies never leave this service.</summary>
public sealed class BccDiagnostics(AdministratorAccess access, IHttpClientFactory clients, IConfiguration config)
{
    public static readonly ApiProbe[] Probes =
    [
        new("Min person: navn og fødselsdato", "/core/v2/persons?limit=1&fields=personID,displayName,birthDate,uid,churchUid&filter=" + Uri.EscapeDataString("{\"personID\":{\"_eq\":21410}}"), "persons.name#read persons.birth_date#read"),
        new("Organisasjoner", "/core/v2/orgs?limit=5&fields=uid,orgID,name,type", "orgs#read"),
        new("Mine organisasjonstilknytninger", "/core/affiliations?limit=5&fields=orgUid,personUid,type,isActive&filter=" + Uri.EscapeDataString("{\"person\":{\"personID\":{\"_eq\":21410}}}"), "persons.affiliations#read"),
        new("Grupper", "/core/groups?limit=5&fields=uid,name,orgUid,type", "groups#read"),
        new("Team og tilgang", "/contributions/TeamConfig", "contributions#read"),
        new("Klubb–organisasjon-kobling", "/contributions/ClubOrgMapping?limit=5", "contributions#read"),
        new("Min status", "/contributions/memberStatus?personId=21410", "contributions#read"),
        new("Mine bidrag denne sesongen", "/contributions/memberClubContributions?personId=21410", "contributions#read"),
        new("Bidrag (utvalg)", "/contributions/Contributions?limit=3&Fields=uid,orgID,personID,amount,contributionDate", "contributions#read"),
        new("Mål (utvalg)", "/contributions/Targets?limit=3", "contributions#read"),
        new("Prosjekter (utvalg)", "/contributions/Projects?limit=3", "contributions#read")
    ];

    public async Task<List<ApiProbeResult>> RunAsync(CancellationToken ct = default)
    {
        // Check on every run, including circuit invocations. Machine keys cannot run diagnostics.
        await access.RequireSuperAdminAsync();
        var options = config.GetSection(BccApiOptions.SectionName).Get<BccApiOptions>() ?? new();
        if (options.UseFakeData) return [new("Konfigurasjon", "", "", "Ikke kjørt", null, null, "Appen bruker testdata. Diagnostikken krever ekte BCC-API.")];
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            return [new("Konfigurasjon", "", "", "Ikke kjørt", null, null, "BCC-klient mangler.")];
        var oauth = new OAuthOptions().WithEnvironmentDefaults(options.AppEnvironment);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        var results = new List<ApiProbeResult>();
        var tokens = new Dictionary<string, string?>();
        using var client = clients.CreateClient("BccDiagnostics");
        var probes = new Queue<ApiProbe>(Probes);
        while (probes.TryDequeue(out var probe))
        {
            try
            {
                if (!tokens.TryGetValue(probe.Scope, out var token))
                {
                    using var response = await client.PostAsync(new Uri(new Uri(oauth.Authority), oauth.TokenEndpoint), new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials", ["client_id"] = options.ClientId,
                        ["client_secret"] = options.ClientSecret, ["audience"] = oauth.Audience, ["scope"] = probe.Scope
                    }), timeout.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        results.Add(new(probe.Name, probe.Path, probe.Scope, "Token avvist", (int)response.StatusCode, null, "Kontroller at BCC-klienten er tildelt disse lesetilgangene."));
                        continue;
                    }
                    using var tokenBody = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
                    token = tokenBody.RootElement.GetProperty("access_token").GetString();
                    tokens[probe.Scope] = token;
                }
                if (string.IsNullOrWhiteSpace(token)) throw new JsonException();
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri("https://" + oauth.Audience), probe.Path));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var responseApi = await client.SendAsync(request, timeout.Token);
                if (!responseApi.IsSuccessStatusCode)
                {
                    results.Add(new(probe.Name, probe.Path, probe.Scope, "API avvist", (int)responseApi.StatusCode, null,
                        responseApi.StatusCode == System.Net.HttpStatusCode.BadRequest ? "Forespørselen eller filteret må verifiseres mot API-et." : "Ingen data vist. Kontroller klientens scopes og organisasjonstilgang."));
                    continue;
                }
                using var body = await JsonDocument.ParseAsync(await responseApi.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
                var root = body.RootElement;
                var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var wrapped) ? wrapped : root;
                if (probe.Name.StartsWith("Min person") && data.ValueKind == JsonValueKind.Array
                    && data.EnumerateArray().Any(p => !p.TryGetProperty("personID", out var id) || !id.TryGetInt32(out var value) || value != 21410))
                    throw new JsonException();
                if (probe.Name == "Grupper" && data.ValueKind == JsonValueKind.Array)
                {
                    var first = data.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("uid", out var uid) && Guid.TryParse(uid.GetString(), out var groupId))
                        probes.Enqueue(new("Medlemsliste for første tilgjengelige Core-gruppe", $"/core/groups/{groupId}/members?limit=3&fields=personUid,groupUid", "persons.groups#read"));
                }
                var count = data.ValueKind == JsonValueKind.Array ? data.GetArrayLength() : data.ValueKind == JsonValueKind.Object ? 1 : 0;
                // Output allowlisted fields only. Never render arbitrary response properties (or error bodies).
                string[] allowed = ["personID", "displayName", "birthDate", "churchUid", "uid", "orgID", "orgUid", "name", "type", "isActive", "teamID", "teamName", "orgIDsWithAccess", "userIDsWithAccess", "clubID", "clubId", "orgId", "personUid", "groupUid", "score", "totalContribution", "ajourUntil", "amount", "contributionDate"];
                var sample = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Take(3).ToArray() : new[] { data };
                var summaries = sample.Where(e => e.ValueKind == JsonValueKind.Object).Select(e => string.Join(", ", e.EnumerateObject().Where(p => allowed.Contains(p.Name)).Select(p =>
                    p.Name == "birthDate" ? $"fødselsdato: {(p.Value.ValueKind == JsonValueKind.String && DateOnly.TryParse(p.Value.GetString(), out _) ? "tilgjengelig" : "mangler")}" : $"{p.Name}: {p.Value}")));
                results.Add(new(probe.Name, probe.Path, probe.Scope, count == 0 ? "OK – tomt svar" : "OK", (int)responseApi.StatusCode, count,
                    "Felter i utvalget: " + string.Join(", ", sample.Where(e => e.ValueKind == JsonValueKind.Object).SelectMany(e => e.EnumerateObject().Select(p => p.Name)).Distinct()) + "\n" + string.Join("\n", summaries)));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                results.Add(new(probe.Name, probe.Path, probe.Scope, "Tidsavbrudd", null, null, "Prøv igjen senere."));
                if (timeout.IsCancellationRequested) break;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
            {
                results.Add(new(probe.Name, probe.Path, probe.Scope, "Uventet svar", null, null, "Nettverksfeil eller ukjent svarformat. Ingen rå feildata vises."));
            }
        }
        return results;
    }
}
