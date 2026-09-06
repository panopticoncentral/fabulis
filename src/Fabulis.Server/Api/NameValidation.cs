namespace Fabulis.Server.Api;

/// <summary>
/// Guards the four values that become path segments in a CLI archive
/// (category name, story title, prompt title, storyteller name). The archive
/// identifies rows by path, so a separator or control character in one of
/// these would make export lossy and import ambiguous.
/// </summary>
public static class NameValidation
{
    public static bool IsValidPathSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        foreach (var ch in name)
        {
            if (ch == '/' || ch == '\\' || char.IsControl(ch)) return false;
        }

        if (name is "." or "..") return false;

        // Mirror ArchiveLayout.Sanitize's (Fabulis.Cli) trim step exactly: it
        // does TrimStart() (Unicode-whitespace-aware) and then trims trailing
        // characters where char.IsWhiteSpace(c) || c == '.' || c == '-'. A
        // name this method accepts must be exactly what Sanitize writes to
        // disk on export, or the database row and its on-disk directory/file
        // name desync silently — the mirror-import mismatch this validator
        // exists to prevent. If Sanitize's trimming rule ever changes, this
        // must change with it: Fabulis.Server cannot reference Fabulis.Cli to
        // share the logic directly, so the two are pinned together only by
        // NameValidationAgreementTests in tests/Fabulis.Cli.Tests.
        var trimStart = name.TrimStart();
        if (trimStart.Length != name.Length) return false;

        var end = trimStart.Length;
        while (end > 0 && (char.IsWhiteSpace(trimStart[end - 1]) || trimStart[end - 1] == '.' || trimStart[end - 1] == '-'))
            end--;

        return end == trimStart.Length;
    }

    /// <summary>
    /// The 400 body for a rejected name. It has to describe the whole rule
    /// above, not just the separator half: a user naming a category "Sci-Fi -"
    /// or a story "The End." is rejected by the trailing-character check, and
    /// a message that only mentions slashes leaves them with no idea what to
    /// change. Callers validate the TRIMMED value, so surrounding whitespace
    /// alone is forgiven rather than reported.
    /// </summary>
    public static string Error(string field) =>
        $"{field} must not contain '/', '\\' or control characters, start with " +
        "whitespace, or end with whitespace, '.' or '-'";
}
