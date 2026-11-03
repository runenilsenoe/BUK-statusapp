namespace BukStatus.Web.Auth;

/// <summary>
/// Login with BCC (OIDC via login.bcc.no) for people, and API keys for machine callers such as the Telegram bot.
/// Bound from the "Auth" section. Keep ClientSecret and ApiKeys in user-secrets / environment variables.
/// </summary>
public class BukAuthOptions
{
    public const string SectionName = "Auth";

    public string Authority { get; set; } = "https://login.bcc.no";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>BCC person ids that may use the site. Everyone else gets "no access" after logging in.</summary>
    public List<int> AllowedPersonIds { get; set; } = [];

    /// <summary>Keys accepted in the X-Api-Key header on /api. One per caller, so a key can be revoked alone.</summary>
    public List<string> ApiKeys { get; set; } = [];

    public bool OidcConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public static class BukClaims
{
    /// <summary>Person id claim in BCC's id_token.</summary>
    public const string PersonId = "https://login.bcc.no/claims/personId";

    /// <summary>Set on the local development user and on API key callers, which skip the person allowlist.</summary>
    public const string Trusted = "buk:trusted";
}
