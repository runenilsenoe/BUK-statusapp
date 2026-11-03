using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Auth;

/// <summary>
/// Signs everyone in as a local developer. Only registered in the Development environment when BCC login isn't
/// configured, so the site can be worked on before the OIDC client exists.
/// </summary>
public class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [new Claim("name", "Utvikler (lokal)"), new Claim(BukClaims.Trusted, "true")], SchemeName, "name", null);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
