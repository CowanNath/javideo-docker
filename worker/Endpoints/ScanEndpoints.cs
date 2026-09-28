using Javideo.Worker.Services;

namespace Javideo.Worker.Endpoints;

public static class ScanEndpoints
{
    public static void MapScanEndpoints(this WebApplication app)
    {
        app.MapPost("/api/libraries/{id:long}/scan", async (long id, Scanner scanner, HttpContext ctx) =>
            Results.Ok(await scanner.ScanLibraryAsync(id, ctx.RequestAborted)))
           .WithTags("Scan");
    }
}
