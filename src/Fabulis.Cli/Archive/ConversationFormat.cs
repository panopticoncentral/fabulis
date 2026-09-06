using System.Text;
using System.Text.RegularExpressions;
using Fabulis.Server.Data;

namespace Fabulis.Cli.Archive;

/// <summary>
/// The "**Me:** / **StoryTeller:**" turn format shared by story versions and
/// drafts. A content line that would itself read as a delimiter is escaped
/// with a leading backslash, so a message quoting the format round-trips
/// intact — the pre-versioning format corrupted those silently.
/// </summary>
public static partial class ConversationFormat
{
    private const string PromptLabel = "**Me:**";
    private const string ResponseLabel = "**StoryTeller:**";

    public sealed record Turn(MessageRole Role, string Content, int SortOrder);

    // Group 1 captures leading backslashes: zero means a real delimiter, one
    // or more means escaped content.
    [GeneratedRegex(@"^(\\*)\*\*(Me|StoryTeller):\*\*[ \t]*$")]
    private static partial Regex DelimiterPattern();

    public static string Write(IEnumerable<Turn> turns)
    {
        var sb = new StringBuilder();
        foreach (var turn in turns.OrderBy(t => t.SortOrder))
        {
            sb.Append(turn.Role == MessageRole.Prompt ? PromptLabel : ResponseLabel).Append('\n');
            sb.Append('\n');
            foreach (var line in turn.Content.Replace("\r\n", "\n").Split('\n'))
                sb.Append(Escape(line)).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static List<Turn> Read(string body)
    {
        var turns = new List<Turn>();
        var lines = body.Replace("\r\n", "\n").Split('\n');

        MessageRole? role = null;
        var buffer = new List<string>();
        var sortOrder = 0;

        void Flush()
        {
            if (role is null) return;
            turns.Add(new Turn(role.Value, TrimBlankEdges(buffer), sortOrder++));
            buffer.Clear();
        }

        foreach (var line in lines)
        {
            var match = DelimiterPattern().Match(line);
            if (match.Success && match.Groups[1].Value.Length == 0)
            {
                Flush();
                role = match.Groups[2].Value == "Me" ? MessageRole.Prompt : MessageRole.Response;
            }
            else if (role is not null)
            {
                buffer.Add(Unescape(line));
            }
        }

        Flush();
        return turns;
    }

    private static string Escape(string line) =>
        DelimiterPattern().IsMatch(line) ? "\\" + line : line;

    private static string Unescape(string line)
    {
        var match = DelimiterPattern().Match(line);
        return match.Success && match.Groups[1].Value.Length > 0 ? line[1..] : line;
    }

    /// <summary>Drops blank leading and trailing lines, keeping internal ones.</summary>
    internal static string TrimBlankEdges(List<string> lines)
    {
        var start = 0;
        while (start < lines.Count && lines[start].Trim().Length == 0) start++;

        var end = lines.Count - 1;
        while (end >= start && lines[end].Trim().Length == 0) end--;

        return start > end ? string.Empty : string.Join('\n', lines[start..(end + 1)]);
    }
}
