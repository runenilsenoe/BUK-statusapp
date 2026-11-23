using System.Net;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using BukStatus.Core.Pot;
using BukStatus.Core.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Tests;
public sealed class TransactionServiceTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), $"buk-transactions-{Guid.NewGuid()}");
    readonly Factory factory;
    readonly Clubs clubs = new();
    readonly Handler handler = new();
    readonly MemoryCache cache = new(new MemoryCacheOptions());
    readonly TransactionService service;
    public TransactionServiceTests()
    {
        Directory.CreateDirectory(folder);
        factory = new(new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite($"Data Source={Path.Combine(folder,"test.db")};Pooling=False").Options);
        using var db = factory.CreateDbContext(); db.Database.Migrate();
        db.PotSettings.Add(new() { ExtraMode = ExtraContributionsMode.Manual, ExtraContributions = 99999, ExtraDescriptionFilter = "Donation", ExtraExcludedPersonIds = "1" }); db.SaveChanges();
        var source = new MyshareExtraContributionsSource(new HttpFactory(handler), Options.Create(new MyshareOptions { BaseUrl = "https://synthetic.example/api", Bearer = "synthetic", ClubId = 4050 }));
        service = new([source], clubs, factory, cache, new Gate());
    }
    [Fact] public async Task Full_ledger_ignores_pot_filters_preserves_refunds_and_checks_access_before_cached_reads()
    {
        var result = await service.GetAsync(new(2026,1,1),new(2026,1,31));
        Assert.Equal(3,result.Rows.Count); Assert.Equal(110,result.Rows.Sum(r=>r.Amount));
        Assert.Contains(result.Rows, r => r.PersonId == 0 && r.Amount == 30);
        Assert.Equal("Roster member",result.Rows.Single(r=>r.Id==1).PersonName);
        Assert.Contains("clubId=4050",handler.LastUrl); Assert.Equal("10016",handler.TeamId);
        await service.GetAsync(new(2026,1,1),new(2026,1,31)); Assert.Equal(1,handler.Calls);
        clubs.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.GetAsync(new(2026,1,1),new(2026,1,31)));
        Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task Clubs_have_separate_cache_entries_and_invalid_periods_are_rejected()
    {
        await service.GetAsync(new(2026,1,1),new(2026,1,31));
        clubs.Id = 6817;
        await service.GetAsync(new(2026,1,1),new(2026,1,31));
        Assert.Equal(2,handler.Calls); Assert.Contains("clubId=6817",handler.LastUrl);
        await Assert.ThrowsAsync<GroupException>(()=>service.GetAsync(new(2026,2,1),new(2026,1,1)));
        await Assert.ThrowsAsync<GroupException>(()=>service.GetAsync(new(2000,1,1),new(2026,1,1)));
    }
    public void Dispose() { cache.Dispose(); Directory.Delete(folder,true); }
    sealed class Gate : BukStatus.Core.Administration.IWriteAccess { public Task RequireAsync(CancellationToken ct = default) => Task.CompletedTask; }
    sealed class Factory(DbContextOptions<BukStatusDbContext> options) : IDbContextFactory<BukStatusDbContext> { public BukStatusDbContext CreateDbContext()=>new(options); }
    sealed class Clubs : IClubContext
    {
        public bool Enabled => true; public int Id = 4050; public bool Denied;
        public Task<IReadOnlyList<MyshareClub>> GetClubsAsync(CancellationToken ct = default)=>throw new NotImplementedException();
        public Task<ClubScope?> GetSelectedAsync(CancellationToken ct = default) => Denied ? throw new UnauthorizedAccessException() : Task.FromResult<ClubScope?>(new(new(Id,10016,"Synthetic",[new(1,"Roster member",null,null,null,20)]),4050));
    }
    sealed class HttpFactory(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name)=>new(handler,false); }
    sealed class Handler : HttpMessageHandler
    {
        public int Calls; public string? LastUrl,TeamId;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++; LastUrl=request.RequestUri!.ToString(); TeamId=request.Headers.GetValues("TeamId").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("""
            {"statusCode":200,"data":[
            {"id":1,"userId":1,"transactionDate":"2026-01-02","description":"Personal","amount":100},
            {"id":2,"userId":1,"transactionDate":"2026-01-03","description":"Correction","amount":-20},
            {"id":4,"userId":null,"transactionDate":"2026-01-04","description":"Unassigned","amount":30},
            {"id":3,"userId":1,"transactionDate":"2026-02-02","description":"Outside period","amount":500}]}
            """) });
        }
    }
}
