using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli;

/// <summary>
/// Writes the whole vault to a directory tree. Traversal only — every
/// question about file shape lives under <c>Archive/</c>.
/// </summary>
public sealed class VaultExporter
{
    private const string ApiKeySetting = "OpenRouterApiKey";

    public async Task<ExportResult> ExportAsync(
        FabulisDbContext db, string destinationPath, bool includeSecrets = false)
    {
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            throw new IOException($"Destination already exists: {destinationPath}");

        var result = new ExportResult();
        Directory.CreateDirectory(destinationPath);

        await WriteManifestAsync(destinationPath);
        await WriteSettingsAsync(db, destinationPath, includeSecrets, result);
        var storytellerStems = await WriteStorytellersAsync(db, destinationPath, result);
        await WriteLibraryAsync(db, destinationPath, result);
        await WriteDraftsAsync(db, destinationPath, result, storytellerStems);

        return result;
    }

    private static async Task WriteManifestAsync(string root)
    {
        var body =
            "A Fabulis vault archive.\n\n" +
            $"- `{ArchiveLayout.LibraryDir}/` one directory per category, each holding " +
            $"`{ArchiveLayout.StoriesDir}/`, `{ArchiveLayout.PromptsDir}/`, " +
            $"`{ArchiveLayout.OneLinersFile}` and `{ArchiveLayout.TropesFile}`\n" +
            $"- `{ArchiveLayout.DraftsDir}/` unsaved drafts\n" +
            $"- `{ArchiveLayout.StorytellersDir}/` storyteller prompts and tuning\n" +
            $"- `{ArchiveLayout.SettingsFile}` app settings\n";

        await ArchiveLayout.WriteManifestAsync(root, body);
    }

    private static async Task WriteSettingsAsync(
        FabulisDbContext db, string root, bool includeSecrets, ExportResult result)
    {
        var settings = await db.AppSettings.OrderBy(s => s.Key).ToListAsync();

        var scalars = new List<KeyValuePair<string, string?>>();
        var sections = new List<KeyValuePair<string, string>>();

        foreach (var setting in settings)
        {
            if (setting.Key == ApiKeySetting && !includeSecrets)
                continue;

            // The mapping is generic rather than a hardcoded field list, so a
            // new AppSetting is covered without touching this code. Multi-line
            // values (SummaryPrompt today) go to a body section.
            if (setting.Value.Contains('\n'))
                sections.Add(new(setting.Key, setting.Value));
            else
                scalars.Add(new(setting.Key, setting.Value));
        }

        var text = FrontMatter.Serialize(scalars, BodySections.Write(sections));
        await File.WriteAllTextAsync(Path.Combine(root, ArchiveLayout.SettingsFile), text);
        result.SettingsWritten = true;
    }

    /// <summary>
    /// Writes each storyteller file and returns the file stem the
    /// allocator actually used for each, keyed by Storyteller.Id — the
    /// only record of the sanitized/deduplicated name, needed so a draft's
    /// <c>storyteller</c> reference can point at the same stem rather than
    /// risking disagreement with a sanitized or suffixed filename.
    /// </summary>
    private static async Task<Dictionary<int, string>> WriteStorytellersAsync(
        FabulisDbContext db, string root, ExportResult result)
    {
        var dir = Path.Combine(root, ArchiveLayout.StorytellersDir);
        Directory.CreateDirectory(dir);

        var stems = new Dictionary<int, string>();
        var names = new NameAllocator();
        foreach (var s in await db.Storytellers.OrderBy(s => s.Id).ToListAsync())
        {
            var text = FrontMatter.Serialize(
                [
                    new("model", s.ModelName),
                    new("temperature", FrontMatter.FormatDouble(s.Temperature)),
                    new("topP", s.TopP is null ? null : FrontMatter.FormatDouble(s.TopP.Value)),
                    new("maxTokens", s.MaxTokens?.ToString()),
                    new("minP", s.MinP is null ? null : FrontMatter.FormatDouble(s.MinP.Value)),
                    new("topK", s.TopK?.ToString()),
                    new("topA", s.TopA is null ? null : FrontMatter.FormatDouble(s.TopA.Value)),
                    new("reasoningEffort", s.ReasoningEffort?.ToString()),
                    new("created", FrontMatter.FormatTimestamp(s.CreatedAt)),
                ],
                BodySections.Write(
                [
                    new(ArchiveLayout.SystemPromptSection, s.Prompt),
                    new(ArchiveLayout.TitlingPromptSection, s.TitlingPrompt),
                ]));

            var stem = names.Allocate(SafeName(s.Name));
            await File.WriteAllTextAsync(Path.Combine(dir, stem + ".md"), text);
            stems[s.Id] = stem;
            result.Storytellers++;
        }

        return stems;
    }

    private static async Task WriteLibraryAsync(
        FabulisDbContext db, string root, ExportResult result)
    {
        var libraryDir = Path.Combine(root, ArchiveLayout.LibraryDir);
        Directory.CreateDirectory(libraryDir);

        var categories = await db.Categories
            .Include(c => c.Stories).ThenInclude(s => s.Versions).ThenInclude(v => v.Messages)
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .Include(c => c.OneLiners)
            .Include(c => c.Tropes)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var categoryNames = new NameAllocator();

        foreach (var category in categories)
        {
            var categoryDir = Path.Combine(libraryDir, categoryNames.Allocate(SafeName(category.Name)));
            Directory.CreateDirectory(categoryDir);
            result.Categories++;

            await WriteStoriesAsync(categoryDir, category, result);
            await WritePromptsAsync(categoryDir, category, result);

            // Written even when empty, so an empty list and a missing file
            // stay distinguishable on import.
            await File.WriteAllTextAsync(
                Path.Combine(categoryDir, ArchiveLayout.OneLinersFile),
                ItemListFormat.Write(category.OneLiners.OrderBy(o => o.Id).Select(o => o.Text)));
            result.OneLiners += category.OneLiners.Count;

            await File.WriteAllTextAsync(
                Path.Combine(categoryDir, ArchiveLayout.TropesFile),
                ItemListFormat.Write(category.Tropes.OrderBy(t => t.Id).Select(t => t.Text)));
            result.Tropes += category.Tropes.Count;
        }
    }

    private static async Task WriteStoriesAsync(
        string categoryDir, Category category, ExportResult result)
    {
        var storiesDir = Path.Combine(categoryDir, ArchiveLayout.StoriesDir);
        Directory.CreateDirectory(storiesDir);

        var storyNames = new NameAllocator();
        foreach (var story in category.Stories.OrderBy(s => s.Title, StringComparer.Ordinal))
        {
            var storyDir = Path.Combine(storiesDir, storyNames.Allocate(SafeName(story.Title)));
            Directory.CreateDirectory(storyDir);
            result.Stories++;

            // Nothing enforces one row per VersionNumber within a story (a
            // concurrent double-save can produce two), and both would be
            // written to the same "Version N.md" — the later one silently
            // winning while ExportResult.Versions still counts both. A
            // NameAllocator " (2)" suffix is not the answer: that filename
            // does not parse back as a version file, so it would turn a
            // silent overwrite into a file the importer rejects. Warn instead,
            // so the loss is at least visible in the run's output.
            var writtenVersionNumbers = new HashSet<int>();

            foreach (var version in story.Versions.OrderBy(v => v.VersionNumber))
            {
                if (!writtenVersionNumbers.Add(version.VersionNumber))
                {
                    Console.Error.WriteLine(
                        $"warn: duplicate version number {version.VersionNumber} in story " +
                        $"'{story.Title}': " +
                        $"'{ArchiveLayout.VersionFileName(version.VersionNumber)}' holds only " +
                        "the last of them");
                }

                var text = FrontMatter.Serialize(
                    [
                        new("origin", version.Origin.ToString()),
                        new("model", version.ModelName),
                        new("created", FrontMatter.FormatTimestamp(version.CreatedAt)),
                    ],
                    ConversationFormat.Write(version.Messages.Select(
                        m => new ConversationFormat.Turn(m.Role, m.Content, m.SortOrder))));

                await File.WriteAllTextAsync(
                    Path.Combine(storyDir, ArchiveLayout.VersionFileName(version.VersionNumber)),
                    text);
                result.Versions++;
            }
        }
    }

    private static async Task WritePromptsAsync(
        string categoryDir, Category category, ExportResult result)
    {
        var promptsDir = Path.Combine(categoryDir, ArchiveLayout.PromptsDir);
        Directory.CreateDirectory(promptsDir);

        var promptNames = new NameAllocator();
        foreach (var prompt in category.Prompts.OrderBy(p => p.Title, StringComparer.Ordinal))
        {
            var text = FrontMatter.Serialize(
                [
                    new("created", FrontMatter.FormatTimestamp(prompt.CreatedAt)),
                    new("updated", FrontMatter.FormatTimestamp(prompt.UpdatedAt)),
                ],
                BlockListFormat.Write(
                    prompt.Messages.OrderBy(m => m.SortOrder).Select(m => m.Content)));

            var stem = promptNames.Allocate(SafeName(prompt.Title));
            await File.WriteAllTextAsync(Path.Combine(promptsDir, stem + ".md"), text);
            result.Prompts++;
        }
    }

    private static async Task WriteDraftsAsync(
        FabulisDbContext db, string root, ExportResult result, Dictionary<int, string> storytellerStems)
    {
        var draftsDir = Path.Combine(root, ArchiveLayout.DraftsDir);
        Directory.CreateDirectory(draftsDir);

        var drafts = await db.Drafts
            .Include(d => d.Storyteller)
            .Include(d => d.Messages)
            .OrderBy(d => d.Id)
            .ToListAsync();

        var fileNames = new NameAllocator();
        foreach (var draft in drafts)
        {
            // The allocated storyteller file stem, not the raw name: the
            // stem is what the storyteller file is actually named (after
            // sanitizing and de-duplicating), and it is the only thing an
            // importer can match a draft's reference against.
            var storytellerRef = storytellerStems.TryGetValue(draft.StorytellerID, out var storytellerStem)
                ? storytellerStem
                : draft.Storyteller?.Name ?? "(unknown)";

            var text = FrontMatter.Serialize(
                [
                    new("storyteller", storytellerRef),
                    new("title", draft.Title),
                    new("created", FrontMatter.FormatTimestamp(draft.CreatedAt)),
                    new("updated", FrontMatter.FormatTimestamp(draft.UpdatedAt)),
                ],
                ConversationFormat.Write(draft.Messages.Select(
                    m => new ConversationFormat.Turn(m.Role, m.Content, m.SortOrder))));

            var desired = ArchiveLayout.DraftFileName(draft.CreatedAt, draft.Title);
            var stem = fileNames.Allocate(Path.GetFileNameWithoutExtension(desired));
            await File.WriteAllTextAsync(Path.Combine(draftsDir, stem + ".md"), text);
            result.Drafts++;
        }
    }

    /// <summary>Sanitizes a path segment, warning when the name had to change.</summary>
    private static string SafeName(string name)
    {
        var safe = ArchiveLayout.Sanitize(name);
        if (!string.Equals(safe, name, StringComparison.Ordinal))
            Console.Error.WriteLine($"warn: wrote '{name}' to disk as '{safe}'");
        return safe;
    }
}

public class ExportResult
{
    public int Categories { get; set; }
    public int Stories { get; set; }
    public int Versions { get; set; }
    public int Drafts { get; set; }
    public int Prompts { get; set; }
    public int OneLiners { get; set; }
    public int Tropes { get; set; }
    public int Storytellers { get; set; }
    public bool SettingsWritten { get; set; }
}
