using System.Net.Http.Headers;
using System.Text.Json;
using BukStatus.Core.Pot;
using Microsoft.Extensions.Options;

namespace BukStatus.Core.Clubs;

public sealed record ClubMember(int Id, string DisplayName, string? FirstName, string? LastName, DateOnly? BirthDate, int? ReportedAge)
{
    public int? Age(DateOnly today)
    {
        if (BirthDate is not { } birth) return ReportedAge;
        if (birth > today) return null;
        var age = today.Year - birth.Year;
        return today < birth.AddYears(age) ? age - 1 : age;
    }
}
public sealed record MyshareCampaignMember(int PersonId, string? Name, bool? IsCountedInAksjon, decimal? TargetAmount);
public sealed record MyshareClub(int Id, int TeamId, string Name, IReadOnlyList<ClubMember> Members);
public sealed class MyshareDirectoryException(string message) : InvalidOperationException(message);

public interface IMyshareDirectory
{
    Task<IReadOnlyList<MyshareClub>> GetClubsAsync(int personId, CancellationToken ct = default);
    Task<IReadOnlyList<MyshareCampaignMember>?> GetCampaignAsync(ClubScope scope, DateOnly today, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MyshareCampaignMember>?>(null);
}

/// <summary>Access is derived from Teams(userId), never from the clubs' member lists.</summary>
public sealed class MyshareDirectory(HttpClient client, IOptions<MyshareOptions> options, IMyshareTokenProvider? tokens = null) : IMyshareDirectory
{
    public async Task<IReadOnlyList<MyshareClub>> GetClubsAsync(int personId, CancellationToken ct = default)
    {
        if (personId <= 0) throw new UnauthorizedAccessException();
        var teams = await GetAsync($"Teams?userId={personId}", ct: ct);
        var clubs = new List<MyshareClub>();
        foreach (var teamId in teams.Select(t => t.GetProperty("id").GetInt32()).Distinct())
        {
            if (teamId <= 0) throw new InvalidDataException("Ugyldig MyShare-team.");
            foreach (var club in await GetAsync($"Clubs/{teamId}", teamId, ct))
            {
                if (club.GetProperty("teamId").GetInt32() != teamId) throw new InvalidDataException("Klubben tilhører et annet team.");
                var id = club.GetProperty("id").GetInt32();
                if (id <= 0 || clubs.Any(c => c.Id == id)) throw new InvalidDataException("Ugyldig eller gjentatt klubb.");
                var members = club.GetProperty("members").EnumerateArray().Select(ParseMember).ToArray();
                if (members.Select(m => m.Id).Distinct().Count() != members.Length) throw new InvalidDataException("Dupliserte medlemmer.");
                clubs.Add(new(id, teamId, club.GetProperty("name").GetString() ?? $"Klubb {id}", members));
            }
        }
        return clubs.OrderBy(c => c.Name).ToArray();
    }
    private async Task<JsonElement[]> GetAsync(string path, int? teamId = null, CancellationToken ct = default)
    {
        var settings = options.Value;
        if (!Uri.TryCreate(settings.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root)
            || root.Scheme != "https" || root.UserInfo.Length > 0 || root.Query.Length > 0 || root.Fragment.Length > 0)
            throw new MyshareDirectoryException("MyShare-tilgang er ikke konfigurert. En administrator må sette opp tilgangen.");
        var bearer = tokens is not null ? await tokens.GetAsync(ct) : settings.Bearer;
        if (string.IsNullOrWhiteSpace(bearer)) throw new MyshareDirectoryException("MyShare-tilgang er ikke konfigurert.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (teamId.HasValue) request.Headers.Add("TeamId", teamId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new MyshareDirectoryException(response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "MyShare avviser innloggingen (HTTP 401). Koble til MyShare på nytt via appen.",
            System.Net.HttpStatusCode.Forbidden => "MyShare nekter tilgang (HTTP 403). Kontroller kontoens og appens API-rettigheter.",
            _ => $"MyShare svarte med HTTP {(int)response.StatusCode}. Prøv igjen senere."
        });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var value = body.RootElement;
        if (value.GetProperty("statusCode").GetInt32() != 200 || value.GetProperty("data").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MyShare returnerte et ugyldig svar.");
        return value.GetProperty("data").EnumerateArray().Select(x => x.Clone()).ToArray();
    }
    public async Task<IReadOnlyList<MyshareCampaignMember>?> GetCampaignAsync(ClubScope scope, DateOnly today, CancellationToken ct = default)
    {
        if (tokens?.CanUse != true && string.IsNullOrWhiteSpace(options.Value.Bearer)) return null;
        var seasonId = options.Value.SeasonId;
        if (seasonId <= 0)
        {
            var seasons = await GetAsync("ClubSeasons/", scope.Club.TeamId, ct);
            DateOnly? Date(JsonElement item, string key) => item.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: >= 10 } text
                && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", out var date) ? date : null;
            var current = seasons.Where(s => Date(s, "startDateLocal") is { } start && start <= today
                && Date(s, "endDateLocal") is { } end && end >= today).ToArray();
            if (current.Length != 1) throw new MyshareDirectoryException("Kan ikke velge aksjonssesong. Konfigurer Myshare:SeasonId.");
            seasonId = current[0].GetProperty("id").GetInt32();
        }
        var rows = await GetAsync($"ClubMembersStatus/?clubSeasonId={seasonId}&clubId={scope.Club.Id}", scope.Club.TeamId, ct);
        return rows.Select(row => new MyshareCampaignMember(row.GetProperty("personId").GetInt32(),
            row.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null,
            row.TryGetProperty("isCountedInAksjon", out var counted) && counted.ValueKind is JsonValueKind.True or JsonValueKind.False ? counted.GetBoolean() : null,
            row.TryGetProperty("targetAmount", out var target) && target.ValueKind == JsonValueKind.Number && target.TryGetDecimal(out var amount) ? amount : null)).ToArray();
    }
    private static ClubMember ParseMember(JsonElement m)
    {
        string? Text(string key) => m.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        var id = m.GetProperty("id").GetInt32();
        if (id <= 0) throw new InvalidDataException("Ugyldig person-ID.");
        var birthText = Text("birthDate");
        DateOnly? birth = birthText is { Length: >= 10 } && DateOnly.TryParseExact(birthText[..10], "yyyy-MM-dd", out var date) ? date : null;
        int? age = m.TryGetProperty("age", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out var number) && number >= 0 ? number : null;
        return new(id, (string.IsNullOrWhiteSpace(Text("displayName")) ? null : Text("displayName")) ?? string.Join(' ', new[] { Text("firstName"), Text("lastName") }.Where(x => !string.IsNullOrWhiteSpace(x))), Text("firstName"), Text("lastName"), birth, age);
    }
}
