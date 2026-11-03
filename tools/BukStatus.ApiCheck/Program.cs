// Smoke test for the BCC Contributions API setup: fetches a client-credentials token and makes a few read calls.
//
//   dotnet user-secrets set "BccApi:ClientId" "<id>" --project tools/BukStatus.ApiCheck
//   dotnet user-secrets set "BccApi:ClientSecret" "<secret>" --project tools/BukStatus.ApiCheck
//   dotnet run --project tools/BukStatus.ApiCheck [personId]

using BccCode.Contributions.Api.Client;
using BccCode.Platform;
using BukStatus.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.AddBccContributionsApi(builder.Configuration);
using var host = builder.Build();
await host.StartAsync(); // runs the BccApi options validation

var client = host.Services.GetRequiredService<IContributionsApiClient>();

try
{
    var statuses = await client.MemberStatuses.GetAllMemberStatusesForAction() ?? [];
    Console.WriteLine($"allMemberStatusesForAction: {statuses.Count} statuses");
    foreach (var group in statuses.GroupBy(s => s.OnTrackStatus).OrderBy(g => g.Key))
        Console.WriteLine($"  onTrackStatus {group.Key}: {group.Count()}");

    if (args.Length > 0 && int.TryParse(args[0], out var personId))
    {
        var status = await client.MemberStatuses.GetAsync(personId)
                     ?? throw new InvalidOperationException($"No status returned for {personId}");
        Console.WriteLine($"memberStatus {personId}: contributed {status.TotalContribution:N0} of {status.TotalAksjonTarget:N0}, " +
                          $"ajour until {status.AjourUntil}, ajour target {status.AjourTarget:N0}");
    }
}
catch (ApiRequestException ex)
{
    Console.Error.WriteLine($"API call failed ({ex.StatusCode}): {ex.Content}");
    return 1;
}
catch (UnauthorizedException ex)
{
    Console.Error.WriteLine($"Could not get a token from login.bcc.no - check BccApi:ClientId/ClientSecret. {ex.Message}");
    return 1;
}

return 0;
