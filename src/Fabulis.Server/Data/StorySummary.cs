namespace Fabulis.Server.Data;

/// <summary>
/// Pure helpers for turning a story's versions into a one-paragraph
/// summary. The LLM call lives in <see cref="SummaryService"/>; everything
/// here is deterministic and unit-tested. Parallel to <see cref="TitleGeneration"/>.
/// </summary>
public static class StorySummary
{
    public const string DefaultPrompt =
        "You write concise summaries of stories. Given the full text of a story — and, when an existing summary is provided, that summary to update — respond with a single paragraph (3 to 5 sentences) capturing the main characters, setting, and arc. Output only the summary paragraph: no preamble, no headings, no quotation marks, no commentary.";

    /// <summary>
    /// Joins a version's assistant responses (in sort order), ignoring the
    /// user-side prompts. Returns "" when the version has no responses.
    /// </summary>
    public static string BuildVersionBody(IEnumerable<StoryMessage> messages) =>
        string.Join("\n\n", messages
            .Where(m => m.Role == MessageRole.Response)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.Content));

    /// <summary>
    /// Builds the user message. With no prior summary the model just sees the
    /// story content; with one it sees the prior summary plus the content to
    /// fold in.
    /// </summary>
    public static string ComposeUserMessage(string? priorSummary, string storyContent)
    {
        if (string.IsNullOrWhiteSpace(priorSummary))
            return storyContent;

        return $"EXISTING SUMMARY:\n{priorSummary}\n\nNEW STORY CONTENT:\n{storyContent}";
    }

    /// <summary>
    /// Normalizes model output to a single paragraph: trims, drops blank
    /// lines, and joins the rest with single spaces.
    /// </summary>
    public static string CleanSummary(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var lines = raw
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        return string.Join(" ", lines);
    }

    /// <summary>
    /// True when the story has unsummarized content: either nothing has been
    /// summarized yet, or a newer version exists than the one last summarized.
    /// </summary>
    public static bool NeedsWork(int? summarizedThroughVersion, int latestVersion) =>
        summarizedThroughVersion is not int through || through < latestVersion;
}
