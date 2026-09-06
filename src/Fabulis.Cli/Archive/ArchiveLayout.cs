using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fabulis.Cli.Archive;

/// <summary>
/// Single source of truth for the shape of an archive directory. Identity is
/// the path: a category, story, prompt or storyteller is named by its
/// directory or file, and nothing on disk carries a row id.
/// </summary>
public static partial class ArchiveLayout
{
    public const int FormatVersion = 1;

    public const string ManifestFile = "fabulis.md";
    public const string SettingsFile = "settings.md";
    public const string StorytellersDir = "storytellers";
    public const string LibraryDir = "library";
    public const string DraftsDir = "drafts";
    public const string StoriesDir = "stories";
    public const string PromptsDir = "prompts";
    public const string OneLinersFile = "one-liners.md";
    public const string TropesFile = "tropes.md";

    public const string SystemPromptSection = "System prompt";
    public const string TitlingPromptSection = "Titling prompt";

    [GeneratedRegex(@"^Version (\d+)\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionFilePattern();

    /// <summary>
    /// Makes a user- or model-supplied string safe as a single path segment.
    /// A safety net for names already in the vault; the server rejects these
    /// characters going forward.
    /// </summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastWasDash = false;

        foreach (var ch in name)
        {
            if (ch == '/' || ch == '\\' || char.IsControl(ch))
            {
                // Suppress a dash at the very start, so a leading unsafe
                // character does not turn into "-name".
                if (sb.Length > 0 && !lastWasDash)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }
            else
            {
                sb.Append(ch);
                lastWasDash = false;
            }
        }

        // Trim trailing whitespace, dots and dashes together in one pass rather
        // than in sequence: stripping '.' before '-' would let a trailing dash
        // expose a dot that nothing re-trims, making Sanitize non-idempotent and
        // churning filenames on every re-export. char.IsWhiteSpace (not an ASCII
        // char set) so a trailing NBSP or EM SPACE cannot survive into a filename.
        var trimmed = sb.ToString().TrimStart();
        var end = trimmed.Length;
        while (end > 0 && (char.IsWhiteSpace(trimmed[end - 1]) || trimmed[end - 1] == '.' || trimmed[end - 1] == '-'))
            end--;
        var result = trimmed[..end];
        return result.Length == 0 ? "Untitled" : result;
    }

    public static string VersionFileName(int versionNumber) =>
        $"Version {versionNumber.ToString(CultureInfo.InvariantCulture)}.md";

    public static bool TryParseVersionFileName(string fileName, out int versionNumber)
    {
        versionNumber = 0;
        var match = VersionFilePattern().Match(fileName);
        return match.Success
            && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out versionNumber);
    }

    public static string DraftFileName(DateTime createdUtc, string? title)
    {
        var stamp = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc)
            .ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var safeTitle = Sanitize(string.IsNullOrWhiteSpace(title) ? "Untitled" : title);
        return $"{stamp} - {safeTitle}.md";
    }

    /// <summary>
    /// Writes <see cref="ManifestFile"/> with the standard formatVersion/exportedAt
    /// front matter, so every producer of an archive (the full vault exporter, the
    /// SillyTavern converter) shares one definition of what makes a directory a
    /// recognizable Fabulis archive. Callers supply only the body text.
    /// </summary>
    public static async Task WriteManifestAsync(string root, string body)
    {
        var text = FrontMatter.Serialize(
            [
                new("formatVersion", FormatVersion.ToString()),
                new("exportedAt", FrontMatter.FormatTimestamp(DateTime.UtcNow)),
            ],
            body);

        await File.WriteAllTextAsync(Path.Combine(root, ManifestFile), text);
    }
}

/// <summary>
/// Hands out unique names within one directory, appending " (2)", " (3)" on
/// collision — the convention SillyTavernConvertService already uses.
/// </summary>
public sealed class NameAllocator
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public string Allocate(string desired)
    {
        if (_used.Add(desired)) return desired;

        for (var n = 2; ; n++)
        {
            var candidate = $"{desired} ({n.ToString(CultureInfo.InvariantCulture)})";
            if (_used.Add(candidate)) return candidate;
        }
    }
}
