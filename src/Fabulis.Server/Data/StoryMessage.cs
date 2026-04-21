namespace Fabulis.Server.Data;

public class StoryMessage
{
    public int Id { get; set; }
    public int StoryVersionId { get; set; }
    public MessageRole Role { get; set; }
    public required string Content { get; set; }
    public int SortOrder { get; set; }

    public StoryVersion Version { get; set; } = null!;
}
