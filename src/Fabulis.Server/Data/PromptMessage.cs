namespace Fabulis.Server.Data;

public class PromptMessage
{
    public int Id { get; set; }
    public int PromptId { get; set; }
    public required string Content { get; set; }
    public int SortOrder { get; set; }

    public Prompt Prompt { get; set; } = null!;
}
