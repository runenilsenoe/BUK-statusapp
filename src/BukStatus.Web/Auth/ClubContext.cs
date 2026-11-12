using BukStatus.Core.Clubs;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Auth;

public sealed class ClubAccessOptions
{
    public bool Enabled { get; set; }
    public int LegacyClubId { get; set; } = 4050;
    // API keys have no person identity. Explicitly bind machine access to a MyShare user.
    public int ApiPersonId { get; set; }
}

public sealed class ClubContext(IHttpContextAccessor http, AuthenticationStateProvider authentication,
    IMyshareDirectory directory, IOptions<ClubAccessOptions> options) : IClubContext
{
    public const string CookieName = "buk.club";
    public bool Enabled => options.Value.Enabled;
    private IReadOnlyList<MyshareClub>? cached;
    private DateTimeOffset expires;
    private int cachedPersonId;
    private int? selectedId;

    public async Task<IReadOnlyList<MyshareClub>> GetClubsAsync(CancellationToken ct = default)
    {
        if (!Enabled) return [];
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException("Innlogging kreves.");
        var personId = user.Identity.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName
            ? options.Value.ApiPersonId : user.PersonId() ?? 0;
        if (personId <= 0) throw new UnauthorizedAccessException("BCC-person-ID mangler.");
        if (cached is not null && cachedPersonId == personId && expires > DateTimeOffset.UtcNow) return cached;
        // Do not serve stale permissions on upstream failure; short-lived cache stays within this user's scope.
        cached = null;
        var clubs = await directory.GetClubsAsync(personId, ct);
        cachedPersonId = personId;
        cached = clubs;
        expires = DateTimeOffset.UtcNow.AddSeconds(30);
        return clubs;
    }

    public async Task<ClubScope?> GetSelectedAsync(CancellationToken ct = default)
    {
        if (!Enabled) return null;
        var clubs = await GetClubsAsync(ct);
        selectedId ??= int.TryParse(http.HttpContext?.Request.Cookies[CookieName], out var cookieId) ? cookieId : null;
        // A machine caller may select a club, but never the person whose access is checked.
        if (http.HttpContext?.User.Identity?.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName
            && int.TryParse(http.HttpContext.Request.Headers["X-Club-Id"], out var headerId)) selectedId = headerId;
        var club = selectedId.HasValue ? clubs.FirstOrDefault(c => c.Id == selectedId)
            : clubs.FirstOrDefault(c => c.Id == options.Value.LegacyClubId) ?? clubs.FirstOrDefault();
        if (club is null) throw new UnauthorizedAccessException("Ingen tilgang til den valgte klubben.");
        selectedId = club.Id;
        return new(club, options.Value.LegacyClubId);
    }
}
