using BccCode.Contributions.Api.Client;
using BccCode.Platform;
using BukStatus.Core.Bcc;
using BukStatus.Core.Groups;
using BukStatus.Core.Pot;
using BukStatus.Core.Status;
using BukStatus.Core.Teams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BukStatus.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers member statuses (<see cref="IMemberStatusService"/>), contribution history, groups
    /// (<see cref="GroupService"/>, <see cref="GroupStatusService"/>) and teams with their reports
    /// (<see cref="TeamService"/>, <see cref="TeamReportService"/>) and the pot screen (<see cref="PotService"/>). Shared by the website and the Telegram bot.
    /// Groups and teams are stored in SQLite (ConnectionStrings:BukStatus). Statuses come from the BCC Contributions API,
    /// or from made-up data when BccApi:UseFakeData is true.
    /// </summary>
    public static IServiceCollection AddBukStatusCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        var bcc = configuration.GetSection(BccApiOptions.SectionName).Get<BccApiOptions>() ?? new BccApiOptions();
        if (bcc.UseFakeData)
        {
            services.AddSingleton<IMemberStatusService, FakeMemberStatusService>();
            services.AddSingleton<IContributionHistoryService, FakeContributionHistoryService>();
            services.AddSingleton<IExtraContributionsSource, FakeExtraContributionsSource>();
        }
        else
        {
            services.AddBccContributionsApi(configuration);
            services.AddSingleton<IMemberStatusService, BccMemberStatusService>();
            services.AddSingleton<IContributionHistoryService, BccContributionHistoryService>();
            services.AddSingleton<IExtraContributionsSource, BccExtraContributionsSource>();
        }

        // myshare is the backup for extra contributions (tried after the BCC API), and only used when configured.
        services.AddOptions<MyshareOptions>().Bind(configuration.GetSection(MyshareOptions.SectionName));
        services.AddHttpClient(MyshareExtraContributionsSource.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IExtraContributionsSource, MyshareExtraContributionsSource>();
        services.AddSingleton<ExtraContributionsService>();

        var connectionString = configuration.GetConnectionString("BukStatus") ?? "Data Source=bukstatus.db";
        services.AddDbContextFactory<BukStatusDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<GroupService>();
        services.AddSingleton<GroupStatusService>();
        services.AddSingleton<TeamService>();
        services.AddSingleton<TeamReportService>();
        services.AddSingleton<PotService>();

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
