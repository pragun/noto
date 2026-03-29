namespace Noto.Shared.Models;

public class Attachment
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string Kind { get; set; } = "";
    public string? Filename { get; set; }
    public string StoragePath { get; set; } = "";
    public string? MimeType { get; set; }
    public DateTime CreatedAt { get; set; }

    public Entity Entity { get; set; } = null!;
}
