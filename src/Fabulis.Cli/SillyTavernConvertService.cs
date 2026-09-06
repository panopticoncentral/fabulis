using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fabulis.Cli.Archive;
using Fabulis.Server.Data;

namespace Fabulis.Cli;

public partial class SillyTavernConvertService
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"[/\\:*?""<>|]")]
    private static partial Regex FilesystemUnsafe();

    [GeneratedRegex(@"[\p{P}\s]+$")]
    private static partial Regex TrailingPunctuation();

    public async Task<ConvertResult> ConvertAsync(string sourcePath, string destPath)
    {
        var source = new DirectoryInfo(sourcePath);
        if (!source.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");

        if (Directory.Exists(destPath) || File.Exists(destPath))
            throw new IOException($"Destination already exists: {destPath}");

        var jsonlFiles = source.GetFiles("*.jsonl").OrderBy(f => f.Name).ToArray();
        if (jsonlFiles.Length == 0)
            throw new InvalidOperationException(
                $"No .jsonl files found in '{sourcePath}'.");

        var draftsDir = Path.Combine(destPath, ArchiveLayout.DraftsDir);
        Directory.CreateDirectory(draftsDir);

        // A drafts-only archive still needs the manifest: without it,
        // `fabulis-cli import` refuses the directory outright.
        await ArchiveLayout.WriteManifestAsync(destPath,
            $"Drafts converted from SillyTavern chat logs. Only `{ArchiveLayout.DraftsDir}/` " +
            "is populated; run `fabulis-cli import` on this directory to bring them into the vault.\n");

        var result = new ConvertResult();
        var fileNames = new NameAllocator();
        foreach (var file in jsonlFiles)
        {
            List<ParsedTurn>? turns;
            try
            {
                turns = await ParseFileAsync(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: could not read ({ex.Message})");
                result.FilesFailed++;
                continue;
            }

            if (turns is null || turns.Count == 0)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: no conversation turns found, failed");
                result.FilesFailed++;
                continue;
            }

            var storytellerName = DeriveStorytellerName(turns, file.FullName);
            if (storytellerName is null)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: no storyteller turns found, skipped");
                result.FilesSkipped++;
                continue;
            }

            var (createdUtc, updatedUtc) = DeriveTimestamps(turns, file);

            // Drop the greeting: the first non-user turn that precedes any user turn.
            var bodyTurns = turns.ToList();
            var firstUserIndex = bodyTurns.FindIndex(t => t.IsUser);
            if (firstUserIndex < 0)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: greeting-only chat, skipped");
                result.FilesSkipped++;
                continue;
            }
            var greetingIndex = -1;
            for (int i = 0; i < firstUserIndex; i++)
            {
                if (!bodyTurns[i].IsUser)
                {
                    greetingIndex = i;
                    break;
                }
            }
            if (greetingIndex >= 0)
                bodyTurns.RemoveAt(greetingIndex);

            var title = DeriveTitle(bodyTurns);

            // Named and de-duplicated exactly as the full vault exporter does
            // it: one definition of a draft's filename (and of the invariant
            // culture the timestamp is formatted in) rather than a second,
            // drifting copy here.
            var desired = ArchiveLayout.DraftFileName(createdUtc, title);
            var fileName = fileNames.Allocate(Path.GetFileNameWithoutExtension(desired)) + ".md";

            var messages = bodyTurns.Select((t, idx) => (
                Role: t.IsUser ? MessageRole.Prompt : MessageRole.Response,
                Content: t.Message,
                SortOrder: idx));

            var content = FrontMatter.Serialize(
                [
                    new("storyteller", storytellerName),
                    new("title", title),
                    new("created", FrontMatter.FormatTimestamp(createdUtc)),
                    new("updated", FrontMatter.FormatTimestamp(updatedUtc)),
                ],
                ConversationFormat.Write(messages.Select(
                    m => new ConversationFormat.Turn(m.Role, m.Content, m.SortOrder))));

            var outputPath = Path.Combine(draftsDir, fileName);
            await File.WriteAllTextAsync(outputPath, content);
            result.DraftsWritten++;
        }

        return result;
    }

    private static async Task<List<ParsedTurn>?> ParseFileAsync(FileInfo file)
    {
        // Any IOException from ReadAllLinesAsync propagates up to ConvertAsync,
        // which classifies the file as Failed.
        var turns = new List<ParsedTurn>();
        var lines = await File.ReadAllLinesAsync(file.FullName);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                Console.Error.WriteLine($"warn: {file.FullName}:{i + 1}: invalid JSON, skipped");
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                // Skip the chat-header line that starts the file.
                if (root.TryGetProperty("chat_metadata", out _)) continue;

                // Skip system turns (SillyTavern internal commands).
                if (root.TryGetProperty("is_system", out var isSystemElem) &&
                    isSystemElem.ValueKind == JsonValueKind.True)
                    continue;

                if (!root.TryGetProperty("name", out var nameElem) ||
                    !root.TryGetProperty("mes", out var mesElem))
                    continue;

                var isUser = root.TryGetProperty("is_user", out var isUserElem) &&
                             isUserElem.ValueKind == JsonValueKind.True;

                DateTime? sendDate = null;
                if (root.TryGetProperty("send_date", out var dateElem) &&
                    dateElem.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(dateElem.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var parsedDate))
                {
                    sendDate = parsedDate.Kind == DateTimeKind.Utc
                        ? parsedDate
                        : parsedDate.ToUniversalTime();
                }

                string? apiModel = null;
                if (root.TryGetProperty("extra", out var extraElem) &&
                    extraElem.ValueKind == JsonValueKind.Object &&
                    extraElem.TryGetProperty("model", out var modelElem) &&
                    modelElem.ValueKind == JsonValueKind.String)
                {
                    var m = modelElem.GetString();
                    if (!string.IsNullOrWhiteSpace(m))
                        apiModel = m;
                }

                turns.Add(new ParsedTurn(
                    Name: nameElem.GetString() ?? "",
                    IsUser: isUser,
                    Message: mesElem.GetString() ?? "",
                    SendDate: sendDate,
                    ApiModel: apiModel));
            }
        }

        return turns;
    }

    private static string? DeriveStorytellerName(List<ParsedTurn> turns, string filePath)
    {
        var storytellerNames = turns
            .Where(t => !t.IsUser && !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => t.Name)
            .ToList();

        if (storytellerNames.Count == 0)
            return null;

        var first = storytellerNames[0];
        var distinct = storytellerNames.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > 1)
        {
            Console.Error.WriteLine(
                $"warn: {filePath}: mixed storyteller names ({string.Join(", ", distinct)}), used '{first}'");
        }
        return first;
    }

    private static (DateTime CreatedUtc, DateTime UpdatedUtc) DeriveTimestamps(
        List<ParsedTurn> turns, FileInfo file)
    {
        var firstSendDate = turns.Count > 0 ? turns[0].SendDate : null;
        var lastSendDate = turns.Count > 0 ? turns[^1].SendDate : null;
        var fallback = DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc);

        DateTime created;
        if (firstSendDate is not null)
        {
            created = firstSendDate.Value;
        }
        else
        {
            Console.Error.WriteLine(
                $"warn: {file.FullName}: send_date missing on first turn, used file mtime for Created");
            created = fallback;
        }

        DateTime updated;
        if (lastSendDate is not null)
        {
            updated = lastSendDate.Value;
        }
        else
        {
            Console.Error.WriteLine(
                $"warn: {file.FullName}: send_date missing on last turn, used file mtime for Updated");
            updated = fallback;
        }

        return (created, updated);
    }

    private static string DeriveTitle(List<ParsedTurn> turnsAfterGreetingSkip)
    {
        var firstUser = turnsAfterGreetingSkip.FirstOrDefault(t => t.IsUser);
        if (firstUser is null) return "Untitled";

        var collapsed = WhitespaceRun().Replace(firstUser.Message, " ").Trim();
        if (collapsed.Length == 0) return "Untitled";

        const int Max = 60;
        bool wasTruncated = collapsed.Length > Max;
        string truncated;
        if (!wasTruncated)
        {
            truncated = collapsed;
        }
        else
        {
            var cut = collapsed[..Max];
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > Max / 2) cut = cut[..lastSpace];
            truncated = cut;
        }

        truncated = FilesystemUnsafe().Replace(truncated, "");
        truncated = TrailingPunctuation().Replace(truncated, "");
        if (string.IsNullOrWhiteSpace(truncated)) return "Untitled";
        return wasTruncated ? truncated + "…" : truncated;
    }

    private record ParsedTurn(
        string Name,
        bool IsUser,
        string Message,
        DateTime? SendDate,
        string? ApiModel);
}

public class ConvertResult
{
    public int DraftsWritten { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }
}
