using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BukStatus.Web.Auth;

/// <summary>
/// People log in with BCC (OIDC against login.bcc.no) and must be on Auth:AllowedPersonIds.
/// Machine callers use an X-Api-Key header on /api. In Development without an OIDC client, everyone is signed in
/// as a local developer.
/// </summary>
public static class AuthSetup
{
    public const string Policy = "AllowedUser";
    private const string Selector = "BukStatus";

    public static WebApplicationBuilder AddBukAuthentication(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(BukAuthOptions.SectionName);
        builder.Services.Configure<BukAuthOptions>(section);
        var options = section.Get<BukAuthOptions>() ?? new BukAuthOptions();

        var useDevelopmentLogin = !options.OidcConfigured && builder.Environment.IsDevelopment();
        if (!options.OidcConfigured && !useDevelopmentLogin)
            throw new InvalidOperationException(
                "BCC login is not configured. Set Auth:ClientId and Auth:ClientSecret (e.g. Auth__ClientSecret).");

        var userScheme = useDevelopmentLogin
            ? DevelopmentAuthenticationHandler.SchemeName
            : CookieAuthenticationDefaults.AuthenticationScheme;

        var auth = builder.Services
            .AddAuthentication(Selector)
            .AddPolicyScheme(Selector, null, policy => policy.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                    ? ApiKeyAuthenticationHandler.SchemeName
                    : userScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null)
            .AddCookie(cookie =>
            {
                cookie.LoginPath = "/account/login";
                cookie.AccessDeniedPath = "/access-denied";
                cookie.Cookie.Name = "bukstatus.auth";
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromDays(7);
                cookie.SlidingExpiration = true;
                // /api answers with status codes instead of sending the caller to the login page.
                cookie.Events.OnRedirectToLogin = context => RedirectOrStatus(context, StatusCodes.Status401Unauthorized);
                cookie.Events.OnRedirectToAccessDenied = context => RedirectOrStatus(context, StatusCodes.Status403Forbidden);
            });

        if (useDevelopmentLogin)
            auth.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, null);
        else
            auth.AddOpenIdConnect(oidc =>
            {
                oidc.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.ResponseType = "code";
                oidc.CallbackPath = "/signin-auth0";
                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.Scope.Add("profile");
                oidc.Scope.Add("email");
                // Keep BCC's claim names as-is, e.g. https://login.bcc.no/claims/personId.
                oidc.MapInboundClaims = false;
                // The person id normally comes in the id_token. Ask userinfo as well, so login still works if BCC only
                // puts it there.
                oidc.GetClaimsFromUserInfoEndpoint = true;
                oidc.ClaimActions.MapUniqueJsonKey(BukClaims.PersonId, BukClaims.PersonId);
                oidc.TokenValidationParameters.NameClaimType = "name";
            });

        builder.Services.AddSingleton<IAuthorizationHandler, AllowedPersonHandler>();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AllowedPersonRequirement()));
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/account/login", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = LocalUrlOrRoot(returnUrl) },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery, IOptions<BukAuthOptions> options) =>
        {
            // The logout forms send an antiforgery token; without this check, another site could log people out.
            if (!await antiforgery.IsRequestValidAsync(context))
                return Results.BadRequest();

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!options.Value.OidcConfigured)
                return Results.LocalRedirect("/");

            // Also end the session at login.bcc.no, otherwise the next visit logs straight back in.
            var returnTo = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/";
            var logoutUrl = $"{options.Value.Authority.TrimEnd('/')}/v2/logout" +
                            $"?client_id={Uri.EscapeDataString(options.Value.ClientId)}&returnTo={Uri.EscapeDataString(returnTo)}";
            return Results.Redirect(logoutUrl);
        })
        .AllowAnonymous();

        return app;
    }

    private static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
            context.Response.StatusCode = statusCode;
        else
            context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    private static string LocalUrlOrRoot(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : "/";

    public static int? PersonId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(BukClaims.PersonId)?.Value, out var personId) ? personId : null;
}

public class AllowedPersonRequirement : IAuthorizationRequirement;

/// <summary>Lets in API key callers, the local developer, and logged-in people on Auth:AllowedPersonIds.</summary>
public class AllowedPersonHandler(IOptionsMonitor<BukAuthOptions> options, IOptions<ClubAccessOptions> clubs, BukStatus.Core.Administration.AdministratorStore administrators) : AuthorizationHandler<AllowedPersonRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowedPersonRequirement requirement)
    {
        var user = context.User;
        if (clubs.Value.Enabled)
        {
            if (user.PersonId() is > 0 || (user.Identity?.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName && clubs.Value.ApiPersonId > 0))
                context.Succeed(requirement);
            return;
        }
        if (user.HasClaim(c => c.Type == BukClaims.Trusted)
            || user.PersonId() is { } personId && (options.CurrentValue.AllowedPersonIds.Contains(personId) || await administrators.IsAdministratorAsync(personId)))
            context.Succeed(requirement);
        return;
    }
}
