using BukStatus.Core.Clubs;
using BukStatus.Core.Status;
using BukStatus.Web.Auth;

namespace BukStatus.Web;

public static class ClubApi
{
    public static IEndpointRouteBuilder MapClubApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/clubs").RequireAuthorization(AuthSetup.Policy);
        api.MapGet("/", async (IClubContext clubs, CancellationToken ct) =>
            Results.Ok((await clubs.GetClubsAsync(ct)).Select(c => new { c.Id, c.TeamId, c.Name, MemberCount = c.Members.Count })));
        api.MapGet("/{clubId:int}/members", async (int clubId, IClubContext clubs, CancellationToken ct) =>
        {
            var club = (await clubs.GetClubsAsync(ct)).FirstOrDefault(c => c.Id == clubId);
            if (club is null) return Results.NotFound();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Oslo").DateTime);
            return Results.Ok(club.Members.Select(m => new { m.Id, m.DisplayName, m.FirstName, m.LastName, Age = m.Age(today) }));
        });
        api.MapGet("/{clubId:int}/status", async (int clubId, IClubContext clubs, IMemberStatusService statuses, CancellationToken ct) =>
        {
            var club = (await clubs.GetClubsAsync(ct)).FirstOrDefault(c => c.Id == clubId);
            if (club is null) return Results.NotFound();
            var data = await statuses.GetStatusesAsync(club.Members.Select(m => m.Id), ct);
            return Results.Ok(club.Members.Select(m => new { m.Id, m.DisplayName, Status = data.GetValueOrDefault(m.Id) }));
        });
        app.MapGet("/clubs/select/{clubId:int}", async (int clubId, HttpContext http, IClubContext clubs, CancellationToken ct) =>
        {
            if (!(await clubs.GetClubsAsync(ct)).Any(c => c.Id == clubId)) return Results.NotFound();
            http.Response.Cookies.Append(ClubContext.CookieName, clubId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromDays(30) });
            return Results.LocalRedirect("/clubs");
        }).RequireAuthorization(AuthSetup.Policy);
        return app;
    }
}
