using System.Globalization;
using System.Text.RegularExpressions;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli;

public partial class CategoryImportService
{
    [GeneratedRegex(@"^Version\s+(\d+)\s+\[(.+)\]\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionFileNamePattern();

    // Accepts either the timestamp-stamped form ("Draft 20260115T120000Z - title.md")
    // or the legacy id-stamped form ("Draft 1 - title.md") so old archives still import.
    [GeneratedRegex(@"^Draft\s+(\d{8}T\d{6}Z|\d+)\s+-\s+.+\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex DraftFileNamePattern();

    [GeneratedRegex(@"^\*\*(Me|Paul):?\*\*:?|\*\*(Chat|StoryTeller):?\*\*:?", RegexOptions.None)]
    private static partial Regex TurnDelimiterPattern();

    public async Task<ImportResult> ImportAsync(FabulisDbContext db, string rootPath)
    {
        var result = new ImportResult();
        var root = new DirectoryInfo(rootPath);
        if (!root.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

        foreach (var subDir in root.GetDirectories().OrderBy(d => d.Name))
        {
            if (string.Equals(subDir.Name, "_drafts", StringComparison.OrdinalIgnoreCase))
                continue;

            await ImportCategoryAsync(db, subDir, result);
        }

        var draftsDir = root.GetDirectories("_drafts").FirstOrDefault();
        if (draftsDir is not null)
            await ImportDraftsAsync(db, draftsDir, result);

        await db.SaveChangesAsync();
        return result;
    }

    private async Task ImportCategoryAsync(FabulisDbContext db, DirectoryInfo categoryDir, ImportResult result)
    {
        var categoryName = categoryDir.Name;
        var category = await db.Categories
            .Include(c => c.Stories)
                .ThenInclude(s => s.Versions)
            .FirstOrDefaultAsync(c => c.Name == categoryName);

        if (category is null)
        {
            category = new Category { Name = categoryName, CreatedAt = DateTime.UtcNow };
            db.Categories.Add(category);
            result.CategoriesCreated++;
        }

        foreach (var storyDir in categoryDir.GetDirectories().OrderBy(d => d.Name))
        {
            var story = category.Stories.FirstOrDefault(s => s.Title == storyDir.Name);
            if (story is null)
            {
                story = new Story
                {
                    Title = storyDir.Name,
                    CreatedAt = DateTime.UtcNow,
                    Category = category
                };
                category.Stories.Add(story);
                result.StoriesCreated++;
            }

            var fileNameRegex = VersionFileNamePattern();
            foreach (var file in storyDir.GetFiles("*.md"))
            {
                var match = fileNameRegex.Match(file.Name);
                if (!match.Success)
                {
                    Console.Error.WriteLine($"warn: skipping file with unrecognized name: {file.FullName}");
                    continue;
                }

                var versionNumber = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var modelName = match.Groups[2].Value;

                if (story.Versions.Any(v => v.VersionNumber == versionNumber))
                    continue;

                var content = await File.ReadAllTextAsync(file.FullName);
                var (_, messages) = ParseConversation(content);

                if (messages.Count == 0)
                {
                    Console.Error.WriteLine($"warn: no conversation turns found in: {file.FullName}");
                    continue;
                }

                var version = new StoryVersion
                {
                    VersionNumber = versionNumber,
                    ModelName = modelName,
                    CreatedAt = DateTime.UtcNow,
                    Story = story
                };
                version.Messages.AddRange(messages.Select(m => new StoryMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    SortOrder = m.SortOrder
                }));
                story.Versions.Add(version);
                result.VersionsCreated++;
            }
        }
    }

    private async Task ImportDraftsAsync(FabulisDbContext db, DirectoryInfo draftsDir, ImportResult result)
    {
        var fileNameRegex = DraftFileNamePattern();
        var storytellersByName = await db.Storytellers.ToDictionaryAsync(
            s => s.Name, s => s, StringComparer.Ordinal);

        foreach (var file in draftsDir.GetFiles("*.md").OrderBy(f => f.Name))
        {
            if (!fileNameRegex.IsMatch(file.Name))
            {
                Console.Error.WriteLine($"warn: skipping draft file with unrecognized name: {file.FullName}");
                continue;
            }

            var content = await File.ReadAllTextAsync(file.FullName);
            var (headers, parsed) = ParseConversation(content);

            if (parsed.Count == 0)
            {
                Console.Error.WriteLine($"warn: no conversation turns found in draft: {file.FullName}");
                continue;
            }

            if (!headers.TryGetValue("Storyteller", out var storytellerName) ||
                string.IsNullOrWhiteSpace(storytellerName))
            {
                Console.Error.WriteLine($"warn: draft missing 'Storyteller' header, skipping: {file.FullName}");
                continue;
            }

            if (!storytellersByName.TryGetValue(storytellerName, out var storyteller))
            {
                Console.Error.WriteLine(
                    $"warn: draft references unknown storyteller '{storytellerName}', skipping: {file.FullName}");
                continue;
            }

            var title = ExtractDraftTitleFromFileName(file.Name);
            var createdAt = ParseHeaderDate(headers, "Created") ?? DateTime.UtcNow;
            var updatedAt = ParseHeaderDate(headers, "Updated") ?? createdAt;

            // Dedupe by (StorytellerId, Title, CreatedAt) so re-importing is idempotent.
            var existing = await db.Drafts.FirstOrDefaultAsync(d =>
                d.StorytellerID == storyteller.Id &&
                d.Title == title &&
                d.CreatedAt == createdAt);

            if (existing is not null)
                continue;

            var draft = new Draft
            {
                StorytellerID = storyteller.Id,
                Title = title,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                Storyteller = storyteller
            };
            draft.Messages.AddRange(parsed.Select(m => new DraftMessage
            {
                Role = m.Role,
                Content = m.Content,
                SortOrder = m.SortOrder
            }));
            db.Drafts.Add(draft);
            result.DraftsCreated++;
        }
    }

    private static string? ExtractDraftTitleFromFileName(string fileName)
    {
        // "Draft <Id> - <Title>.md"
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var sep = stem.IndexOf(" - ", StringComparison.Ordinal);
        if (sep < 0) return null;
        var title = stem[(sep + 3)..];
        return string.Equals(title, "Untitled", StringComparison.Ordinal) ? null : title;
    }

    private static DateTime? ParseHeaderDate(Dictionary<string, string> headers, string key)
    {
        if (!headers.TryGetValue(key, out var raw)) return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.Kind == DateTimeKind.Utc
                ? parsed
                : DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc);
        }
        return null;
    }

    private static (Dictionary<string, string> Headers, List<ParsedMessage> Messages) ParseConversation(string content)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var messages = new List<ParsedMessage>();
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        var regex = TurnDelimiterPattern();

        MessageRole? currentRole = null;
        var currentContent = new List<string>();
        int sortOrder = 0;

        foreach (var line in lines)
        {
            var match = regex.Match(line);
            if (match.Success)
            {
                if (currentRole is not null && currentContent.Count > 0)
                {
                    messages.Add(new ParsedMessage(currentRole.Value, JoinContent(currentContent), sortOrder++));
                    currentContent.Clear();
                }

                currentRole = match.Groups[1].Success
                    ? MessageRole.Prompt
                    : MessageRole.Response;
            }
            else if (currentRole is not null)
            {
                currentContent.Add(line);
            }
            else
            {
                // Pre-conversation header line ("Key: value")
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    var key = line[..colon].Trim();
                    var value = line[(colon + 1)..].Trim();
                    if (key.Length > 0 && !headers.ContainsKey(key))
                        headers[key] = value;
                }
            }
        }

        if (currentRole is not null && currentContent.Count > 0)
        {
            messages.Add(new ParsedMessage(currentRole.Value, JoinContent(currentContent), sortOrder));
        }

        return (headers, messages);
    }

    private static string JoinContent(List<string> lines)
    {
        var start = 0;
        while (start < lines.Count && string.IsNullOrWhiteSpace(lines[start]))
            start++;

        var end = lines.Count - 1;
        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
            end--;

        if (start > end)
            return string.Empty;

        return string.Join('\n', lines[start..(end + 1)]);
    }

    private record ParsedMessage(MessageRole Role, string Content, int SortOrder);
}

public class ImportResult
{
    public int CategoriesCreated { get; set; }
    public int StoriesCreated { get; set; }
    public int VersionsCreated { get; set; }
    public int DraftsCreated { get; set; }
}
