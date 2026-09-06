using System.Text;
using System.Text.RegularExpressions;

namespace Fabulis.Cli.Archive;

/// <summary>
/// "## Heading" delimited body sections, for multi-line text that does not
/// belong in front matter: a storyteller's system and titling prompts, and any
/// AppSetting whose value contains a newline. A content line that would itself
/// read as a heading is escaped with a leading backslash, so an LLM prompt
/// containing markdown H2s round-trips intact. Escaping mirrors
/// <see cref="ConversationFormat"/> and <see cref="BlockListFormat"/>.
/// </summary>
public static partial class BodySections
{
    private const string HeadingPrefix = "## ";

    // Group 1 captures leading backslashes: zero means a real heading, one or
    // more means escaped content. Group 2 captures the heading text.
    [GeneratedRegex(@"^(\\*)## (.*)$")]
    private static partial Regex HeadingPattern();

    public static string Write(IEnumerable<KeyValuePair<string, string>> sections)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var (heading, text) in sections)
        {
            if (!first) sb.Append('\n');
            first = false;
            sb.Append(HeadingPrefix).Append(heading.Trim()).Append('\n').Append('\n');
            foreach (var line in text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
                sb.Append(Escape(line)).Append('\n');
        }
        return sb.ToString();
    }

    public static Dictionary<string, string> Read(string body)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? heading = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (heading is null) return;
            if (sections.ContainsKey(heading))
                Console.Error.WriteLine($"warn: duplicate section heading, keeping last occurrence: {heading}");
            sections[heading] = ConversationFormat.TrimBlankEdges(buffer);
            buffer.Clear();
        }

        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var match = HeadingPattern().Match(line);
            if (match.Success && match.Groups[1].Value.Length == 0)
            {
                Flush();
                heading = match.Groups[2].Value.Trim();
            }
            else if (heading is not null)
            {
                buffer.Add(Unescape(line));
            }
        }

        Flush();
        return sections;
    }

    private static string Escape(string line) =>
        HeadingPattern().IsMatch(line) ? "\\" + line : line;

    private static string Unescape(string line)
    {
        var match = HeadingPattern().Match(line);
        return match.Success && match.Groups[1].Value.Length > 0 ? line[1..] : line;
    }
}
