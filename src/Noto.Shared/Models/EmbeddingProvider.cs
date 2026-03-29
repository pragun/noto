namespace Noto.Shared.Models;

public class EmbeddingProvider
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";          // e.g. "openai-text-embedding-3-small"
    public string ModelId { get; set; } = "";        // e.g. "text-embedding-3-small"
    public string Provider { get; set; } = "";       // e.g. "openai", "openrouter", "local"
    public string? Endpoint { get; set; }            // cloud: "https://api.openai.com/v1/embeddings"
                                                     // local: "http://localhost:8090/v1/embeddings" or null for in-process
    public string? ModelPath { get; set; }           // local: "/Users/pragun/models/nomic-embed-text-v1.5.gguf"
                                                     // cloud: null
    public int Dimensions { get; set; }              // e.g. 1536, 768
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }               // which provider to use for new embeddings
    public string? Notes { get; set; }               // e.g. "deprecated 2025-01, re-embed by 2025-06"

    public List<Embedding> Embeddings { get; set; } = [];
}
