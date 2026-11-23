namespace BukStatus.Core.Clubs;

public interface IMyshareTokenProvider
{
    bool CanUse { get; }
    Task<string> GetAsync(CancellationToken ct = default);
}

// Token material is encrypted by ASP.NET Data Protection before it enters SQLite.
public sealed class MyshareUserSession
{
    public Guid Id { get; set; }
    public int PersonId { get; set; }
    public string Subject { get; set; } = "";
    public string ProtectedTokens { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
