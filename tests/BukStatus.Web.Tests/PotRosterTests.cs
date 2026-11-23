using System.Security.Claims;
using BukStatus.Core.Clubs;
using BukStatus.Core.Pot;
using BukStatus.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Tests;
public class PotRosterTests
{
    [Fact] public async Task Legacy_signed_in_pot_reads_complete_authorized_roster_without_any_local_teams()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(BukClaims.PersonId, "21410")], "oidc"));
        var (pot, directory) = Create(user);
        var scope = await pot.GetAsync();
        Assert.Equal(4050, scope!.Club.Id);
        Assert.Equal([1,2], scope.Club.Members.Select(m=>m.Id));
        await pot.GetAsync();
        Assert.Equal([21410],directory.Requests);
    }
    [Fact] public async Task Explicit_machine_identity_is_used_and_missing_club_permissions_do_not_fall_back_to_teams()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([],ApiKeyAuthenticationHandler.SchemeName));
        var (pot,directory) = Create(user,21410);
        Assert.Equal(2,(await pot.GetAsync())!.Club.Members.Count);
        Assert.Equal([21410],directory.Requests);
        var (denied,unavailable) = Create(new ClaimsPrincipal(new ClaimsIdentity([new Claim(BukClaims.PersonId,"12345")],"oidc")));
        unavailable.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>denied.GetAsync());
    }
    [Fact] public async Task Anonymous_legacy_screen_does_not_request_a_person_roster()
    {
        var (pot,directory)=Create(new ClaimsPrincipal());
        Assert.Null(await pot.GetAsync()); Assert.Empty(directory.Requests);
    }
    static (PotRoster,Directory) Create(ClaimsPrincipal user,int apiPersonId=0)
    {
        var http=new HttpContextAccessor { HttpContext=new DefaultHttpContext { User=user } };
        var state=new State(user); var directory=new Directory();
        var options=Options.Create(new ClubAccessOptions { LegacyClubId=4050,ApiPersonId=apiPersonId });
        var people=new PersonDirectory(new LegacyClub(),directory,state,http,Options.Create(new MyshareOptions()),options);
        return (new(people,http,state,options,new ConfigurationBuilder().Build()),directory);
    }
    sealed class State(ClaimsPrincipal user) : AuthenticationStateProvider
    { public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(user)); }
    sealed class LegacyClub : IClubContext
    {
        public bool Enabled=>false;
        public Task<ClubScope?> GetSelectedAsync(CancellationToken ct=default)=>Task.FromResult<ClubScope?>(null);
        public Task<IReadOnlyList<MyshareClub>> GetClubsAsync(CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<MyshareClub>>([]);
    }
    sealed class Directory : IMyshareDirectory
    {
        public bool Denied; public List<int> Requests=[];
        public Task<IReadOnlyList<MyshareClub>> GetClubsAsync(int id,CancellationToken ct=default)
        {
            Requests.Add(id); if(Denied)throw new UnauthorizedAccessException();
            return Task.FromResult<IReadOnlyList<MyshareClub>>([new(4050,10016,"Synthetic BUK",[new(1,"Member",null,null,null,20),new(2,"No team",null,null,null,21)])]);
        }
    }
}
