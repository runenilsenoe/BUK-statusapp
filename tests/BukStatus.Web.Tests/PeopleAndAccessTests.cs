using BukStatus.Core.Administration;
using BukStatus.Core.Clubs;
using BukStatus.Core.Groups;
using BukStatus.Core.People;
using BukStatus.Web.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BukStatus.Web.Tests;
public sealed class PeopleAndAccessTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"buk-people-{Guid.NewGuid()}.db");
    readonly Factory factory;
    readonly Directory directory = new();
    readonly Gate gate = new();
    readonly PersonService people;
    readonly AccessRequestService requests;
    public PeopleAndAccessTests()
    {
        factory = new(new DbContextOptionsBuilder<BukStatusDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        using var db = factory.CreateDbContext(); db.Database.Migrate();
        people = new(factory, directory, gate); requests = new(factory);
    }
    [Fact] public async Task Roster_is_independent_of_teams_and_notes_are_isolated_by_club()
    {
        Assert.Equal(2, (await people.GetAllAsync()).Count);
        await people.SaveAsync(1, "  Follow up\nNext week  ", WorkPreference.CommittedJob);
        var reloaded = new PersonService(factory, directory, gate);
        Assert.Equal("Follow up\nNext week", (await reloaded.GetAllAsync())[0].FollowUp.Note);
        Assert.Equal(WorkPreference.CommittedJob, (await reloaded.GetAllAsync())[0].FollowUp.Preference);
        directory.ClubId = 6817;
        Assert.Empty((await people.GetAllAsync())[0].FollowUp.Note);
        await people.SaveAsync(1, "Other club", WorkPreference.EmployedSaver);
        directory.ClubId = 4050;
        Assert.Equal("Follow up\nNext week", (await people.GetAllAsync())[0].FollowUp.Note);
        await people.SaveAsync(1, "", null);
        Assert.Null((await people.GetAllAsync())[0].FollowUp.Preference);
    }
    [Fact] public async Task Notes_require_admin_and_current_roster_membership_and_valid_values()
    {
        await Assert.ThrowsAsync<GroupException>(() => people.SaveAsync(999, "", null));
        await Assert.ThrowsAsync<GroupException>(() => people.SaveAsync(1, "", (WorkPreference)999));
        await Assert.ThrowsAsync<GroupException>(() => people.SaveAsync(1, new string('a', 5001), null));
        gate.Denied = true;
        await Assert.ThrowsAsync<GroupException>(() => people.GetAllAsync());
        await Assert.ThrowsAsync<GroupException>(() => people.SaveAsync(1, "Secret", null));
    }
    [Fact] public async Task Repeated_requests_queue_once_and_approval_uses_the_authenticated_name()
    {
        await requests.RequestAsync(55555, "Synthetic Applicant");
        await requests.RequestAsync(55555, "Changed name");
        var row = Assert.Single(await requests.GetPendingAsync(21410));
        Assert.Equal("Synthetic Applicant", row.DisplayName);
        Assert.Null(row.NotifiedAtUtc);
        await requests.ApproveAsync(21410, 55555);
        await requests.ApproveAsync(21410, 55555);
        Assert.Empty(await requests.GetPendingAsync(21410));
        using var db = factory.CreateDbContext();
        Assert.Equal("Synthetic Applicant", Assert.Single(db.Administrators).DisplayName);
        await requests.RequestAsync(55555, "Again");
        Assert.Empty(await requests.GetPendingAsync(21410));
    }
    [Fact] public async Task Only_superadmin_can_read_approve_or_configure_notifications()
    {
        await requests.RequestAsync(55555, "Synthetic");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requests.GetPendingAsync(55555));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requests.ApproveAsync(55555, 55555));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requests.SaveRecipientAsync(55555, "test@example.com"));
        await Assert.ThrowsAsync<GroupException>(() => requests.SaveRecipientAsync(21410, "bad\r\nBcc: other@example.com"));
        await requests.SaveRecipientAsync(21410, "owner@example.com");
        Assert.Equal("owner@example.com", await requests.GetRecipientAsync(21410));
    }
    [Fact] public async Task Email_outbox_waits_for_configuration_retries_failure_and_does_not_resend_success()
    {
        await requests.RequestAsync(55555, "Synthetic");
        var mail = new Mail();
        var worker = new AccessRequestNotifier(factory, mail, NullLogger<AccessRequestNotifier>.Instance);
        await worker.ProcessAsync(); Assert.Equal(0, mail.Attempts);
        await requests.SaveRecipientAsync(21410, "owner@example.com");
        mail.Configured = false; await worker.ProcessAsync(); Assert.Equal(0, mail.Attempts);
        mail.Configured = true; mail.Fail = true;
        await worker.ProcessAsync(); Assert.Equal(1, mail.Attempts);
        Assert.NotNull(Assert.Single(await requests.GetPendingAsync(21410)).DeliveryError);
        await worker.ProcessAsync(); Assert.Equal(1, mail.Attempts);
        using (var db = factory.CreateDbContext()) { var row = await db.AccessRequests.SingleAsync(); row.NextAttemptUtc = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); }
        mail.Fail = false; await worker.ProcessAsync(); await worker.ProcessAsync();
        Assert.Equal(2, mail.Attempts); Assert.Equal("owner@example.com", mail.Recipient);
        Assert.NotNull(Assert.Single(await requests.GetPendingAsync(21410)).NotifiedAtUtc);
    }
    public void Dispose() => File.Delete(path);
    sealed class Factory(DbContextOptions<BukStatusDbContext> options) : IDbContextFactory<BukStatusDbContext> { public BukStatusDbContext CreateDbContext() => new(options); }
    sealed class Gate : IWriteAccess { public bool Denied; public Task RequireAsync(CancellationToken ct = default) => Denied ? throw new GroupException("Denied") : Task.CompletedTask; }
    sealed class Directory : IPersonDirectory
    {
        public int ClubId = 4050;
        public Task<ClubScope> GetAsync(CancellationToken ct = default) => Task.FromResult(new ClubScope(new(ClubId, 10016, "Synthetic club", [new(1,"Synthetic one",null,null,null,20),new(2,"Synthetic two",null,null,null,21)]),4050));
    }
    sealed class Mail : IAccessRequestMail
    {
        public bool Configured { get; set; } = true;
        public bool Fail; public int Attempts; public string? Recipient;
        public Task SendAsync(string recipient, AccessRequest request, CancellationToken ct) { Attempts++; Recipient = recipient; if (Fail) throw new IOException("Synthetic delivery failure"); return Task.CompletedTask; }
    }
}
