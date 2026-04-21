namespace Fabulis.Server.Data;

public class Draft
{
    public int Id { get; set; }
    public int StorytellerID { get; set; }
    public string? Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Storyteller Storyteller { get; set; } = null!;
    public List<DraftMessage> Messages { get; set; } = [];
}
