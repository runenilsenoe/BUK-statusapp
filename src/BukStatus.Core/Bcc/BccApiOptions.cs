using System.ComponentModel.DataAnnotations;
using BccCode.Platform;

namespace BukStatus.Core.Bcc;

/// <summary>
/// Credentials for BCC's APIs (api.bcc.no), using the OAuth client credentials flow via login.bcc.no.
/// Bound from the "BccApi" configuration section. Keep the secret out of appsettings.json:
/// use user-secrets locally and environment variables (BccApi__ClientSecret) in production.
/// </summary>
public class BccApiOptions
{
    public const string SectionName = "BccApi";

    /// <summary>Use made-up statuses instead of calling BCC. For local development before credentials are in place.</summary>
    public bool UseFakeData { get; set; }

    /// <summary>prod, sandbox or dev. Selects login.bcc.no / api.bcc.no or their sandbox/dev counterparts.</summary>
    [Required]
    public string Environment { get; set; } = "prod";

    [Required]
    public string ClientId { get; set; } = "";

    [Required]
    public string ClientSecret { get; set; } = "";

    /// <summary>Request write access as well. Off by default since status lookups only need contributions#read.</summary>
    public bool IncludeWriteScope { get; set; }

    public AppEnvironment AppEnvironment => Environment.ToLowerInvariant() switch
    {
        "prod" or "production" => AppEnvironment.Prod,
        "sandbox" => AppEnvironment.Sandbox,
        "dev" => AppEnvironment.Dev,
        _ => throw new InvalidOperationException($"Unknown {SectionName}:Environment '{Environment}'. Use prod, sandbox or dev.")
    };
}
