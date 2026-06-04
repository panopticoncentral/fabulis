namespace Fabulis.Server.Data;

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Story> Stories { get; set; } = [];
    public List<Prompt> Prompts { get; set; } = [];
}
