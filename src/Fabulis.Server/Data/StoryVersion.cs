namespace Fabulis.Server.Data;

public class StoryVersion
{
    public int Id { get; set; }
    public int StoryId { get; set; }
    public int VersionNumber { get; set; }
    public required string ModelName { get; set; }
    public DateTime CreatedAt { get; set; }

    public Story Story { get; set; } = null!;
    public List<StoryMessage> Messages { get; set; } = [];
}
