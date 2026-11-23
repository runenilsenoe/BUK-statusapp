using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using BukStatus.Core.People;
using BukStatus.Core.Pot;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Auth;

// The legacy site also reads the complete MyShare roster, never a union of local teams.
public sealed class PersonDirectory(IClubContext clubs, IMyshareDirectory directory,
    AuthenticationStateProvider authentication, IHttpContextAccessor http, IOptions<MyshareOptions> options,
    IOptions<ClubAccessOptions> access) : IPersonDirectory
{
    public async Task<ClubScope> GetAsync(CancellationToken ct = default)
    {
        if (clubs.Enabled) return await clubs.GetSelectedAsync(ct) ?? throw new UnauthorizedAccessException();
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) user = (await authentication.GetAuthenticationStateAsync()).User;
        var id = user.Identity?.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName ? access.Value.ApiPersonId : user.PersonId();
        if (user.Identity?.IsAuthenticated != true || id is not > 0) throw new UnauthorizedAccessException();
        var available = await directory.GetClubsAsync(id.Value, ct);
        var clubId = options.Value.ClubId > 0 ? options.Value.ClubId : access.Value.LegacyClubId;
        var club = available.FirstOrDefault(c => c.Id == clubId);
        return club is null ? throw new GroupException("Ingen MyShare-tilgang til den konfigurerte klubben.") : new(club, access.Value.LegacyClubId);
    }
}
