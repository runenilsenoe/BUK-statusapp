using BukStatus.Core.Clubs;
using BukStatus.Core.Pot;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Auth;
public sealed class UserMyshareTokenProvider(MyshareTokenStore store, IHttpContextAccessor http,
    AuthenticationStateProvider authentication, IOptions<MyshareOptions> options, IConfiguration config) : IMyshareTokenProvider
{
    public bool CanUse => !config.GetValue<bool>("BccApi:UseFakeData");
    public async Task<string> GetAsync(CancellationToken ct = default)
    {
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true) throw new MyshareDirectoryException("Innlogging kreves for å hente MyShare-data.");
        if (user.Identity.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName)
        {
            // A machine call never borrows a browser user's token.
            if (!string.IsNullOrWhiteSpace(options.Value.Bearer)) return options.Value.Bearer;
            throw new MyshareDirectoryException("Maskininnhenting fra MyShare er ikke konfigurert. Brukerinnlogging gjelder bare din egen økt.");
        }
        return await store.GetAsync(user, ct);
    }
}
