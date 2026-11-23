using BukStatus.Core.Groups;
using BukStatus.Core.Samvirk;
using BukStatus.Web.Auth;
namespace BukStatus.Web;
public static class SamvirkApi
{
    public static void MapSamvirkApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/samvirk").RequireAuthorization(AuthSetup.Policy);
        // Machine-only bridge. Browser upload uses the authorized Blazor service directly.
        api.AddEndpointFilter(async (context, next) => context.HttpContext.User.Identity?.AuthenticationType != ApiKeyAuthenticationHandler.SchemeName
            ? Results.Forbid() : await next(context));
        api.MapPost("/preview", async (HttpRequest request, SamvirkService service, CancellationToken ct) =>
        {
            if (request.ContentLength is null or > SamvirkParser.MaxBytes + 65536) return Results.BadRequest(new { error = "Filen er for stor." });
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count != 1) return Results.BadRequest(new { error = "Send én fil." });
            DateOnly? date = null;
            if (!string.IsNullOrWhiteSpace(form["date"]))
            {
                if (!DateOnly.TryParseExact(form["date"], "yyyy-MM-dd", out var supplied)) return Results.BadRequest(new { error = "Eksportdato må være yyyy-MM-dd." });
                date = supplied;
            }
            var file = form.Files[0];
            if (file.Length > SamvirkParser.MaxBytes) return Results.BadRequest(new { error = "Maksimalt 5 MB." });
            using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct);
            try { return Results.Ok(await service.PreviewAsync(buffer.ToArray(), file.FileName, date, ct)); }
            catch (GroupException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        api.MapPost("/confirm/{id:guid}", async (Guid id, SamvirkService service, CancellationToken ct) =>
        {
            try { await service.ConfirmAsync(id, ct); return Results.Ok(new { confirmed = true }); }
            catch (GroupException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        api.MapGet("/latest", async (SamvirkService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.LatestAsync(ct)); }
            catch (GroupException) { return Results.Forbid(); }
        });
    }
}
