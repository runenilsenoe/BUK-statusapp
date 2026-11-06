using BukStatus.Core.Pot;
using BukStatus.Web.Auth;

namespace BukStatus.Web;

/// <summary>JSON endpoint for the pot screen, e.g. for the Telegram bot.</summary>
public static class PotApi
{
    /// <summary>Requires a logged-in, allowed user or an X-Api-Key header (see AuthSetup).</summary>
    public static IEndpointRouteBuilder MapPotApi(this IEndpointRouteBuilder app)
    {
        // ?source=lag | lag-{id} | gruppe-{id}&simulated=10000&strategy=Even|MaximizeAjour
        app.MapGet("/api/pot", async (string? source, decimal? simulated, PotStrategy? strategy, PotService pot, CancellationToken ct) =>
            {
                var screen = await pot.GetScreenAsync(source, simulated ?? 0, strategy ?? PotStrategy.Even, ct);
                var result = screen.Result;
                return Results.Ok(new
                {
                    Source = screen.Source,
                    screen.Settings.Title,
                    screen.Settings.SamvirkPercent,
                    ExtraContributions = screen.Extra.Amount,
                    ExtraContributionsSource = screen.Extra.Source,
                    ExtraContributionsWarning = screen.Extra.Warning,
                    Milestones = screen.Settings.MilestoneList,
                    screen.Simulated,
                    Strategy = result.Strategy.ToString(),
                    result.Pot,
                    result.PotUsed,
                    result.Count,
                    result.MembersWithoutStatus,
                    result.OnTrackCount,
                    result.OnTrackPercent,
                    result.OnTrackWithPotCount,
                    result.OnTrackWithPotPercent,
                    screen.CombinedPercent,
                    result.RemainingBeforePot,
                    result.RemainingForAllOnTrack,
                    Members = result.Members.Select(m => new
                    {
                        m.PersonId,
                        m.DisplayName,
                        m.Status.TotalContribution,
                        m.Status.AjourTarget,
                        m.Boost,
                        m.IsOnTrack,
                        m.IsOnTrackWithPot,
                    }),
                });
            })
            .RequireAuthorization(AuthSetup.Policy);

        return app;
    }
}
