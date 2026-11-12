using System.Security.Claims;
using BukStatus.Core.Administration;
using BukStatus.Core.Groups;
using BukStatus.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Tests;

public sealed class AdministratorAccessTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AdministratorStore store;
    public AdministratorAccessTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite(connection).Options;
        using var db = new BukStatusDbContext(options);
        db.Database.Migrate();
        store = new(new Factory(options));
    }
    public void Dispose() => connection.Dispose();
    private AdministratorAccess Access(int person, string authType = "oidc") => new(
        new HttpContextAccessor(), new UserState(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(BukClaims.PersonId, person.ToString()), new Claim(ClaimTypes.Role, "SuperAdmin")], authType))),
        store, Options.Create(new ClubAccessOptions { Enabled = true, ApiPersonId = 21410 }), Options.Create(new BukAuthOptions()));
    [Fact]
    public async Task Role_claim_cannot_grant_admin_and_revocation_applies_immediately()
    {
        var user = Access(12345);
        Assert.False(await user.CanEditAsync());
        await Assert.ThrowsAsync<GroupException>(() => user.RequireAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => user.AddAsync(54321, null));
        await Access(21410).AddAsync(12345, null);
        Assert.True(await user.CanEditAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => user.GetAllAsync());
        await Access(21410).RemoveAsync(12345);
        Assert.False(await user.CanEditAsync());
    }
    [Fact]
    public async Task Machine_key_cannot_manage_roles_even_when_bound_to_superadmin()
    {
        var machine = Access(21410, ApiKeyAuthenticationHandler.SchemeName);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => machine.AddAsync(12345, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => machine.GetAllAsync());
    }
    private sealed class Factory(DbContextOptions<BukStatusDbContext> options) : IDbContextFactory<BukStatusDbContext>
    {
        public BukStatusDbContext CreateDbContext() => new(options);
    }
    private sealed class UserState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
