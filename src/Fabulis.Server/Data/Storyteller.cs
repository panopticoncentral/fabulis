namespace Fabulis.Server.Data;

public class Storyteller
{
    public const string DefaultTitlingPrompt =
        "You write titles for stories. Given the full text of a story, respond with a single short, evocative title — 2 to 6 words. Output only the title itself: no quotation marks, no trailing punctuation, no commentary.";

    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Prompt { get; set; }
    public required string TitlingPrompt { get; set; }
    public required string ModelName { get; set; }
    public double Temperature { get; set; } = 0.7;
    public double? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public double? MinP { get; set; }
    public int? TopK { get; set; }
    public double? TopA { get; set; }
    public DateTime CreatedAt { get; set; }
}
