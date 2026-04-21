namespace Fabulis.Server.Data;

public class Storyteller
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Prompt { get; set; }
    public required string ModelName { get; set; }
    public double Temperature { get; set; } = 0.7;
    public double? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public DateTime CreatedAt { get; set; }
}
