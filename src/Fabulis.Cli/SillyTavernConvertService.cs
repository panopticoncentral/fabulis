using System.Globalization;
using System.Text.Json;

namespace Fabulis.Cli;

public class SillyTavernConvertService
{
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

            // Output (Tasks 5-6) lands here. For now, drop the turns on the floor.
            _ = turns;
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
