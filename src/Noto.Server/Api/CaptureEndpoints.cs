using System.Text.Json;
using Noto.Server.Data;
using Noto.Server.Services;
using Noto.Shared.Models;

namespace Noto.Server.Api;

public static class CaptureEndpoints
{
    public static void MapCaptureApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Serve attachments
        api.MapGet("/attachments/{id:guid}", async (Guid id, NotoDbContext db, IConfiguration config) =>
        {
            var attachment = await db.Attachments.FindAsync(id);
            if (attachment == null) return Results.NotFound();

            var mediaRoot = config["Storage:MediaRoot"] ?? "/Users/pragun/noto-data/media";
            var fullPath = Path.IsPathRooted(attachment.StoragePath)
                ? attachment.StoragePath
                : Path.Combine(mediaRoot, attachment.StoragePath);

            if (!File.Exists(fullPath)) return Results.NotFound();
            return Results.File(fullPath, attachment.MimeType ?? "application/octet-stream");
        });

        // Unified capture: one entity, multiple attachments (text + photos + audio)
        api.MapPost("/capture", async (HttpRequest request, EntityService svc, IConfiguration config) =>
        {
            var form = await request.ReadFormAsync();
            var title = form["title"].FirstOrDefault();
            var body = form["body"].FirstOrDefault();
            var mediaRoot = config["Storage:MediaRoot"] ?? "/Users/pragun/noto-data/media";

            // Must have at least text or a file
            var hasText = !string.IsNullOrWhiteSpace(body);
            var hasFiles = form.Files.Count > 0;
            if (!hasText && !hasFiles)
                return Results.BadRequest("nothing to capture");

            // Create one entity
            var entity = await svc.Create(EntityTypes.Capture, title?.Trim(), body?.Trim());

            // Attach all files
            foreach (var file in form.Files)
            {
                var isImage = file.ContentType?.StartsWith("image/") == true;
                var isAudio = file.ContentType?.StartsWith("audio/") == true
                    || file.FileName?.EndsWith(".webm") == true
                    || file.FileName?.EndsWith(".ogg") == true;

                string subdir, kind;
                if (isImage) { subdir = Path.Combine("photos", DateTime.UtcNow.ToString("yyyy-MM")); kind = "image"; }
                else if (isAudio) { subdir = Path.Combine("audio", DateTime.UtcNow.ToString("yyyy-MM")); kind = "audio"; }
                else { subdir = Path.Combine("files", DateTime.UtcNow.ToString("yyyy-MM")); kind = "file"; }

                Directory.CreateDirectory(Path.Combine(mediaRoot, subdir));

                var ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(ext))
                    ext = isImage ? ".jpg" : isAudio ? ".webm" : ".bin";

                var filename = $"{Guid.NewGuid()}{ext}";
                var relativePath = Path.Combine(subdir, filename);

                using (var stream = File.Create(Path.Combine(mediaRoot, relativePath)))
                    await file.CopyToAsync(stream);

                await svc.AddAttachment(entity.Id, kind, file.FileName ?? filename, relativePath, file.ContentType);
            }

            return Results.Ok(new { id = entity.Id });
        }).DisableAntiforgery();

        // Keep the old endpoints for backwards compat
        api.MapPost("/capture/text", async (HttpRequest request, EntityService svc) =>
        {
            var form = await request.ReadFormAsync();
            var title = form["title"].FirstOrDefault();
            var body = form["body"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(body)) return Results.BadRequest("no body");
            var entity = await svc.Create(EntityTypes.Capture, title, body?.Trim());
            return Results.Ok(new { id = entity.Id });
        }).DisableAntiforgery();

        api.MapPost("/capture/photo", async (HttpRequest request, EntityService svc, IConfiguration config) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            var caption = form["caption"].FirstOrDefault();
            if (file == null) return Results.BadRequest("no file");

            var mediaRoot = config["Storage:MediaRoot"] ?? "/Users/pragun/noto-data/media";
            var subdir = Path.Combine("photos", DateTime.UtcNow.ToString("yyyy-MM"));
            Directory.CreateDirectory(Path.Combine(mediaRoot, subdir));
            var ext = Path.GetExtension(file.FileName); if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var filename = $"{Guid.NewGuid()}{ext}";
            var relativePath = Path.Combine(subdir, filename);
            using (var stream = File.Create(Path.Combine(mediaRoot, relativePath))) await file.CopyToAsync(stream);

            var entity = await svc.Create(EntityTypes.Capture, caption, null);
            await svc.AddAttachment(entity.Id, "image", file.FileName ?? filename, relativePath, file.ContentType);
            return Results.Ok(new { id = entity.Id });
        }).DisableAntiforgery();

        api.MapPost("/capture/audio", async (HttpRequest request, EntityService svc, IConfiguration config) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file == null) return Results.BadRequest("no file");

            var mediaRoot = config["Storage:MediaRoot"] ?? "/Users/pragun/noto-data/media";
            var subdir = Path.Combine("audio", DateTime.UtcNow.ToString("yyyy-MM"));
            Directory.CreateDirectory(Path.Combine(mediaRoot, subdir));
            var ext = Path.GetExtension(file.FileName); if (string.IsNullOrEmpty(ext)) ext = ".webm";
            var filename = $"{Guid.NewGuid()}{ext}";
            var relativePath = Path.Combine(subdir, filename);
            using (var stream = File.Create(Path.Combine(mediaRoot, relativePath))) await file.CopyToAsync(stream);

            var entity = await svc.Create(EntityTypes.Capture, "voice note", null);
            await svc.AddAttachment(entity.Id, "audio", filename, relativePath, file.ContentType ?? "audio/webm");
            return Results.Ok(new { id = entity.Id });
        }).DisableAntiforgery();
    }
}
