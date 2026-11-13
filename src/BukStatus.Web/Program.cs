using BukStatus.Core.Clubs;
using System.Globalization;
using BukStatus.Core;
using BukStatus.Core.Groups;
using BukStatus.Web;
using BukStatus.Web.Auth;
using BukStatus.Web.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var norwegian = CultureInfo.GetCultureInfo("nb-NO");
CultureInfo.DefaultThreadCurrentCulture = norwegian;
CultureInfo.DefaultThreadCurrentUICulture = norwegian;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBukStatusCore(builder.Configuration);
builder.AddBukAuthentication();
builder.Services.Configure<ClubAccessOptions>(builder.Configuration.GetSection("ClubAccess"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BukStatus.Core.Administration.AdministratorStore>();
builder.Services.AddScoped<AdministratorAccess>();
builder.Services.AddScoped<BukStatus.Web.Diagnostics.BccDiagnostics>();
builder.Services.AddHttpClient("BccDiagnostics", c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<BukStatus.Core.Administration.IWriteAccess>(sp => sp.GetRequiredService<AdministratorAccess>());
builder.Services.AddScoped<IClubContext, ClubContext>();
builder.Services.AddHttpClient<IMyshareDirectory, MyshareDirectory>(c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Behind a reverse proxy (Coolify/Traefik) TLS ends at the proxy. Trust its X-Forwarded-* headers so the app knows
// the request was https, otherwise BCC login would get an http:// callback URL.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear(); // the proxy sits on the Docker network, not localhost
    options.KnownProxies.Clear();
});

// Keys that encrypt the login cookie and antiforgery tokens. In a container they must survive redeploys,
// or everyone is logged out and open forms break on every deploy.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    builder.Services.AddDataProtection()
        .SetApplicationName("BukStatus")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddHealthChecks();

var app = builder.Build();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<BukStatusDbContext>>().CreateDbContextAsync())
    await db.Database.MigrateAsync();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (UnauthorizedAccessException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Ingen tilgang til valgt klubb.");
    }
});

app.MapStaticAssets();
app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapAccountEndpoints();
app.MapGroupApi();
app.MapTeamApi();
app.MapPotApi();
app.MapClubApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(AuthSetup.Policy);

app.Run();

public partial class Program;
