namespace Fabulis.Server.Data;

public class DraftMessage
{
    public int Id { get; set; }
    public int DraftId { get; set; }
    public MessageRole Role { get; set; }
    public required string Content { get; set; }
    public int SortOrder { get; set; }

    public Draft Draft { get; set; } = null!;
}
