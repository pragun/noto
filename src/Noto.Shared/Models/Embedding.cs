using Pgvector;

namespace Noto.Shared.Models;

public class Embedding
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid ProviderId { get; set; }
    public Vector Vector { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public Entity Entity { get; set; } = null!;
    public EmbeddingProvider EmbeddingProvider { get; set; } = null!;
}
