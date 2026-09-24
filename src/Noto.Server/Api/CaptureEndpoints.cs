using System.Text.Json;
using Noto.Server.Data;
using Noto.Server.Services;
using Noto.Shared.Models;

namespace Noto.Server.Api;

public static class CaptureEndpoints
{
    // meta is free-form JSON; read a string field defensively.
    private static string? MetaString(JsonDocument? meta, string field)
    {
        if (meta == null) return null;
        try
        {
            if (meta.RootElement.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }
        catch { }
        return null;
    }

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

            // Structured capture fields. These live in meta rather than being
            // encoded in thread names, so the same note can be viewed by person,
            // by medium or by date — grouping is a view concern, not storage.
            var mode = form["mode"].FirstOrDefault();
            var recFrom = form["from"].FirstOrDefault();
            var recKind = form["kind"].FirstOrDefault();
            var seedForm = form["form"].FirstOrDefault();

            JsonDocument? meta = null;
            var metaFields = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(mode)) metaFields["mode"] = mode.Trim();
            if (!string.IsNullOrWhiteSpace(recFrom)) metaFields["from"] = recFrom.Trim();
            if (!string.IsNullOrWhiteSpace(recKind)) metaFields["kind"] = recKind.Trim();
            if (!string.IsNullOrWhiteSpace(seedForm)) metaFields["form"] = seedForm.Trim();
            if (metaFields.Count > 0)
                meta = JsonDocument.Parse(JsonSerializer.Serialize(metaFields));

            // Create one entity
            var entity = await svc.Create(EntityTypes.Capture, title?.Trim(), body?.Trim(), meta);

            // Tag into existing threads, and optionally a brand new one. A note
            // captured on a walk has to be able to start a song that does not
            // exist yet without a round trip to the desktop.
            foreach (var raw in form["threads"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (Guid.TryParse(raw.Trim(), out var threadId))
                    await svc.TagCapture(entity.Id, threadId);

            var newThread = form["new_thread"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(newThread))
            {
                var kindMeta = JsonDocument.Parse(JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["kind"] = form["new_thread_kind"].FirstOrDefault() ?? "theme" }));
                var created = await svc.Create(EntityTypes.Thread, newThread.Trim(), null, kindMeta);
                await svc.TagCapture(entity.Id, created.Id);
            }

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

        // Threads, most recently used first: the song you are working on this
        // week is the one you will tag ten more times.
        api.MapGet("/threads", async (EntityService svc) =>
        {
            var stats = await svc.GetThreadsWithStats();
            return Results.Ok(stats.Select(t => new
            {
                id = t.Thread.Id,
                title = t.Thread.Title,
                kind = MetaString(t.Thread.Meta, "kind") ?? "theme",
                count = t.CaptureCount,
                lastAt = t.LastCapture,
            }).OrderByDescending(t => t.lastAt ?? DateTime.MinValue));
        });

        api.MapPost("/threads", async (HttpRequest request, EntityService svc) =>
        {
            var form = await request.ReadFormAsync();
            var title = form["title"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(title)) return Results.BadRequest("no title");
            var meta = JsonDocument.Parse(JsonSerializer.Serialize(
                new Dictionary<string, string> { ["kind"] = form["kind"].FirstOrDefault() ?? "theme" }));
            var thread = await svc.Create(EntityTypes.Thread, title.Trim(), null, meta);
            return Results.Ok(new { id = thread.Id, title = thread.Title });
        }).DisableAntiforgery();

        // Recommendations, flat. The client groups by person / medium / date —
        // three lenses over the same rows.
        api.MapGet("/recs", async (EntityService svc) =>
        {
            var all = await svc.GetByType(EntityTypes.Capture);
            return Results.Ok(all
                .Where(e => MetaString(e.Meta, "mode") == "rec")
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new
                {
                    id = e.Id,
                    title = e.Title,
                    body = e.Body,
                    from = MetaString(e.Meta, "from"),
                    kind = MetaString(e.Meta, "kind"),
                    createdAt = e.CreatedAt,
                }));
        });

        // Templates
        api.MapGet("/templates", async (EntityService svc) =>
        {
            var templates = await svc.GetTemplates();
            return Results.Ok(templates.Select(t => new { id = t.Id, title = t.Title, body = t.Body }));
        });

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
