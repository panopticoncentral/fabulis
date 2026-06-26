namespace Fabulis.Server.Data;

public class OneLiner
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Text { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
}
