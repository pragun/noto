using Pgvector;

namespace Noto.Shared.Models;

public class Embedding
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid ProviderId { get; set; }
    public int ChunkIndex { get; set; }          // 0-based position within the entity
    public string? ChunkText { get; set; }       // the text this vector was built from
    public Vector Vector { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public Entity Entity { get; set; } = null!;
    public EmbeddingProvider EmbeddingProvider { get; set; } = null!;
}
