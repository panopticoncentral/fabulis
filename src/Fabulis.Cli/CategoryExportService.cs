using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli;

public class CategoryExportService
{
    public async Task<ExportResult> ExportAsync(FabulisDbContext db, string destinationPath)
    {
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            throw new IOException($"Destination already exists: {destinationPath}");

        var categories = await db.Categories
            .Include(c => c.Stories)
                .ThenInclude(s => s.Versions)
                    .ThenInclude(v => v.Messages)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var drafts = await db.Drafts
            .Include(d => d.Storyteller)
            .Include(d => d.Messages)
            .OrderBy(d => d.Id)
            .ToListAsync();

        Directory.CreateDirectory(destinationPath);

        var result = new ExportResult();

        foreach (var category in categories)
        {
            var exportableStories = category.Stories
                .Where(s => s.Versions.Any(v => v.Messages.Count > 0))
                .OrderBy(s => s.Title)
                .ToList();

            if (exportableStories.Count == 0)
                continue;

            var categoryDir = Path.Combine(destinationPath, category.Name);
            Directory.CreateDirectory(categoryDir);
            result.CategoriesExported++;

            foreach (var story in exportableStories)
            {
                var exportableVersions = story.Versions
                    .Where(v => v.Messages.Count > 0)
                    .OrderBy(v => v.VersionNumber)
                    .ToList();

                var storyDir = Path.Combine(categoryDir, story.Title);
                Directory.CreateDirectory(storyDir);
                result.StoriesExported++;

                foreach (var version in exportableVersions)
                {
                    var fileName = $"Version {version.VersionNumber} [{version.ModelName}].md";
                    var filePath = Path.Combine(storyDir, fileName);
                    var content = DraftMarkdownWriter.FormatConversation(
                        version.Messages.Select(m => (m.Role, m.Content, m.SortOrder)));
                    await File.WriteAllTextAsync(filePath, content);
                    result.VersionsExported++;
                }
            }
        }

        var exportableDrafts = drafts
            .Where(d => d.Messages.Count > 0)
            .ToList();

        if (exportableDrafts.Count > 0)
        {
            var draftsDir = Path.Combine(destinationPath, "_drafts");
            Directory.CreateDirectory(draftsDir);

            foreach (var draft in exportableDrafts)
            {
                var title = string.IsNullOrWhiteSpace(draft.Title) ? "Untitled" : draft.Title;
                var stamp = DateTime.SpecifyKind(draft.CreatedAt, DateTimeKind.Utc)
                    .ToString("yyyyMMddTHHmmssZ");
                var fileName = $"Draft {stamp} - {title}.md";
                var filePath = Path.Combine(draftsDir, fileName);
                var storytellerName = draft.Storyteller?.Name ?? "(unknown)";
                var modelName = draft.Storyteller?.ModelName ?? "(unknown)";
                var createdUtc = DateTime.SpecifyKind(draft.CreatedAt, DateTimeKind.Utc);
                var updatedUtc = DateTime.SpecifyKind(draft.UpdatedAt, DateTimeKind.Utc);
                var content = DraftMarkdownWriter.FormatDraft(
                    storytellerName, modelName, createdUtc, updatedUtc,
                    draft.Messages.Select(m => (m.Role, m.Content, m.SortOrder)));
                await File.WriteAllTextAsync(filePath, content);
                result.DraftsExported++;
            }
        }

        return result;
    }

}

public class ExportResult
{
    public int CategoriesExported { get; set; }
    public int StoriesExported { get; set; }
    public int VersionsExported { get; set; }
    public int DraftsExported { get; set; }
}
