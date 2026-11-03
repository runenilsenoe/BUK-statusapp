using BccCode.Contributions.Api.Client;
using BccCode.Platform;
using BukStatus.Core.Bcc;
using BukStatus.Core.Groups;
using BukStatus.Core.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BukStatus.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers member statuses (<see cref="IMemberStatusService"/>) and groups (<see cref="GroupService"/>,
    /// <see cref="GroupStatusService"/>). Shared by the website and the Telegram bot.
    /// Groups are stored in SQLite (ConnectionStrings:BukStatus). Statuses come from the BCC Contributions API,
    /// or from made-up data when BccApi:UseFakeData is true.
    /// </summary>
    public static IServiceCollection AddBukStatusCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        var bcc = configuration.GetSection(BccApiOptions.SectionName).Get<BccApiOptions>() ?? new BccApiOptions();
        if (bcc.UseFakeData)
            services.AddSingleton<IMemberStatusService, FakeMemberStatusService>();
        else
        {
            services.AddBccContributionsApi(configuration);
            services.AddSingleton<IMemberStatusService, BccMemberStatusService>();
        }

        var connectionString = configuration.GetConnectionString("BukStatus") ?? "Data Source=bukstatus.db";
        services.AddDbContextFactory<BukStatusDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<GroupService>();
        services.AddSingleton<GroupStatusService>();

        return services;
    }

    /// <summary>
    /// Registers the BCC Contributions API client (<see cref="IContributionsApiClient"/>) with OAuth client
    /// credentials read from the "BccApi" section. Tokens are fetched from login.bcc.no and cached/refreshed by
    /// the BCC platform client, so consumers just inject the client and call it.
    /// </summary>
    public static IServiceCollection AddBccContributionsApi(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(BccApiOptions.SectionName);
        services.AddOptions<BccApiOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The BCC installer takes its options eagerly, so bind a copy here as well.
        var bcc = section.Get<BccApiOptions>() ?? new BccApiOptions();
        var environment = bcc.AppEnvironment;

        ContributionsApiScope[] scopes = bcc.IncludeWriteScope
            ? [ContributionsApiScope.ContributionsRead, ContributionsApiScope.ContributionsWrite]
            : [ContributionsApiScope.ContributionsRead];

        var auth = new OAuthOptions()
            .WithEnvironmentDefaults(environment)
            .WithCredentials(bcc.ClientId, bcc.ClientSecret, string.Join(' ', scopes.Select(s => s.Value)));

        services.AddBccContributionsApiClient(new ContributionsApiClientOptions
        {
            Environment = environment,
            Auth = auth,
            Scopes = scopes,
        });

        return services;
    }
}
