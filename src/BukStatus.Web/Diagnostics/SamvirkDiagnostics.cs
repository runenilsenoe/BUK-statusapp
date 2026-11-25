using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BccCode.Platform;
using BukStatus.Core.Bcc;
using BukStatus.Core.Clubs;
using BukStatus.Web.Auth;

namespace BukStatus.Web.Diagnostics;

/// <summary>Read-only tests against the endpoint used by aksjon.bcc.no. Never returns tokens or personal values.</summary>
public sealed class SamvirkDiagnostics(AdministratorAccess access, IHttpClientFactory clients,
    IConfiguration config, IMyshareTokenProvider myshare)
{
    public const string Audience = "aksjon.bcc.no";
    public const string Endpoint = "https://ca-aksjonapp-api.kindisland-edecf2b1.westeurope.azurecontainerapps.io/SamvirkStatus/myStatus";

    public async Task<List<ApiProbeResult>> RunAsync(CancellationToken ct = default)
    {
        await access.RequireSuperAdminAsync();
        var options = config.GetSection(BccApiOptions.SectionName).Get<BccApiOptions>() ?? new();
        if (options.UseFakeData)
            return [Result("Konfigurasjon", "", "Ikke kjørt", null, "Appen bruker testdata.")];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        using var client = clients.CreateClient("BccDiagnostics");
        var results = new List<ApiProbeResult>();
        var oauth = new OAuthOptions().WithEnvironmentDefaults(options.AppEnvironment);

        // Keep audiences separate: an Aksjon token never replaces the Contributions or login token.
        await TestClient("Eksisterende Contributions-tilgang", oauth.Audience, "contributions#read");
        const string userName = "Eksisterende MyShare-brukertoken";
        try
        {
            var token = await myshare.GetAsync(timeout.Token);
            results.Add(await ProbeAsync(client, userName, "MyShare-brukerinnlogging", token, timeout.Token));
        }
        catch (MyshareDirectoryException)
        {
            results.Add(Result(userName, "MyShare-brukerinnlogging", "Ikke kjørt", null,
                "MyShare-brukertoken er ikke tilgjengelig. Koble til MyShare og prøv igjen."));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(Result(userName, "MyShare-brukerinnlogging", "Tidsavbrudd eller nettverksfeil", null, "Ingen rå feildata vises."));
        }
        await TestClient("Samme BCC-klient med Aksjon-audience", Audience, null);
        return results;

        async Task TestClient(string name, string audience, string? scope)
        {
            var description = $"audience={audience}" + (scope is null ? "" : $" · {scope}");
            if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                results.Add(Result(name, description, "Ikke kjørt", null, "BCC API-klient mangler."));
                return;
            }
            try
            {
                var fields = new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials", ["client_id"] = options.ClientId,
                    ["client_secret"] = options.ClientSecret, ["audience"] = audience
                };
                if (scope is not null) fields["scope"] = scope;
                using var response = await client.PostAsync(new Uri(new Uri(oauth.Authority), oauth.TokenEndpoint),
                    new FormUrlEncodedContent(fields), timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    results.Add(Result(name, description, "Token avvist", (int)response.StatusCode,
                        audience == Audience
                            ? "BCC-klienten fikk ikke token for aksjon.bcc.no med client_credentials. BCC må avklare klienttilgang og om API-et krever brukerinnlogging."
                            : "Klienten fikk ikke Contributions-token. Kontroller API-klient og contributions#read."));
                    return;
                }
                using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
                var token = body.RootElement.GetProperty("access_token").GetString();
                if (string.IsNullOrWhiteSpace(token)) throw new JsonException();
                results.Add(await ProbeAsync(client, name, description, token, timeout.Token));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(Result(name, description, "Test kunne ikke fullføres", null, "Nettverksfeil, tidsavbrudd eller ukjent tokenformat. Ingen rå feildata vises."));
            }
        }
    }

    private static ApiProbeResult Result(string name, string token, string result, int? status, string summary, int? count = null)
        => new(name, Endpoint, token, result, status, count, summary);

    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "data", "currentStatus", "monthlyStatuses", "currentSamvirkStatus", "samvirkMonthlyStatuses",
        "personId", "personID", "year", "month", "samvirkCoin", "discount", "isActiveInAksjon",
        "ungParticipationStatus", "onTrackAmount", "getOnTrackAmount", "remainingFullDiscount",
        "remainingHalfDiscount", "currentBalance", "currentTarget", "totalEstimatedTarget", "onTrackUntilDate"
    };

    private static async Task<ApiProbeResult> ProbeAsync(HttpClient client, string name, string source, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.NoContent)
            return Result(name, source, "OK – ingen personstatus", status,
                "API-et godtok forespørselen, men returnerte ingen status. Dette bekrefter ikke tilgang til klubbens medlemmer.", 0);
        if (!response.IsSuccessStatusCode)
            return Result(name, source, "API avvist", status, response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Tokenet ble ikke godtatt av Samvirk-endepunktet. Aksjon bruker audience aksjon.bcc.no; Contributions og MyShare har andre audiences.",
                HttpStatusCode.Forbidden => "API-et avviste tilgangen. Kontroller brukerroller og klientens tillatelser hos BCC.",
                _ => "Ingen data vist. API-et returnerte et mislykket svar."
            });
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = body.RootElement;
            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) throw new JsonException();
            var fields = new HashSet<string>();
            Collect(root, 0);
            if (!fields.Overlaps(["samvirkCoin", "ungParticipationStatus", "isActiveInAksjon", "remainingFullDiscount"])) return Result(name, source, "Ukjent svarformat", status,
                "HTTP-kallet lyktes, men ingen kjente Samvirk-felter ble funnet. Ingen personverdier vises.");
            return Result(name, source, "OK – Samvirk-data tilgjengelig", status,
                "Bekreftede felter: " + string.Join(", ", fields.Order()) +
                ". Ingen personverdier vises. myStatus gjelder tokenets egen bruker, ikke en komplett medlemsliste.",
                root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : null);

            void Collect(JsonElement element, int depth)
            {
                if (depth > 6) return;
                if (element.ValueKind == JsonValueKind.Object)
                    foreach (var property in element.EnumerateObject())
                    {
                        if (KnownFields.Contains(property.Name) && property.Name != "data") fields.Add(property.Name);
                        Collect(property.Value, depth + 1);
                    }
                else if (element.ValueKind == JsonValueKind.Array)
                    foreach (var item in element.EnumerateArray().Take(12)) Collect(item, depth + 1);
            }
        }
        catch (JsonException)
        {
            return Result(name, source, "Ukjent svarformat", status, "HTTP-kallet lyktes, men svaret var ikke forventet JSON. Ingen rå data vises.");
        }
    }
}
