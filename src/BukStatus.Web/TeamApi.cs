using BukStatus.Core.Teams;
using BukStatus.Web.Auth;

namespace BukStatus.Web;

/// <summary>JSON endpoints for teams and the team reports, e.g. for the Telegram bot.</summary>
public static class TeamApi
{
    /// <summary>Requires a logged-in, allowed user or an X-Api-Key header (see AuthSetup).</summary>
    public static IEndpointRouteBuilder MapTeamApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/teams").RequireAuthorization(AuthSetup.Policy);

        api.MapGet("/", async (TeamService teams, CancellationToken ct) =>
            Results.Ok((await teams.GetAllAsync(ct)).Select(t => new
            {
                t.Id,
                t.Name,
                t.LeaderName,
                t.BonusPercent,
                Members = t.Members.Select(m => new { m.PersonId, m.DisplayName }),
            })));

        // ?month=2026-10, defaults to the current month.
        api.MapGet("/report/month", async (string? month, TeamReportService reports, CancellationToken ct) =>
        {
            var today = reports.Today;
            if (month is null)
                return Results.Ok(await reports.GetMonthlyAsync(today.Year, today.Month, ct));
            return DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", out var picked)
                ? Results.Ok(await reports.GetMonthlyAsync(picked.Year, picked.Month, ct))
                : Results.BadRequest(new { error = "month must be yyyy-MM" });
        });

        // ?year=2026, defaults to the current year.
        api.MapGet("/report/weeks", async (int? year, TeamReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.GetWeeklyAsync(year ?? reports.Today.Year, ct)));

        return app;
    }
}
