using System.Net.Http.Json;
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
        if (existing != null) return existing;

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
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var embedding = json?.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray();

            return embedding;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to get embedding from {Endpoint}", endpoint);
            return null;
        }
    }

    public async Task<bool> EmbedEntity(Guid entityId, EmbeddingProvider provider)
    {
        var entity = await _db.Entities.FindAsync(entityId);
        if (entity == null) return false;

        var text = $"{entity.Title ?? ""} {entity.Body ?? ""}".Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Truncate to reasonable length for embedding
        if (text.Length > 8000) text = text[..8000];

        var vector = await GetEmbeddingFromApi(text, provider);
        if (vector == null) return false;

        // Upsert: check if embedding already exists for this entity+provider
        var existing = await _db.Embeddings
            .FirstOrDefaultAsync(e => e.EntityId == entityId && e.ProviderId == provider.Id);

        if (existing != null)
        {
            existing.Vector = new Vector(vector);
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.Embeddings.Add(new Embedding
            {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                ProviderId = provider.Id,
                Vector = new Vector(vector),
                CreatedAt = DateTime.UtcNow,
            });
        }

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
                SELECT e.* FROM embeddings e
                WHERE e.provider_id = {provider.Id}
                ORDER BY e.vector <=> {pgVector}::vector
                LIMIT {limit}")
            .Include(e => e.Entity)
            .ToListAsync();

        return results.Select(e => (e.Entity, 0.0)).ToList();
    }

    public async Task<int> GetEmbeddedCount(Guid providerId)
    {
        return await _db.Embeddings.CountAsync(e => e.ProviderId == providerId);
    }

    public async Task<List<Guid>> GetUnembeddedEntityIds(Guid providerId, int limit = 50)
    {
        return await _db.Entities
            .Where(e => e.Body != null && e.Body != "")
            .Where(e => !_db.Embeddings.Any(emb => emb.EntityId == e.Id && emb.ProviderId == providerId))
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .Select(e => e.Id)
            .ToListAsync();
    }
}
