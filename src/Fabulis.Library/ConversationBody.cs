using System.Text;
using System.Text.Json;

namespace Fabulis.Library;

public enum Speaker { Prompt, Response }

public sealed record ConversationMessage(Guid Id, Speaker Role, string Content, int SortOrder);

/// <summary>
/// Lossless conversation bodies for the proposed live-library Markdown format.
/// Document headers/version validation belong to the document reader, not this codec.
/// This deliberately does not share the legacy archive's whitespace normalization.
/// </summary>
public static class ConversationBody
{
    private const string Start = "<!-- fabulis:message ";
    private const string CommentEnd = " -->";
    private const string End = "\n\n<!-- fabulis:end -->\n";
    private const string Prompt = "**Me:**";
    private const string Response = "**StoryTeller:**";

    public static string Write(IEnumerable<ConversationMessage> messages)
    {
        var result = new StringBuilder();
        var ids = new HashSet<Guid>();
        foreach (var message in messages)
        {
            if (message.Id == Guid.Empty || !ids.Add(message.Id))
                throw new InvalidDataException("Message IDs must be nonempty and unique.");
            var label = message.Role switch
            {
                Speaker.Prompt => Prompt,
                Speaker.Response => Response,
                _ => throw new InvalidDataException("Unknown message role."),
            };
            if (result.Length > 0) result.Append('\n');
            result.Append(Start)
                .Append(JsonSerializer.Serialize(new { id = message.Id.ToString("D"), sortOrder = message.SortOrder }))
                .Append(CommentEnd).Append('\n').Append(label).Append("\n\n")
                .Append(TransformContent(message.Content, encode: true)).Append(End);
        }
        return result.ToString();
    }

    public static IReadOnlyList<ConversationMessage> Read(string body)
    {
        var messages = new List<ConversationMessage>();
        var ids = new HashSet<Guid>();
        var offset = 0;
        while (offset < body.Length)
        {
            var metadataLine = ReadLine(body, ref offset);
            if (!metadataLine.StartsWith(Start, StringComparison.Ordinal) ||
                !metadataLine.EndsWith(CommentEnd, StringComparison.Ordinal))
                throw new InvalidDataException("Expected a Fabulis message metadata comment.");
            var metadata = metadataLine[Start.Length..^CommentEnd.Length];
            if (metadata.Contains("--", StringComparison.Ordinal))
                throw new InvalidDataException("Metadata must not contain raw double hyphens.");
            var (id, sortOrder) = ReadMetadata(metadata);
            if (!ids.Add(id)) throw new InvalidDataException("Duplicate message ID.");

            var role = ReadLine(body, ref offset) switch
            {
                Prompt => Speaker.Prompt,
                Response => Speaker.Response,
                _ => throw new InvalidDataException("Expected an exact speaker label."),
            };
            Expect(body, ref offset, "\n");
            var end = body.IndexOf(End, offset, StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("Message is missing its closing framing.");
            var content = TransformContent(body[offset..end], encode: false);
            messages.Add(new(id, role, content, sortOrder));
            offset = end + End.Length;
            if (offset < body.Length)
            {
                Expect(body, ref offset, "\n");
                if (offset == body.Length)
                    throw new InvalidDataException("Unexpected trailing separator.");
            }
        }
        return messages;
    }

    private static (Guid Id, int SortOrder) ReadMetadata(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Message metadata must be an object.");
            Guid? id = null;
            int? order = null;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate metadata key.");
                switch (property.Name)
                {
                    case "id":
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            !Guid.TryParseExact(property.Value.GetString(), "D", out var parsed) || parsed == Guid.Empty)
                            throw new InvalidDataException("Invalid message ID.");
                        id = parsed;
                        break;
                    case "sortOrder":
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var parsedOrder))
                            throw new InvalidDataException("Sort order must be a 32-bit integer.");
                        order = parsedOrder;
                        break;
                    default:
                        // Until extension preservation exists, fail closed rather than drop metadata on save.
                        throw new InvalidDataException("Unsupported message metadata key.");
                }
            }
            if (id is null || order is null) throw new InvalidDataException("Missing message metadata.");
            return (id.Value, order.Value);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed message metadata JSON.", ex);
        }
    }

    private static string TransformContent(string content, bool encode)
    {
        // Split only on LF and rejoin verbatim. CR bytes (including a final bare CR)
        // are content; they must never be normalized or mistaken for framing.
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var slashCount = 0;
            while (slashCount < line.Length && line[slashCount] == '\\') slashCount++;
            var candidate = line[slashCount..];
            if (candidate.EndsWith('\r')) candidate = candidate[..^1];
            var reserved = candidate is Prompt or Response ||
                candidate.StartsWith("<!-- fabulis:", StringComparison.Ordinal);
            if (!reserved) continue;
            if (!encode && slashCount == 0)
                throw new InvalidDataException("Unescaped structural line in message content.");
            lines[i] = encode ? "\\" + line : line[1..];
        }
        return string.Join('\n', lines);
    }

    private static string ReadLine(string text, ref int offset)
    {
        var end = text.IndexOf('\n', offset);
        if (end < 0) throw new InvalidDataException("Incomplete structural line.");
        var line = text[offset..end];
        offset = end + 1;
        return line;
    }

    private static void Expect(string text, ref int offset, string expected)
    {
        if (!text.AsSpan(offset).StartsWith(expected, StringComparison.Ordinal))
            throw new InvalidDataException("Invalid message framing.");
        offset += expected.Length;
    }
}
