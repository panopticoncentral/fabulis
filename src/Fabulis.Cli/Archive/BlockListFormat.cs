using System.Text;
using System.Text.RegularExpressions;

namespace Fabulis.Cli.Archive;

/// <summary>
/// Ordered content blocks separated by a line that is exactly "---". Used for
/// PromptMessages, which carry no role — only content and sort order. Escaping
/// mirrors <see cref="ConversationFormat"/>.
/// </summary>
public static partial class BlockListFormat
{
    private const string Separator = "---";

    [GeneratedRegex(@"^(\\*)---[ \t]*$")]
    private static partial Regex SeparatorPattern();

    public static string Write(IEnumerable<string> blocks)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var block in blocks)
        {
            if (!first)
                sb.Append('\n').Append(Separator).Append('\n').Append('\n');
            first = false;

            foreach (var line in block.Replace("\r\n", "\n").Split('\n'))
                sb.Append(Escape(line)).Append('\n');
        }
        return sb.ToString();
    }

    public static List<string> Read(string body)
    {
        var blocks = new List<string>();
        var buffer = new List<string>();
        var sawContent = false;

        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var match = SeparatorPattern().Match(line);
            if (match.Success && match.Groups[1].Value.Length == 0)
            {
                blocks.Add(ConversationFormat.TrimBlankEdges(buffer));
                buffer.Clear();
                sawContent = true;
            }
            else
            {
                if (line.Trim().Length > 0) sawContent = true;
                buffer.Add(Unescape(line));
            }
        }

        if (!sawContent) return [];

        blocks.Add(ConversationFormat.TrimBlankEdges(buffer));
        return blocks;
    }

    private static string Escape(string line) =>
        SeparatorPattern().IsMatch(line) ? "\\" + line : line;

    private static string Unescape(string line)
    {
        var match = SeparatorPattern().Match(line);
        return match.Success && match.Groups[1].Value.Length > 0 ? line[1..] : line;
    }
}
