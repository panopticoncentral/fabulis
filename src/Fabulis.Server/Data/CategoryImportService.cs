using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public partial class CategoryImportService(ILogger<CategoryImportService> logger)
{
    [GeneratedRegex(@"^Version\s+(\d+)\s+\[(.+)\]\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"^\*\*(Me|Paul):?\*\*:?|\*\*(Chat|StoryTeller):?\*\*:?", RegexOptions.None)]
    private static partial Regex TurnDelimiterPattern();

    public async Task<ImportResult> ImportAsync(FabulisDbContext db, string directoryPath)
    {
        var result = new ImportResult();
        var dir = new DirectoryInfo(directoryPath);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var categoryName = dir.Name;
        var category = await db.Categories
            .Include(c => c.Stories)
                .ThenInclude(s => s.Versions)
            .FirstOrDefaultAsync(c => c.Name == categoryName);

        if (category is null)
        {
            category = new Category { Name = categoryName, CreatedAt = DateTime.UtcNow };
            db.Categories.Add(category);
            result.CategoriesCreated = 1;
        }

        foreach (var storyDir in dir.GetDirectories().OrderBy(d => d.Name))
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

            var fileNameRegex = FileNamePattern();
            foreach (var file in storyDir.GetFiles("*.md"))
            {
                var match = fileNameRegex.Match(file.Name);
                if (!match.Success)
                {
                    logger.LogWarning("Skipping file with unrecognized name: {FileName}", file.FullName);
                    continue;
                }

                var versionNumber = int.Parse(match.Groups[1].Value);
                var modelName = match.Groups[2].Value;

                if (story.Versions.Any(v => v.VersionNumber == versionNumber))
                    continue;

                var content = await File.ReadAllTextAsync(file.FullName);
                var messages = ParseConversation(content);

                if (messages.Count == 0)
                {
                    logger.LogWarning("No conversation turns found in: {FileName}", file.FullName);
                    continue;
                }

                var version = new StoryVersion
                {
                    VersionNumber = versionNumber,
                    ModelName = modelName,
                    CreatedAt = DateTime.UtcNow,
                    Story = story
                };
                version.Messages.AddRange(messages);
                story.Versions.Add(version);
                result.VersionsCreated++;
            }
        }

        await db.SaveChangesAsync();
        return result;
    }

    private static List<StoryMessage> ParseConversation(string content)
    {
        var messages = new List<StoryMessage>();
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
                    messages.Add(new StoryMessage
                    {
                        Role = currentRole.Value,
                        Content = JoinContent(currentContent),
                        SortOrder = sortOrder++
                    });
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
        }

        if (currentRole is not null && currentContent.Count > 0)
        {
            messages.Add(new StoryMessage
            {
                Role = currentRole.Value,
                Content = JoinContent(currentContent),
                SortOrder = sortOrder
            });
        }

        return messages;
    }

    private static string JoinContent(List<string> lines)
    {
        // Trim leading/trailing blank lines, preserve internal structure
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
}

public class ImportResult
{
    public int CategoriesCreated { get; set; }
    public int StoriesCreated { get; set; }
    public int VersionsCreated { get; set; }
}
