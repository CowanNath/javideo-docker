using Javideo.Worker.Services;
using Microsoft.AspNetCore.Mvc;

namespace Javideo.Worker.Endpoints;

public static class BackupEndpoints
{
    // Backups include cached images and can easily exceed Kestrel's 30 MB
    // default request limit (and the 128 MB multipart section limit).
    public const long MaxUploadBytes = 1024L * 1024 * 1024;

    public static void MapBackupEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/backup").WithTags("Backup");

        // Export all user data as a zip download.
        g.MapGet("/export", (BackupService backup) =>
        {
            var zipPath = backup.Export();
            // Stream the file; delete the temp file after sending.
            var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.None,
                bufferSize: 8192, FileOptions.DeleteOnClose);
            var fileName = Path.GetFileName(zipPath);
            return Results.File(stream, "application/zip", fileName);
        });

        // Export to a specific local path — REMOVED in the Docker build: it
        // allowed writing a zip to any path the worker could reach. The web UI
        // uses the /export zip download above instead.

        // Import a zip (multipart form upload, field name "file").
        g.MapPost("/import", async (HttpContext ctx, BackupService backup) =>
        {
            var tempZip = Path.Combine(Path.GetTempPath(), $"javideo-import-{Guid.NewGuid():N}.zip");
            try
            {
                var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
                var file = form.Files.GetFile("file");
                if (file == null || file.Length == 0)
                    return Results.BadRequest(new { ok = false, detail = "未提供文件" });

                await using (var fs = File.Create(tempZip))
                    await file.CopyToAsync(fs, ctx.RequestAborted);

                backup.Import(tempZip);
                return Results.Ok(new { ok = true, detail = "导入成功,请重启应用以生效" });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { ok = false, detail = $"备份文件无效: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, detail = $"导入失败: {ex.Message}" },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
            finally
            {
                if (File.Exists(tempZip)) File.Delete(tempZip);
            }
        }).WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes));
    }
}
