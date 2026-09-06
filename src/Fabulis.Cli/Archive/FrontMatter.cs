using System.Globalization;
using System.Text;

namespace Fabulis.Cli.Archive;

/// <summary>
/// A deliberately minimal YAML front-matter subset: a "---" fenced block of
/// flat "key: value" scalar lines at the very top of a markdown file. No
/// nesting, no lists, no anchors. Hand-rolled so the CLI takes no YAML
/// dependency; forward compatibility is guarded by the manifest's
/// formatVersion rather than by validating keys here.
/// </summary>
public static class FrontMatter
{
    private const string Fence = "---";

    public static (Dictionary<string, string> Fields, string Body) Parse(string content) =>
        Parse(content, out _);

    /// <summary>
    /// As <see cref="Parse(string)"/>, but reports whether any line inside the
    /// fence had to be thrown away. A dropped line is a dropped field, and for
    /// a file whose identity lives in its front matter (a draft) that is
    /// indistinguishable from a different file — so the importer treats it the
    /// same way it treats an unclosed fence, and suppresses mirror deletions.
    /// </summary>
    public static (Dictionary<string, string> Fields, string Body) Parse(
        string content, out bool hadMalformedLines)
    {
        hadMalformedLines = false;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalized = content.Replace("\r\n", "\n");

        if (!normalized.StartsWith(Fence + "\n", StringComparison.Ordinal))
            return (fields, normalized);

        var lines = normalized.Split('\n');
        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i] == Fence) { end = i; break; }
        }

        if (end < 0)
            throw new InvalidDataException(
                "Front matter opened with '---' but no closing '---' line was found.");

        for (var i = 1; i < end; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;

            var colon = line.IndexOf(':');
            var key = colon > 0 ? line[..colon].Trim() : "";

            // A key with a space in it is not a key. That case matters
            // because the commonest hand-edit slip — deleting the colon from
            // `created: 2026-04-11T09:03:12Z` — leaves a line whose FIRST
            // colon is the one inside the timestamp, so without this check it
            // would parse silently as the field "created 2026-04-11T09",
            // dropping `created` while looking perfectly well-formed. Every
            // key this format uses is a bare identifier.
            if (colon <= 0 || key.Length == 0 || key.Any(char.IsWhiteSpace))
            {
                Console.Error.WriteLine($"warn: ignoring malformed front-matter line: {line}");
                hadMalformedLines = true;
                continue;
            }

            fields[key] = Unquote(line[(colon + 1)..].Trim());
        }

        var body = string.Join('\n', lines[(end + 1)..]);
        return (fields, body.TrimStart('\n'));
    }

    public static string Serialize(IEnumerable<KeyValuePair<string, string?>> fields, string body)
    {
        var sb = new StringBuilder();
        sb.Append(Fence).Append('\n');
        foreach (var (key, value) in fields)
        {
            if (value is null) continue;
            sb.Append(key).Append(": ").Append(Quote(value)).Append('\n');
        }
        sb.Append(Fence).Append('\n');

        if (body.Length > 0)
            sb.Append('\n').Append(body.TrimEnd('\n')).Append('\n');

        return sb.ToString();
    }

    private static string Quote(string value)
    {
        var needsQuoting =
            value.Length == 0 ||
            value != value.Trim() ||
            value.Contains(": ", StringComparison.Ordinal) ||
            value.Contains('\n') ||
            value.Contains('\\') ||
            "-#\"'[{".Contains(value[0]);

        if (!needsQuoting) return value;

        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n") + "\"";
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            return value;

        var inner = value[1..^1];
        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                i++;
                sb.Append(inner[i] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    var c => c,
                });
            }
            else
            {
                sb.Append(inner[i]);
            }
        }
        return sb.ToString();
    }

    public static string FormatTimestamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    public static DateTime? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
            return null;

        return parsed.Kind == DateTimeKind.Utc
            ? parsed
            : DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc);
    }

    public static string FormatDouble(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);
}
