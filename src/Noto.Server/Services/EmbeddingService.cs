using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Noto.Server.Data;
using Noto.Shared.Models;
using Pgvector;

namespace Noto.Server.Services;

public class EmbeddingService
{
    private readonly NotoDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<EmbeddingService> _log;

    public EmbeddingService(NotoDbContext db, IHttpClientFactory httpFactory, ILogger<EmbeddingService> log)
    {
        _db = db;
        _http = httpFactory.CreateClient("Embeddings");
        _log = log;
    }

    public async Task<EmbeddingProvider?> GetActiveProvider()
    {
        return await _db.EmbeddingProviders.FirstOrDefaultAsync(p => p.IsActive);
    }

    public async Task<EmbeddingProvider> EnsureProvider(string name, string modelId, string provider,
        string endpoint, int dimensions)
    {
        var existing = await _db.EmbeddingProviders
            .FirstOrDefaultAsync(p => p.Name == name);
        if (existing != null)
        {
            // The endpoint is per-host (Ollama may be a container, localhost, or a tailnet
            // peer) so config must win over whatever was stored on first run.
            if (existing.Endpoint != endpoint)
            {
                _log.LogInformation("Embedding provider {Name}: endpoint {Old} -> {New}",
                    name, existing.Endpoint, endpoint);
                existing.Endpoint = endpoint;
                await _db.SaveChangesAsync();
            }
            return existing;
        }

        var ep = new EmbeddingProvider
        {
            Id = Guid.NewGuid(),
            Name = name,
            ModelId = modelId,
            Provider = provider,
            Endpoint = endpoint,
            Dimensions = dimensions,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        _db.EmbeddingProviders.Add(ep);
        await _db.SaveChangesAsync();
        _log.LogInformation("Created embedding provider: {Name} ({ModelId}, {Dims}d)", name, modelId, dimensions);
        return ep;
    }

    public async Task<float[]?> GetEmbeddingFromApi(string text, EmbeddingProvider provider)
        => (await TryGetEmbedding(text, provider)).Vector;

    // TooLong is reported separately so callers can shrink and retry rather than
    // giving up: token density varies wildly (prose vs. ChordPro vs. non-Latin
    // scripts), so no character budget reliably predicts the model's context.
    private async Task<(float[]? Vector, bool TooLong)> TryGetEmbedding(string text, EmbeddingProvider provider)
    {
        var endpoint = provider.Endpoint ?? "http://localhost:11434/v1/embeddings";

        var request = new
        {
            model = provider.ModelId,
            input = text
        };

        try
        {
            var response = await _http.PostAsJsonAsync(endpoint, request);
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var detail = await response.Content.ReadAsStringAsync();
                if (detail.Contains("context length", StringComparison.OrdinalIgnoreCase))
                    return (null, true);
                _log.LogError("Embedding endpoint rejected input: {Detail}", detail);
                return (null, false);
            }
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var embedding = json?.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray();

            return (embedding, false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to get embedding from {Endpoint}", endpoint);
            return (null, false);
        }
    }

    public const int MaxEmbedChars = 1800;
    public const int MinEmbedChars = 200;

    // Split on blank lines, then pack whole paragraphs together until the budget
    // is spent. Each chunk carries the title so a matched passage keeps its source.
    public static List<string> BuildChunks(string? title, string? body, int budget = MaxEmbedChars)
    {
        var prefix = string.IsNullOrWhiteSpace(title) ? "" : title.Trim() + "\n\n";
        var room = Math.Max(MinEmbedChars, budget - prefix.Length);

        var chunks = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length == 0) return;
            chunks.Add(prefix + current.ToString());
            current.Clear();
        }

        foreach (var para in SplitParagraphs(body ?? ""))
            foreach (var piece in FitToRoom(para, room))
            {
                if (current.Length > 0 && current.Length + 2 + piece.Length > room) Flush();
                if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }

        Flush();
        if (chunks.Count == 0 && prefix.Length > 0) chunks.Add(prefix.TrimEnd());
        return chunks;
    }

    private static IEnumerable<string> SplitParagraphs(string body) =>
        Regex.Split(body, @"\n\s*\n")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);

    // A single paragraph larger than the budget falls back to line boundaries,
    // then to a hard cut — a wall of text still has to be embedded somehow.
    private static IEnumerable<string> FitToRoom(string para, int room)
    {
        if (para.Length <= room) { yield return para; yield break; }

        var buf = new StringBuilder();
        foreach (var line in para.Split('\n'))
        {
            var rest = line;
            while (rest.Length > room)
            {
                if (buf.Length > 0) { yield return buf.ToString(); buf.Clear(); }
                yield return rest[..room];
                rest = rest[room..];
            }
            if (buf.Length > 0 && buf.Length + 1 + rest.Length > room) { yield return buf.ToString(); buf.Clear(); }
            if (buf.Length > 0) buf.Append('\n');
            buf.Append(rest);
        }
        if (buf.Length > 0) yield return buf.ToString();
    }

    // Token density varies far more than character count suggests (prose vs. ChordPro
    // vs. non-Latin scripts), so shrink and retry rather than guessing a budget.
    private async Task<float[]?> EmbedWithShrink(string text, Guid entityId, EmbeddingProvider provider)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var (vec, tooLong) = await TryGetEmbedding(text, provider);
            if (vec != null) return vec;
            if (!tooLong || text.Length <= MinEmbedChars) return null;

            text = text[..Math.Max(MinEmbedChars, text.Length / 2)];
            _log.LogWarning("Entity {Id} chunk exceeded model context — retrying at {Len} chars",
                entityId, text.Length);
        }
        return null;
    }

    public async Task<bool> EmbedEntity(Guid entityId, EmbeddingProvider provider)
    {
        var entity = await _db.Entities.FindAsync(entityId);
        if (entity == null) return false;

        var chunks = BuildChunks(entity.Title, entity.Body);
        if (chunks.Count == 0) return false;

        // Embed everything before touching what is already stored, so a failure
        // partway through leaves the previous vectors intact.
        var vectors = new List<(string Text, float[] Vector)>();
        foreach (var chunk in chunks)
        {
            var vector = await EmbedWithShrink(chunk, entityId, provider);
            if (vector != null) vectors.Add((chunk, vector));
        }
        if (vectors.Count == 0) return false;

        var existing = await _db.Embeddings
            .Where(e => e.EntityId == entityId && e.ProviderId == provider.Id)
            .ToListAsync();
        _db.Embeddings.RemoveRange(existing);

        for (var i = 0; i < vectors.Count; i++)
            _db.Embeddings.Add(new Embedding
            {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                ProviderId = provider.Id,
                ChunkIndex = i,
                ChunkText = vectors[i].Text,
                Vector = new Vector(vectors[i].Vector),
                CreatedAt = DateTime.UtcNow,
            });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<(Entity Entity, double Distance)>> SearchSemantic(
        string query, EmbeddingProvider provider, int limit = 20)
    {
        var queryVector = await GetEmbeddingFromApi(query, provider);
        if (queryVector == null) return [];

        var pgVector = new Vector(queryVector);

        // Raw SQL for cosine distance search — EF Core can't express this in LINQ
        var results = await _db.Embeddings
            .FromSqlInterpolated($@"
                SELECT * FROM (
                    SELECT DISTINCT ON (e.entity_id) e.*
                    FROM embeddings e
                    JOIN entities n ON n.id = e.entity_id
                    WHERE e.provider_id = {provider.Id} AND n.deleted_at IS NULL
                    ORDER BY e.entity_id, e.vector <=> {pgVector}::vector
                ) best
                ORDER BY best.vector <=> {pgVector}::vector
                LIMIT {limit}")
            .Include(e => e.Entity)
            .ToListAsync();

        return results.Select(e => (e.Entity, 0.0)).ToList();
    }

    public async Task<int> GetEmbeddedCount(Guid providerId)
    {
        return await _db.Embeddings.CountAsync(e => e.ProviderId == providerId);
    }

    // Drops every vector for a provider so the worker rebuilds them — needed
    // whenever chunking or the model changes.
    public async Task<int> ClearEmbeddings(Guid providerId)
    {
        return await _db.Embeddings.Where(e => e.ProviderId == providerId).ExecuteDeleteAsync();
    }

    // One entity may hold many chunks, so progress has to count entities.
    public async Task<int> GetEmbeddedEntityCount(Guid providerId)
    {
        return await _db.Embeddings
            .Where(e => e.ProviderId == providerId)
            .Select(e => e.EntityId)
            .Distinct()
            .CountAsync();
    }

    // Entities worth embedding: has body text, not trashed.
    private IQueryable<Entity> Embeddable() =>
        _db.Entities.Where(e => e.Body != null && e.Body != "" && e.DeletedAt == null);

    private IQueryable<Entity> Unembedded(Guid providerId) =>
        Embeddable().Where(e => !_db.Embeddings.Any(emb => emb.EntityId == e.Id && emb.ProviderId == providerId));

    public async Task<List<Guid>> GetUnembeddedEntityIds(Guid providerId, int limit = 50)
    {
        return await Unembedded(providerId)
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .Select(e => e.Id)
            .ToListAsync();
    }

    public async Task<int> GetUnembeddedCount(Guid providerId) =>
        await Unembedded(providerId).CountAsync();

    public async Task<int> GetEligibleCount() => await Embeddable().CountAsync();

    // Oldest waiting entities — shows what the backlog actually consists of.
    public async Task<List<Entity>> GetUnembeddedPreview(Guid providerId, int limit = 8)
    {
        return await Unembedded(providerId)
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<EmbeddingProvider>> GetAllProviders() =>
        await _db.EmbeddingProviders.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToListAsync();

    public async Task<DateTime?> GetLastEmbeddedAt(Guid providerId)
    {
        return await _db.Embeddings
            .Where(e => e.ProviderId == providerId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => (DateTime?)e.CreatedAt)
            .FirstOrDefaultAsync();
    }

    // Round-trips a token through the endpoint so the admin page can tell
    // "backlog because nothing is running" apart from "backlog because it's slow".
    public async Task<(bool Ok, string Message)> PingProvider(EmbeddingProvider provider)
    {
        var vector = await GetEmbeddingFromApi("ping", provider);
        if (vector == null)
            return (false, $"no response from {provider.Endpoint}");
        if (vector.Length != provider.Dimensions)
            return (false, $"returned {vector.Length}d, provider is configured for {provider.Dimensions}d");
        return (true, $"ok — {vector.Length}d from {provider.ModelId}");
    }
}
