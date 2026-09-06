using System.Text;

namespace Fabulis.Cli.Archive;

/// <summary>
/// A markdown bullet list, one bullet per item, continuation lines indented
/// two spaces. The indent is what lets an item whose own text starts with
/// "- " round-trip without needing an escape rule.
/// </summary>
public static class ItemListFormat
{
    private const string Bullet = "- ";
    private const string Indent = "  ";

    public static string Write(IEnumerable<string> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            var lines = item.Replace("\r\n", "\n").Split('\n');
            sb.Append(Bullet).Append(lines[0]).Append('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) sb.Append('\n');
                else sb.Append(Indent).Append(lines[i]).Append('\n');
            }
        }
        return sb.ToString();
    }

    public static List<string> Read(string content)
    {
        var items = new List<string>();
        var buffer = new List<string>();
        var started = false;

        void Flush()
        {
            if (!started) return;
            items.Add(ConversationFormat.TrimBlankEdges(buffer));
            buffer.Clear();
        }

        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith(Bullet, StringComparison.Ordinal))
            {
                Flush();
                started = true;
                buffer.Add(line[Bullet.Length..]);
            }
            else if (started)
            {
                buffer.Add(line.StartsWith(Indent, StringComparison.Ordinal)
                    ? line[Indent.Length..]
                    : line);
            }
        }

        Flush();
        return items;
    }
}
