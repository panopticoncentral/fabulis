using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        var draftsDir = Path.Combine(destPath, "_drafts");
        Directory.CreateDirectory(draftsDir);

        var result = new ConvertResult();
        var takenFileNames = new HashSet<string>(StringComparer.Ordinal);
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
                Console.Error.WriteLine($"warn: {file.FullName}: no conversation turns found, skipped");
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

            var modelName = DeriveModel(turns, file.FullName);
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
            var stamp = createdUtc.ToString("yyyyMMddTHHmmssZ");
            var baseFileName = $"Draft {stamp} - {title}.md";
            var fileName = MakeUniqueFileName(baseFileName, takenFileNames);

            var messages = bodyTurns.Select((t, idx) => (
                Role: t.IsUser ? MessageRole.Prompt : MessageRole.Response,
                Content: t.Message,
                SortOrder: idx));

            var content = DraftMarkdownWriter.FormatDraft(
                storytellerName, modelName, createdUtc, updatedUtc, messages);

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
                    LineNumber: i + 1,
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

    private static string DeriveModel(List<ParsedTurn> turns, string filePath)
    {
        var lastModel = turns
            .Where(t => !t.IsUser && !string.IsNullOrWhiteSpace(t.ApiModel))
            .Select(t => t.ApiModel!)
            .LastOrDefault();

        if (lastModel is null)
        {
            Console.Error.WriteLine($"warn: {filePath}: no model metadata, wrote 'Model: (unknown)'");
            return "(unknown)";
        }
        return lastModel;
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
        string truncated;
        if (collapsed.Length <= Max)
        {
            truncated = collapsed;
        }
        else
        {
            var cut = collapsed[..Max];
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > Max / 2) cut = cut[..lastSpace];
            truncated = cut + "…";
        }

        truncated = FilesystemUnsafe().Replace(truncated, "");
        truncated = TrailingPunctuation().Replace(truncated, "");
        if (string.IsNullOrWhiteSpace(truncated)) return "Untitled";
        return truncated;
    }

    private static string MakeUniqueFileName(string baseFileName, HashSet<string> taken)
    {
        if (taken.Add(baseFileName))
            return baseFileName;

        var stem = Path.GetFileNameWithoutExtension(baseFileName);
        var ext = Path.GetExtension(baseFileName);
        for (int n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (taken.Add(candidate))
                return candidate;
        }
    }

    private record ParsedTurn(
        int LineNumber,
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
