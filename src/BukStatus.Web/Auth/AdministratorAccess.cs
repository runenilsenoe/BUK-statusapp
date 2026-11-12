using System.Security.Claims;
using BukStatus.Core.Administration;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Auth;

public sealed class AdministratorAccess(IHttpContextAccessor http, AuthenticationStateProvider authentication,
    AdministratorStore store, IOptions<ClubAccessOptions> clubs, IOptions<BukAuthOptions> auth) : IWriteAccess
{
    private async Task<ClaimsPrincipal> UserAsync()
    {
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true) return user;
        try { return (await authentication.GetAuthenticationStateAsync()).User; }
        catch (InvalidOperationException) { return new ClaimsPrincipal(); }
    }
    public async Task<bool> CanEditAsync(CancellationToken ct = default)
    {
        var user = await UserAsync();
        if (user.Identity?.IsAuthenticated != true) return false;
        var machine = user.Identity.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName;
        var id = machine ? clubs.Value.ApiPersonId : user.PersonId() ?? 0;
        if (!clubs.Value.Enabled && (user.HasClaim(c => c.Type == BukClaims.Trusted) || auth.Value.AllowedPersonIds.Contains(id))) return true;
        return id > 0 && await store.IsAdministratorAsync(id, ct);
    }
    public async Task RequireAsync(CancellationToken ct = default)
    {
        if (!await CanEditAsync(ct)) throw new BukStatus.Core.Groups.GroupException("Kun administratorer kan gjøre endringer.");
    }
    private async Task<int> SuperAdminAsync()
    {
        var user = await UserAsync();
        if (user.Identity?.IsAuthenticated != true || user.Identity.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName
            || user.PersonId() != AdministratorStore.SuperAdminPersonId) throw new UnauthorizedAccessException();
        return AdministratorStore.SuperAdminPersonId;
    }
    public async Task<List<Administrator>> GetAllAsync() => await store.GetAllAsync(await SuperAdminAsync());
    public async Task AddAsync(int personId, string? name) => await store.AddAsync(await SuperAdminAsync(), personId, name);
    public async Task RemoveAsync(int personId) => await store.RemoveAsync(await SuperAdminAsync(), personId);
}
