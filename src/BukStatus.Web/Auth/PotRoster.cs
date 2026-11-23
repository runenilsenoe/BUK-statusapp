using BukStatus.Core.Clubs;
using BukStatus.Core.People;
using BukStatus.Core.Pot;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Auth;

// The signed-in legacy app must use the same complete roster as the person overview.
// Anonymous legacy screens and local development retain their explicitly configured local data.
public sealed class PotRoster(IPersonDirectory directory, IHttpContextAccessor http,
    AuthenticationStateProvider authentication, IOptions<ClubAccessOptions> options, IConfiguration configuration) : IPotRoster
{
    private ClubScope? cached;
    private int cachedPersonId;
    private DateTimeOffset expires;
    public async Task<ClubScope?> GetAsync(CancellationToken ct = default)
    {
        if (configuration.GetValue<bool>("BccApi:UseFakeData")) return null;
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) user = (await authentication.GetAuthenticationStateAsync()).User;
        var id = user.Identity?.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName ? options.Value.ApiPersonId : user.PersonId();
        if (user.Identity?.IsAuthenticated != true || id is not > 0) return null;
        if (cached is not null && cachedPersonId == id && expires > DateTimeOffset.UtcNow) return cached;
        cached = null;
        var scope = await directory.GetAsync(ct);
        cached = scope; cachedPersonId = id.Value; expires = DateTimeOffset.UtcNow.AddSeconds(30);
        return scope;
    }
}
