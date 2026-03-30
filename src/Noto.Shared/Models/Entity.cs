using System.Text.Json;

namespace Noto.Shared.Models;

public class Entity
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public JsonDocument? Meta { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<Attachment> Attachments { get; set; } = [];
    public List<Link> LinksFrom { get; set; } = [];
    public List<Link> LinksTo { get; set; } = [];
    public List<Embedding> Embeddings { get; set; } = [];
}

public static class EntityTypes
{
    public const string Capture = "capture";
    public const string Thread = "thread";
    public const string Stash = "stash";
    public const string Chat = "chat";            // AI conversation transcript
    public const string AutoSummary = "autosummary";
}
