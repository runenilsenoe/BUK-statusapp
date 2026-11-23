using System.Net;
using System.Net.Mail;
using BukStatus.Core.Administration;
using BukStatus.Core.Groups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Notifications;
public class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public bool Configured => !string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535 && !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password) && MailAddress.TryCreate(From, out _);
}
public interface IAccessRequestMail
{
    bool Configured { get; }
    Task SendAsync(string recipient, AccessRequest request, CancellationToken ct);
}
public sealed class SmtpAccessRequestMail(IOptions<SmtpOptions> options) : IAccessRequestMail
{
    public bool Configured => options.Value.Configured;
    public async Task SendAsync(string recipient, AccessRequest request, CancellationToken ct)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = true, Credentials = new NetworkCredential(settings.User, settings.Password), Timeout = 30000 };
        using var message = new MailMessage(settings.From, recipient)
        {
            Subject = "BUK Status: Ny tilgangsforespørsel",
            Body = $"{request.DisplayName} (BCC person-ID {request.PersonId}) ber om tilgang til BUK Status.\n\nÅpne https://buk.bttr.no/administrators for å vurdere forespørselen. Innvilgelse gir administratorrolle, men gir ingen nye MyShare-klubbtilganger."
        };
        await client.SendMailAsync(message, ct);
    }
}
// Persistent outbox: submission succeeds even if Gmail is unavailable. Failed deliveries retry.
public sealed class AccessRequestNotifier(IDbContextFactory<BukStatusDbContext> factory, IAccessRequestMail mail,
    ILogger<AccessRequestNotifier> logger) : BackgroundService
{
    public async Task ProcessAsync(CancellationToken ct = default)
    {
        if (!mail.Configured) return;
        await using var db = await factory.CreateDbContextAsync(ct);
        var recipient = (await db.AccessNotificationSettings.FindAsync([1], ct))?.Recipient;
        if (string.IsNullOrWhiteSpace(recipient)) return;
        var now = DateTime.UtcNow;
        var requests = await db.AccessRequests.Where(r => !r.Approved && r.NotifiedAtUtc == null && r.NextAttemptUtc <= now
            && !db.Administrators.Any(a => a.PersonId == r.PersonId)).OrderBy(r => r.RequestedAtUtc).Take(10).ToListAsync(ct);
        foreach (var request in requests)
        {
            try { await mail.SendAsync(recipient, request, ct); request.NotifiedAtUtc = DateTime.UtcNow; request.DeliveryError = null; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                request.NextAttemptUtc = DateTime.UtcNow.AddMinutes(5);
                request.DeliveryError = "E-post kunne ikke sendes. Automatisk nytt forsøk om fem minutter.";
                logger.LogWarning("Access request email delivery failed ({ErrorType})", ex.GetType().Name);
            }
            await db.SaveChangesAsync(ct);
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await ProcessAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning("Access request notifier failed ({ErrorType})", ex.GetType().Name); }
        }
    }
}
