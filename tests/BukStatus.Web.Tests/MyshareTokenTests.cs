using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using BukStatus.Core.Pot;
using BukStatus.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
namespace BukStatus.Web.Tests;

public sealed class MyshareTokenTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(),$"buk-oauth-{Guid.NewGuid()}.db");
    readonly Factory factory;
    readonly EphemeralDataProtectionProvider protection = new();
    readonly Clock time = new();
    readonly Handler handler = new();
    readonly Monitor oidc = new();
    readonly MyshareTokenStore store;
    static readonly IOptions<BukAuthOptions> Auth = Options.Create(new BukAuthOptions { ClientId="synthetic-client",ClientSecret="synthetic-secret" });
    public MyshareTokenTests()
    {
        factory = new(new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        using var db = factory.CreateDbContext(); db.Database.Migrate();
        store = NewStore();
    }
    MyshareTokenStore NewStore()=>new(factory,protection,new HttpFactory(handler),Auth,oidc,time);
    static ClaimsPrincipal User(int id=21410,string? session=null,string? subject=null)
    {
        var claims=new List<Claim>{new("sub",subject ?? $"bcc|{id}"),new(BukClaims.PersonId,id.ToString())};
        if(session is not null)claims.Add(new(BukClaims.MyshareSession,session));
        return new(new ClaimsIdentity(claims,"oidc"));
    }
    static string Jwt(string audience="https://myshare.today/api/",string marker="first")
    {
        static string Encode(string text)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+','-').Replace('/','_');
        return Encode("{\"alg\":\"none\"}")+"."+Encode(JsonSerializer.Serialize(new { aud=audience,sub="bcc|21410",marker,exp=2000000000}))+".x";
    }
    AuthenticationProperties Props(string? token=null,int seconds=3600,string? refresh="synthetic-refresh")
    {
        var props=new AuthenticationProperties();
        var tokens=new List<AuthenticationToken>{new(){Name="access_token",Value=token??Jwt()},new(){Name="expires_at",Value=time.GetUtcNow().AddSeconds(seconds).ToString("O")}};
        if(refresh is not null)tokens.Add(new(){Name="refresh_token",Value=refresh});
        props.StoreTokens(tokens); return props;
    }
    [Fact] public async Task Tokens_are_encrypted_bound_to_identity_and_survive_new_service_instance()
    {
        var user=User(); var props=Props(); await store.CaptureAsync(user,props);
        Assert.Empty(props.GetTokens());
        var session=user.FindFirst(BukClaims.MyshareSession)!.Value;
        using(var db=factory.CreateDbContext())
        {
            var row=Assert.Single(db.MyshareUserSessions);
            Assert.DoesNotContain(Jwt(),row.ProtectedTokens); Assert.DoesNotContain("synthetic-refresh",row.ProtectedTokens);
        }
        Assert.Equal(Jwt(),await NewStore().GetAsync(user));
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(User(999,session)));
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(User(21410,session,"different-subject")));
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(User()));
    }
    [Fact] public async Task Refresh_rotates_tokens_once_for_concurrent_requests_and_retains_rotated_refresh()
    {
        var user=User(); await store.CaptureAsync(user,Props(seconds:30));
        handler.Access=Jwt(marker:"renewed");
        var results=await Task.WhenAll(Enumerable.Range(0,5).Select(_=>store.GetAsync(user)));
        Assert.All(results,t=>Assert.Equal(handler.Access,t)); Assert.Equal(1,handler.Calls);
        Assert.Equal("synthetic-refresh",handler.LastRefresh);
        time.Utc=time.Utc.AddHours(2);
        handler.Access=Jwt(marker:"renewed-again");
        Assert.Equal(handler.Access,await store.GetAsync(user));
        Assert.Equal("rotated-refresh",handler.LastRefresh); Assert.Equal(2,handler.Calls);
    }
    [Fact] public async Task Invalid_audience_is_never_stored_or_returned_and_rejected_refresh_revokes_session()
    {
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.CaptureAsync(User(),Props(Jwt("https://other.example/api"))));
        var user=User(); await store.CaptureAsync(user,Props(seconds:30));
        handler.Access=Jwt("https://other.example/api");
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(user));
        handler.Code=HttpStatusCode.BadRequest;
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(user));
        using var db=factory.CreateDbContext(); Assert.Empty(db.MyshareUserSessions);
    }
    [Fact] public async Task Missing_refresh_expired_session_and_logout_do_not_return_stale_tokens()
    {
        var noRefresh=User(); await store.CaptureAsync(noRefresh,Props(seconds:30,refresh:null));
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(noRefresh)); Assert.Equal(0,handler.Calls);
        var first=User(); var second=User(); await store.CaptureAsync(first,Props()); await store.CaptureAsync(second,Props());
        await store.RevokeAsync(first);
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(first));
        Assert.Equal(Jwt(),await store.GetAsync(second));
        time.Utc=time.Utc.AddDays(8);
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(second));
    }
    [Fact] public async Task Machine_calls_never_borrow_user_tokens_and_browser_calls_never_use_static_machine_bearer()
    {
        var user=User(); await store.CaptureAsync(user,Props());
        UserMyshareTokenProvider Provider(ClaimsPrincipal principal,string bearer)=>new(store,new HttpContextAccessor{HttpContext=new DefaultHttpContext{User=principal}},new State(principal),Options.Create(new MyshareOptions{Bearer=bearer}),new ConfigurationBuilder().Build());
        var machine=new ClaimsPrincipal(new ClaimsIdentity([],ApiKeyAuthenticationHandler.SchemeName));
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>Provider(machine,"").GetAsync());
        Assert.Equal("synthetic-machine-token",await Provider(machine,"synthetic-machine-token").GetAsync());
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>Provider(User(),"synthetic-machine-token").GetAsync());
        Assert.Equal(Jwt(),await Provider(user,"synthetic-machine-token").GetAsync());
    }
    [Fact] public async Task Refresh_does_not_send_credentials_to_untrusted_metadata_endpoint()
    {
        var user=User(); await store.CaptureAsync(user,Props(seconds:30));
        oidc.Value.Configuration!.TokenEndpoint="https://other.example/oauth/token";
        await Assert.ThrowsAsync<MyshareDirectoryException>(()=>store.GetAsync(user)); Assert.Equal(0,handler.Calls);
    }
    public void Dispose()=>File.Delete(path);
    sealed class Factory(DbContextOptions<BukStatusDbContext> options):IDbContextFactory<BukStatusDbContext>{public BukStatusDbContext CreateDbContext()=>new(options);}
    sealed class Clock:TimeProvider{public DateTimeOffset Utc=new(2026,11,23,10,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Utc;}
    sealed class State(ClaimsPrincipal user):AuthenticationStateProvider{public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(user));}
    sealed class Monitor:IOptionsMonitor<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions Value=new(){Configuration=new OpenIdConnectConfiguration{TokenEndpoint="https://login.bcc.no/oauth/token"}};
        public OpenIdConnectOptions CurrentValue=>Value;public OpenIdConnectOptions Get(string? name)=>Value;public IDisposable? OnChange(Action<OpenIdConnectOptions,string?> listener)=>null;
    }
    sealed class HttpFactory(Handler handler):IHttpClientFactory{public HttpClient CreateClient(string name)=>new(handler,false);}
    sealed class Handler:HttpMessageHandler
    {
        public int Calls;public string? LastRefresh;public string Access=Jwt(marker:"renewed");public HttpStatusCode Code=HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;var fields=(await request.Content!.ReadAsStringAsync(ct)).Split('&').Select(s=>s.Split('=',2)).ToDictionary(a=>a[0],a=>Uri.UnescapeDataString(a[1].Replace('+',' ')));
            Assert.Equal("refresh_token",fields["grant_type"]);Assert.Equal("synthetic-client",fields["client_id"]);Assert.Equal("synthetic-secret",fields["client_secret"]);LastRefresh=fields["refresh_token"];
            return new(Code){Content=new StringContent(JsonSerializer.Serialize(new{access_token=Access,expires_in=3600,refresh_token="rotated-refresh"}))};
        }
    }
}
