namespace Noto.Shared.Models;

public class Link
{
    public Guid Id { get; set; }
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }

    public Entity From { get; set; } = null!;
    public Entity To { get; set; } = null!;
}
