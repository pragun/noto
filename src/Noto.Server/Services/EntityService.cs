using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Noto.Server.Data;
using Noto.Shared.Models;

namespace Noto.Server.Services;

public class EntityService
{
    private readonly NotoDbContext _db;

    public EntityService(NotoDbContext db)
    {
        _db = db;
    }

    public async Task<Attachment> AddAttachment(Guid entityId, string kind, string filename, string storagePath, string? mimeType)
    {
        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Kind = kind,
            Filename = filename,
            StoragePath = storagePath,
            MimeType = mimeType,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync();
        return attachment;
    }

    public async Task<Entity> Create(string type, string? title, string? body, JsonDocument? meta = null)
    {
        var entity = new Entity
        {
            Id = Guid.NewGuid(),
            Type = type,
            Title = title,
            Body = body,
            Meta = meta,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Entities.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    public async Task<Entity?> GetById(Guid id)
    {
        return await _db.Entities
            .Include(e => e.Attachments)
            .Include(e => e.LinksFrom).ThenInclude(l => l.To)
            .Include(e => e.LinksTo).ThenInclude(l => l.From)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<List<Entity>> GetStream(int page = 0, int pageSize = 20, string? type = null)
    {
        var query = _db.Entities.Where(e => e.DeletedAt == null);

        if (type != null)
            query = query.Where(e => e.Type == type);

        return await query
            .Include(e => e.LinksFrom).ThenInclude(l => l.To)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<Entity>> GetByType(string type)
    {
        return await _db.Entities
            .Where(e => e.Type == type && e.DeletedAt == null)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
    }

    public async Task<Entity?> Update(Guid id, string? title = null, string? body = null, JsonDocument? meta = null)
    {
        var entity = await _db.Entities.FindAsync(id);
        if (entity == null) return null;

        if (title != null) entity.Title = title;
        if (body != null) entity.Body = body;
        if (meta != null) entity.Meta = meta;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> Delete(Guid id)
    {
        var entity = await _db.Entities.FindAsync(id);
        if (entity == null) return false;

        entity.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> Restore(Guid id)
    {
        var entity = await _db.Entities.FindAsync(id);
        if (entity == null) return false;

        entity.DeletedAt = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<Entity>> GetTrash()
    {
        return await _db.Entities
            .Where(e => e.DeletedAt != null)
            .OrderByDescending(e => e.DeletedAt)
            .ToListAsync();
    }

    public async Task<int> PermanentDelete(Guid id)
    {
        var entity = await _db.Entities.FindAsync(id);
        if (entity == null) return 0;
        _db.Entities.Remove(entity);
        await _db.SaveChangesAsync();
        return 1;
    }

    public async Task<int> EmptyTrash()
    {
        var trashed = await _db.Entities.Where(e => e.DeletedAt != null).ToListAsync();
        _db.Entities.RemoveRange(trashed);
        await _db.SaveChangesAsync();
        return trashed.Count;
    }

    public async Task<int> Count(string? type = null)
    {
        var query = _db.Entities.Where(e => e.DeletedAt == null);
        if (type != null) query = query.Where(e => e.Type == type);
        return await query.CountAsync();
    }

    // Thread operations (threads are just entities + links)
    public async Task<Link> TagCapture(Guid captureId, Guid threadId)
    {
        var link = new Link
        {
            Id = Guid.NewGuid(),
            FromId = captureId,
            ToId = threadId,
            Reason = "tagged",
            CreatedAt = DateTime.UtcNow,
        };
        _db.Links.Add(link);
        await _db.SaveChangesAsync();
        return link;
    }

    public async Task<bool> UntagCapture(Guid captureId, Guid threadId)
    {
        var link = await _db.Links
            .FirstOrDefaultAsync(l => l.FromId == captureId && l.ToId == threadId && l.Reason == "tagged");
        if (link == null) return false;
        _db.Links.Remove(link);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<Entity>> GetThreadCaptures(Guid threadId, int limit = 100)
    {
        return await _db.Links
            .Where(l => l.ToId == threadId && l.Reason == "tagged")
            .OrderByDescending(l => l.From.CreatedAt)
            .Take(limit)
            .Select(l => l.From)
            .ToListAsync();
    }

    public async Task<List<Entity>> GetEntityThreads(Guid entityId)
    {
        return await _db.Links
            .Where(l => l.FromId == entityId && l.Reason == "tagged")
            .Select(l => l.To)
            .ToListAsync();
    }

    // Generic links
    public async Task<Link> CreateLink(Guid fromId, Guid toId, string? reason = null)
    {
        var link = new Link
        {
            Id = Guid.NewGuid(),
            FromId = fromId,
            ToId = toId,
            Reason = reason,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Links.Add(link);
        await _db.SaveChangesAsync();
        return link;
    }

    public async Task<List<Link>> GetLinks(Guid entityId)
    {
        return await _db.Links
            .Include(l => l.From)
            .Include(l => l.To)
            .Where(l => l.FromId == entityId || l.ToId == entityId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> DeleteLink(Guid linkId)
    {
        var link = await _db.Links.FindAsync(linkId);
        if (link == null) return false;
        _db.Links.Remove(link);
        await _db.SaveChangesAsync();
        return true;
    }

    // Thread with stats
    public async Task<List<(Entity Thread, int CaptureCount, DateTime? LastCapture)>> GetThreadsWithStats()
    {
        var threads = await _db.Entities
            .Where(e => e.Type == EntityTypes.Thread)
            .OrderBy(e => e.Title)
            .ToListAsync();

        var result = new List<(Entity, int, DateTime?)>();
        foreach (var t in threads)
        {
            var count = await _db.Links.CountAsync(l => l.ToId == t.Id && l.Reason == "tagged");
            var lastCapture = await _db.Links
                .Where(l => l.ToId == t.Id && l.Reason == "tagged")
                .OrderByDescending(l => l.From.CreatedAt)
                .Select(l => (DateTime?)l.From.CreatedAt)
                .FirstOrDefaultAsync();
            result.Add((t, count, lastCapture));
        }
        return result;
    }

    // Find thread by name (case-insensitive)
    public async Task<Entity?> FindThreadByName(string name)
    {
        return await _db.Entities
            .FirstOrDefaultAsync(e => e.Type == EntityTypes.Thread
                && e.Title != null
                && e.Title.ToLower() == name.ToLower());
    }

    // System prompts (stored as stash with meta.kind = "system-prompt")
    public async Task<List<Entity>> GetSystemPrompts()
    {
        var all = await _db.Entities
            .Where(e => e.Type == EntityTypes.Stash)
            .ToListAsync();

        return all.Where(e =>
        {
            if (e.Meta == null) return false;
            return e.Meta.RootElement.TryGetProperty("kind", out var k) && k.GetString() == "system-prompt";
        }).OrderBy(e => e.Title).ToList();
    }

    public async Task<Entity> CreateSystemPrompt(string name, string body)
    {
        var meta = System.Text.Json.JsonDocument.Parse("""{"kind":"system-prompt"}""");
        return await Create(EntityTypes.Stash, name, body, meta);
    }

    // Search (basic full-text for now, Phase 4 adds semantic)
    public async Task<List<Entity>> SearchText(string query, int limit = 20)
    {
        return await _db.Entities
            .Where(e => e.DeletedAt == null)
            .Where(e => EF.Functions.ToTsVector("english",
                (e.Title ?? "") + " " + (e.Body ?? ""))
                .Matches(EF.Functions.PlainToTsQuery("english", query)))
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }
}
