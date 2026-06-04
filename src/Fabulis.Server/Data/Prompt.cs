namespace Fabulis.Server.Data;

public class Prompt
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public List<PromptMessage> Messages { get; set; } = [];
}
