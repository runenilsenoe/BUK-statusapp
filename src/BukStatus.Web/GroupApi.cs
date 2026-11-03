using BukStatus.Core.Groups;
using BukStatus.Web.Auth;

namespace BukStatus.Web;

/// <summary>JSON endpoints for groups and their status, e.g. for the Telegram bot or other tools.</summary>
public static class GroupApi
{
    public record CreateGroupRequest(string Name);
    public record AddMemberRequest(int PersonId, string? DisplayName);

    /// <summary>Requires a logged-in, allowed user or an X-Api-Key header (see AuthSetup).</summary>
    public static IEndpointRouteBuilder MapGroupApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/groups").RequireAuthorization(AuthSetup.Policy);

        api.MapGet("/", async (GroupStatusService statuses, CancellationToken ct) =>
            Results.Ok(await statuses.GetAllAsync(ct)));

        api.MapGet("/{groupId:int}", async (int groupId, GroupStatusService statuses, CancellationToken ct) =>
            await statuses.GetAsync(groupId, ct) is { } status ? Results.Ok(status) : Results.NotFound());

        api.MapPost("/", (CreateGroupRequest request, GroupService groups, CancellationToken ct) =>
            Handle(async () =>
            {
                var group = await groups.CreateAsync(request.Name, ct);
                return Results.Created($"/api/groups/{group.Id}", new { group.Id, group.Name });
            }));

        api.MapDelete("/{groupId:int}", async (int groupId, GroupService groups, CancellationToken ct) =>
        {
            await groups.DeleteAsync(groupId, ct);
            return Results.NoContent();
        });

        api.MapPost("/{groupId:int}/members", (int groupId, AddMemberRequest request, GroupService groups, CancellationToken ct) =>
            Handle(async () =>
            {
                var member = await groups.AddMemberAsync(groupId, request.PersonId, request.DisplayName, ct);
                return Results.Ok(new { member.PersonId, member.DisplayName });
            }));

        api.MapDelete("/{groupId:int}/members/{personId:int}", async (int groupId, int personId, GroupService groups, CancellationToken ct) =>
        {
            await groups.RemoveMemberAsync(groupId, personId, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (GroupException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
