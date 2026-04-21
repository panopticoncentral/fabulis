namespace Fabulis.Server.Data;

public class Story
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public List<StoryVersion> Versions { get; set; } = [];
}
