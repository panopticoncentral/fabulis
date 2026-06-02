namespace Fabulis.Server.Data;

/// <summary>
/// Pure helpers for turning a draft's story text into a title. The
/// LLM call itself lives in the generate-title endpoint; everything
/// here is deterministic and unit-tested.
/// </summary>
public static class TitleGeneration
{
    /// <summary>
    /// Joins the assistant-generated story responses (in sort order),
    /// ignoring user prompts. Returns "" when there is no story yet.
    /// </summary>
    public static string BuildStoryBody(IEnumerable<DraftMessage> messages) =>
        string.Join("\n\n", messages
            .Where(m => m.Role == MessageRole.Response)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.Content));

    /// <summary>
    /// Normalizes a model's title output: takes the first non-empty line
    /// and strips a single pair of surrounding quotes.
    /// </summary>
    public static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var firstLine = raw
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? "";

        return TrimSurroundingQuotes(firstLine);
    }

    private static string TrimSurroundingQuotes(string s)
    {
        if (s.Length < 2) return s;
        char first = s[0], last = s[^1];
        bool matched =
            (first == '"' && last == '"') ||
            (first == '\'' && last == '\'') ||
            (first == '“' && last == '”'); // " … "
        return matched ? s[1..^1].Trim() : s;
    }
}
