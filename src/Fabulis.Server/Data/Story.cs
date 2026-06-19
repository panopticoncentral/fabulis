namespace Fabulis.Server.Data;

public enum SummaryStatus
{
    None = 0,
    Ready = 1,
    Failed = 2,
}

public class Story
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; set; }

    // Summary state (1:1 with the story). "Generating" is NOT stored here —
    // it is tracked in-memory by SummaryService so a restart can't strand a
    // story mid-generation.
    public string? SummaryText { get; set; }
    public SummaryStatus SummaryStatus { get; set; } = SummaryStatus.None;
    public int? SummarizedThroughVersion { get; set; }
    public string? SummaryError { get; set; }
    public DateTime? SummaryUpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public List<StoryVersion> Versions { get; set; } = [];
}
