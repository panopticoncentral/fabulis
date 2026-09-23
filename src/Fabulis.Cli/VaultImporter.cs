using System.Globalization;
using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli;

public sealed record ImportOptions(bool Mirror = false, Func<MirrorPlan, bool>? ConfirmDeletions = null);

/// <summary>
/// What a mirror import would remove. Cascades are itemized rather than
/// implied: deleting a category also lists the stories, versions and prompts
/// that go with it, so the set the user confirms is the set that is actually
/// destroyed. <see cref="Lists"/> covers the one-liner and trope lists, which
/// are replaced wholesale per category rather than row by row — an entry
/// appears only when the file on disk does not keep every item the vault
/// already had.
/// </summary>
public sealed record MirrorPlan(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Stories,
    IReadOnlyList<string> Versions,
    IReadOnlyList<string> Prompts,
    IReadOnlyList<string> Drafts,
    IReadOnlyList<string> Storytellers,
    IReadOnlyList<string> Lists)
{
    public static readonly MirrorPlan Empty = new([], [], [], [], [], [], []);

    public int Count =>
        Categories.Count + Stories.Count + Versions.Count +
        Prompts.Count + Drafts.Count + Storytellers.Count + Lists.Count;

    public bool IsEmpty => Count == 0;
}

/// <summary>
/// Reads a directory tree back into the vault. Traversal only; file shapes
/// live under <c>Archive/</c>. Identity is the path, so a renamed directory
/// reads as a different category, story or prompt.
/// </summary>
public sealed class VaultImporter
{
    private const string ApiKeySetting = "OpenRouterApiKey";

    public async Task<ImportResult> ImportAsync(
        FabulisDbContext db, string rootPath, ImportOptions? options = null)
    {
        options ??= new ImportOptions();

        var root = new DirectoryInfo(rootPath);
        if (!root.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

        await VerifyManifestAsync(root);

        var result = new ImportResult();
        var seen = new SeenPaths();

        // Any unexpected exception rolls this back instead of leaving the
        // vault half-imported; per-item problems are caught below and
        // reported as warnings instead of propagating here. The mirror
        // deletion pass runs inside this same transaction, so a refused
        // storyteller deletion (or a cancelled confirmation) rolls back
        // everything pass one did too, not just the deletions.
        await using var transaction = await db.Database.BeginTransactionAsync();

        await ImportSettingsAsync(db, root, result, options.Mirror, seen);
        await ImportStorytellersAsync(db, root, result, options.Mirror, seen);
        await ImportLibraryAsync(db, root, result, options.Mirror, seen);
        await ImportDraftsAsync(db, root, result, options.Mirror, seen);

        if (options.Mirror)
        {
            // Flush pass one's creates before computing the plan, so a row
            // created earlier in this very run (e.g. a new draft) already
            // has a real id and shows up in the queries below. Without this,
            // a same-run draft referencing a storyteller whose file was
            // removed would be invisible to the guard that protects that
            // storyteller from deletion.
            await db.SaveChangesAsync();

            // A file we could not read looks exactly like a file that is not
            // there, and "not there" means "delete the row". Mirroring against
            // an archive that was only partly readable is precisely when
            // deletion must not proceed, so any per-file skip in pass one
            // suppresses the whole deletion pass. List replacement already
            // happened above and is still reported (and still confirmable),
            // because that is a destructive edit the user has to agree to.
            var deletions = seen.Incomplete
                ? SuppressedDeletions(seen)
                : await BuildMirrorDeletionsAsync(db, seen);

            result.Deleted = deletions.Plan;

            if (!deletions.Plan.IsEmpty &&
                options.ConfirmDeletions is not null &&
                !options.ConfirmDeletions(deletions.Plan))
            {
                result.Cancelled = true;
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                return result;
            }

            ApplyMirrorDeletions(db, deletions);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }

    /// <summary>
    /// Reads the manifest first. An unrecognized formatVersion is a hard error
    /// rather than a best-effort parse — that is what makes dropping legacy
    /// support safe going forward.
    /// </summary>
    private static async Task VerifyManifestAsync(DirectoryInfo root)
    {
        var manifestPath = Path.Combine(root.FullName, ArchiveLayout.ManifestFile);
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException(
                $"'{root.FullName}' is not a Fabulis archive: no {ArchiveLayout.ManifestFile} found.");

        var (fields, _) = FrontMatter.Parse(await File.ReadAllTextAsync(manifestPath));

        if (!fields.TryGetValue("formatVersion", out var raw) ||
            !int.TryParse(raw, CultureInfo.InvariantCulture, out var version))
            throw new InvalidOperationException(
                $"{ArchiveLayout.ManifestFile} has no readable formatVersion.");

        if (version != ArchiveLayout.FormatVersion)
            throw new InvalidOperationException(
                $"Archive formatVersion {version} is not supported by this build " +
                $"(expected {ArchiveLayout.FormatVersion}).");
    }

    private static async Task ImportSettingsAsync(
        FabulisDbContext db, DirectoryInfo root, ImportResult result, bool mirror, SeenPaths seen)
    {
        var path = Path.Combine(root.FullName, ArchiveLayout.SettingsFile);
        if (!File.Exists(path)) return;

        Dictionary<string, string> fields;
        string body;
        bool malformedLines;
        try
        {
            (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path), out malformedLines);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"warn: skipping malformed file: {path} ({ex.Message})");
            seen.Incomplete = true;
            return;
        }
        if (malformedLines) seen.Incomplete = true;

        var values = new Dictionary<string, string>(fields, StringComparer.Ordinal);
        foreach (var (key, text) in BodySections.Read(body))
            values[key] = text;

        foreach (var (key, value) in values)
        {
            // A redacted archive must never clear a working key.
            if (key == ApiKeySetting && string.IsNullOrEmpty(value)) continue;

            var existing = await db.AppSettings.FindAsync(key);
            if (existing is not null)
            {
                // Merge mode creates what is missing; it never overwrites a
                // setting that already exists in the target vault, since a
                // stale backup must not clobber live server config. Mirror
                // mode is where updating settings belongs.
                if (mirror && existing.Value != value)
                {
                    existing.Value = value;
                    result.SettingsApplied++;
                }
                continue;
            }

            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
            result.SettingsApplied++;
        }

        // Settings are never deleted here, mirror or not: an absent key in
        // the archive means "unspecified", not "removed".
    }

    private static async Task ImportStorytellersAsync(
        FabulisDbContext db, DirectoryInfo root, ImportResult result, bool mirror, SeenPaths seen)
    {
        var dir = new DirectoryInfo(Path.Combine(root.FullName, ArchiveLayout.StorytellersDir));
        if (!dir.Exists) return;

        var storytellers = await db.Storytellers.ToListAsync();

        foreach (var file in dir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file.Name);
            seen.Storytellers.Add(name);

            Dictionary<string, string> fields;
            string body;
            bool malformedLines;
            try
            {
                (fields, body) = FrontMatter.Parse(
                    await File.ReadAllTextAsync(file.FullName), out malformedLines);
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"warn: skipping malformed file: {file.FullName} ({ex.Message})");
                seen.Incomplete = true;
                continue;
            }
            if (malformedLines) seen.Incomplete = true;
            var sections = BodySections.Read(body);

            var existing = MatchOnDisk(storytellers, s => s.Name, name);
            if (existing is not null)
            {
                if (mirror) UpdateStoryteller(existing, fields, sections);
                continue;
            }

            var storyteller = new Storyteller
            {
                Name = name,
                ModelName = Scalar(fields, "model") ?? "",
                Prompt = sections.GetValueOrDefault(ArchiveLayout.SystemPromptSection, ""),
                TitlingPrompt = sections.GetValueOrDefault(
                    ArchiveLayout.TitlingPromptSection, Storyteller.DefaultTitlingPrompt),
                Temperature = Double(fields, "temperature") ?? 0.7,
                TopP = Double(fields, "topP"),
                MaxTokens = Int(fields, "maxTokens"),
                MinP = Double(fields, "minP"),
                TopK = Int(fields, "topK"),
                TopA = Double(fields, "topA"),
                ReasoningEffort = Enum.TryParse<ReasoningEffort>(
                    Scalar(fields, "reasoningEffort"), out var effort) ? effort : null,
                CreatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? DateTime.UtcNow,
            };

            db.Storytellers.Add(storyteller);
            storytellers.Add(storyteller);
            result.StorytellersCreated++;
        }

        await db.SaveChangesAsync();
    }

    private static void UpdateStoryteller(
        Storyteller s, Dictionary<string, string> fields, Dictionary<string, string> sections)
    {
        s.ModelName = Scalar(fields, "model") ?? s.ModelName;
        s.Prompt = sections.GetValueOrDefault(ArchiveLayout.SystemPromptSection, s.Prompt);
        s.TitlingPrompt = sections.GetValueOrDefault(ArchiveLayout.TitlingPromptSection, s.TitlingPrompt);
        s.Temperature = Double(fields, "temperature") ?? s.Temperature;
        s.TopP = Double(fields, "topP");
        s.MaxTokens = Int(fields, "maxTokens");
        s.MinP = Double(fields, "minP");
        s.TopK = Int(fields, "topK");
        s.TopA = Double(fields, "topA");
        s.ReasoningEffort = Enum.TryParse<ReasoningEffort>(
            Scalar(fields, "reasoningEffort"), out var effort) ? effort : null;
        s.CreatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? s.CreatedAt;
    }

    private static async Task ImportLibraryAsync(
        FabulisDbContext db, DirectoryInfo root, ImportResult result, bool mirror, SeenPaths seen)
    {
        var libraryDir = new DirectoryInfo(Path.Combine(root.FullName, ArchiveLayout.LibraryDir));
        if (!libraryDir.Exists) return;

        // Loaded once, whole, up front, for two reasons. First, a directory
        // name has to be matched against the SANITIZED row name (see
        // MatchOnDisk), which SQL cannot express. Second, and more sharply:
        // the Update* helpers below Clear() the message collections, and
        // Clear() on a navigation collection that was never loaded is a
        // silent no-op — the rows stay in the database and the re-add then
        // inserts a second copy of every message. The ThenInclude(...Messages)
        // calls here are what make those Clear()s real.
        var categories = await db.Categories
            .Include(c => c.Stories).ThenInclude(s => s.Versions).ThenInclude(v => v.Messages)
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .Include(c => c.OneLiners)
            .Include(c => c.Tropes)
            .ToListAsync();

        foreach (var categoryDir in libraryDir.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            seen.Categories.Add(categoryDir.Name);

            var category = MatchOnDisk(categories, c => c.Name, categoryDir.Name);

            var isNew = category is null;
            if (category is null)
            {
                category = new Category { Name = categoryDir.Name, CreatedAt = DateTime.UtcNow };
                db.Categories.Add(category);
                categories.Add(category);
                result.CategoriesCreated++;
            }

            var childTimestamps = new List<DateTime>();

            await ImportStoriesAsync(categoryDir, category, result, childTimestamps, mirror, seen);
            await ImportPromptsAsync(categoryDir, category, result, childTimestamps, mirror, seen);
            ImportOneLiners(categoryDir, category, result, isNew, mirror, seen);
            ImportTropes(categoryDir, category, result, isNew, mirror, seen);

            // Category.CreatedAt is derived, not stored on disk.
            if (isNew && childTimestamps.Count > 0)
                category.CreatedAt = childTimestamps.Min();
        }
    }

    private static async Task ImportStoriesAsync(
        DirectoryInfo categoryDir, Category category, ImportResult result,
        List<DateTime> childTimestamps, bool mirror, SeenPaths seen)
    {
        var storiesDir = new DirectoryInfo(Path.Combine(categoryDir.FullName, ArchiveLayout.StoriesDir));
        if (!storiesDir.Exists) return;

        foreach (var storyDir in storiesDir.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            seen.Stories.Add($"{categoryDir.Name}/{storyDir.Name}");

            var story = MatchOnDisk(category.Stories, s => s.Title, storyDir.Name);
            var isNew = story is null;
            if (story is null)
            {
                story = new Story { Title = storyDir.Name, CreatedAt = DateTime.UtcNow, Category = category };
                category.Stories.Add(story);
                result.StoriesCreated++;
            }

            var versionTimestamps = new List<DateTime>();

            foreach (var file in storyDir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (!ArchiveLayout.TryParseVersionFileName(file.Name, out var versionNumber))
                {
                    Console.Error.WriteLine($"warn: skipping unrecognized file: {file.FullName}");
                    seen.Incomplete = true;
                    continue;
                }

                seen.Versions.Add($"{categoryDir.Name}/{storyDir.Name}/{versionNumber}");

                Dictionary<string, string> fields;
                string body;
                bool malformedLines;
                try
                {
                    (fields, body) = FrontMatter.Parse(
                        await File.ReadAllTextAsync(file.FullName), out malformedLines);
                }
                catch (InvalidDataException ex)
                {
                    Console.Error.WriteLine($"warn: skipping malformed file: {file.FullName} ({ex.Message})");
                    seen.Incomplete = true;
                    continue;
                }
                if (malformedLines) seen.Incomplete = true;

                var created = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? DateTime.UtcNow;
                versionTimestamps.Add(created);

                var existingVersion = story.Versions.FirstOrDefault(v => v.VersionNumber == versionNumber);
                if (existingVersion is not null)
                {
                    if (mirror) UpdateVersion(existingVersion, fields, body);
                    continue;
                }

                var origin = ParseStoryOrigin(Scalar(fields, "origin"), file.FullName);
                var version = new StoryVersion
                {
                    VersionNumber = versionNumber,
                    Origin = origin,
                    ModelName = StoryModelName(fields, origin),
                    CreatedAt = created,
                    Story = story,
                };
                version.Messages.AddRange(ConversationFormat.Read(body).Select(t => new StoryMessage
                {
                    Role = t.Role, Content = t.Content, SortOrder = t.SortOrder,
                }));

                story.Versions.Add(version);
                result.VersionsCreated++;
            }

            // Story.CreatedAt is derived from its earliest version.
            if (isNew && versionTimestamps.Count > 0)
                story.CreatedAt = versionTimestamps.Min();

            childTimestamps.Add(story.CreatedAt);
        }
    }

    private static void UpdateVersion(StoryVersion version, Dictionary<string, string> fields, string body)
    {
        version.Origin = ParseStoryOrigin(Scalar(fields, "origin"));
        version.ModelName = StoryModelName(fields, version.Origin);
        version.CreatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? version.CreatedAt;
        version.Messages.Clear();
        version.Messages.AddRange(ConversationFormat.Read(body).Select(t => new StoryMessage
        {
            Role = t.Role, Content = t.Content, SortOrder = t.SortOrder,
        }));
    }

    private static StoryOrigin ParseStoryOrigin(string? raw, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return StoryOrigin.Generated; // archives written before provenance was added

        if (Enum.TryParse<StoryOrigin>(raw, ignoreCase: true, out var origin))
            return origin;

        Console.Error.WriteLine(
            $"warn: unrecognized story origin '{raw}', using Generated" +
            (path is null ? "" : $": {path}"));
        return StoryOrigin.Generated;
    }

    private static string? StoryModelName(
        Dictionary<string, string> fields, StoryOrigin origin) =>
        Scalar(fields, "model") ?? (origin == StoryOrigin.Generated ? "(unknown)" : null);

    private static async Task ImportPromptsAsync(
        DirectoryInfo categoryDir, Category category, ImportResult result,
        List<DateTime> childTimestamps, bool mirror, SeenPaths seen)
    {
        var promptsDir = new DirectoryInfo(Path.Combine(categoryDir.FullName, ArchiveLayout.PromptsDir));
        if (!promptsDir.Exists) return;

        foreach (var file in promptsDir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var title = Path.GetFileNameWithoutExtension(file.Name);
            seen.Prompts.Add($"{categoryDir.Name}/{title}");

            Dictionary<string, string> fields;
            string body;
            bool malformedLines;
            try
            {
                (fields, body) = FrontMatter.Parse(
                    await File.ReadAllTextAsync(file.FullName), out malformedLines);
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"warn: skipping malformed file: {file.FullName} ({ex.Message})");
                seen.Incomplete = true;
                continue;
            }
            if (malformedLines) seen.Incomplete = true;

            var created = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? DateTime.UtcNow;
            childTimestamps.Add(created);

            var existing = MatchOnDisk(category.Prompts, p => p.Title, title);
            if (existing is not null)
            {
                if (mirror) UpdatePrompt(existing, fields, body);
                continue;
            }

            var prompt = new Prompt
            {
                Title = title,
                CreatedAt = created,
                UpdatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "updated")) ?? created,
                Category = category,
            };

            var sortOrder = 0;
            foreach (var block in BlockListFormat.Read(body))
                prompt.Messages.Add(new PromptMessage { Content = block, SortOrder = sortOrder++ });

            category.Prompts.Add(prompt);
            result.PromptsCreated++;
        }
    }

    private static void UpdatePrompt(Prompt prompt, Dictionary<string, string> fields, string body)
    {
        prompt.CreatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? prompt.CreatedAt;
        prompt.UpdatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "updated")) ?? prompt.UpdatedAt;
        prompt.Messages.Clear();
        var sortOrder = 0;
        foreach (var block in BlockListFormat.Read(body))
            prompt.Messages.Add(new PromptMessage { Content = block, SortOrder = sortOrder++ });
    }

    private static void ImportOneLiners(
        DirectoryInfo categoryDir, Category category, ImportResult result, bool isNew, bool mirror,
        SeenPaths seen)
    {
        var path = Path.Combine(categoryDir.FullName, ArchiveLayout.OneLinersFile);
        var present = File.Exists(path);

        // Under merge a missing file means "unspecified"; under mirror it
        // means "empty list", because export always writes the file.
        if (!present && !mirror) return;

        List<string> items = present ? ItemListFormat.Read(File.ReadAllText(path)) : [];

        if (mirror)
        {
            // Clearing the list destroys rows, so it has to reach the plan
            // and the confirmation prompt like any other deletion.
            RecordListLosses(
                seen, category.Name, ArchiveLayout.OneLinersFile,
                category.OneLiners.Select(o => o.Text), items);
            category.OneLiners.Clear();
        }

        var now = DateTime.UtcNow;

        // A category created this run (under merge) has nothing to dedupe
        // against, so import every item verbatim -- a vault may legitimately
        // hold two identical one-liners. The same is true under mirror once
        // the list has just been cleared above: the file's contents replace
        // the list wholesale, verbatim, with no dedupe. Only a merge into a
        // category that already existed keeps the dedupe-by-text path, so
        // re-importing stays idempotent.
        var dedupe = !isNew && !mirror;
        var existing = dedupe
            ? category.OneLiners.Select(o => o.Text).ToHashSet(StringComparer.Ordinal)
            : null;

        foreach (var text in items)
        {
            if (existing is not null && !existing.Add(text)) continue;
            category.OneLiners.Add(new OneLiner { Text = text, CreatedAt = now, UpdatedAt = now });
            result.OneLinersCreated++;
        }
    }

    private static void ImportTropes(
        DirectoryInfo categoryDir, Category category, ImportResult result, bool isNew, bool mirror,
        SeenPaths seen)
    {
        var path = Path.Combine(categoryDir.FullName, ArchiveLayout.TropesFile);
        var present = File.Exists(path);

        if (!present && !mirror) return;

        List<string> items = present ? ItemListFormat.Read(File.ReadAllText(path)) : [];

        if (mirror)
        {
            RecordListLosses(
                seen, category.Name, ArchiveLayout.TropesFile,
                category.Tropes.Select(t => t.Text), items);
            category.Tropes.Clear();
        }

        var now = DateTime.UtcNow;

        // See ImportOneLiners for why dedupe only applies to a merge into an
        // already-existing category.
        var dedupe = !isNew && !mirror;
        var existing = dedupe
            ? category.Tropes.Select(t => t.Text).ToHashSet(StringComparer.Ordinal)
            : null;

        foreach (var text in items)
        {
            if (existing is not null && !existing.Add(text)) continue;
            category.Tropes.Add(new Trope { Text = text, CreatedAt = now, UpdatedAt = now });
            result.TropesCreated++;
        }
    }

    /// <summary>
    /// Notes, for the mirror plan, how many items a wholesale list
    /// replacement would destroy. An item survives only if the file still
    /// carries the same text (multiplicity included), so a file that merely
    /// re-states the list — the ordinary re-import — records nothing.
    /// </summary>
    private static void RecordListLosses(
        SeenPaths seen, string categoryName, string fileName,
        IEnumerable<string> existing, IEnumerable<string> incoming)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var text in incoming)
            remaining[text] = remaining.GetValueOrDefault(text) + 1;

        var lost = 0;
        var total = 0;
        foreach (var text in existing)
        {
            total++;
            if (remaining.TryGetValue(text, out var count) && count > 0) remaining[text] = count - 1;
            else lost++;
        }

        if (lost > 0)
            seen.ListLosses.Add($"{categoryName}/{fileName} ({lost} of {total} items)");
    }

    private static async Task ImportDraftsAsync(
        FabulisDbContext db, DirectoryInfo root, ImportResult result, bool mirror, SeenPaths seen)
    {
        var draftsDir = new DirectoryInfo(Path.Combine(root.FullName, ArchiveLayout.DraftsDir));
        if (!draftsDir.Exists) return;

        var storytellers = await db.Storytellers.ToListAsync();

        foreach (var file in draftsDir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            Dictionary<string, string> fields;
            string body;
            bool malformedLines;
            try
            {
                (fields, body) = FrontMatter.Parse(
                    await File.ReadAllTextAsync(file.FullName), out malformedLines);
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"warn: skipping malformed file: {file.FullName} ({ex.Message})");
                // A draft we could not parse is a draft we cannot record in
                // `seen`, and an unrecorded draft reads as "deleted from the
                // archive". Every other kind records its path before parsing;
                // a draft cannot, because its identity lives in the front
                // matter. Flagging the run as incomplete is what keeps this
                // file's row (and every other row) from being deleted.
                seen.Incomplete = true;
                continue;
            }

            // A line the parser had to throw away is a field that is gone,
            // and a draft's identity IS its front matter: drop `created` and
            // this file describes a draft the vault does not have, while the
            // real row reads as absent from the archive. Same reasoning as
            // the catch above, so the same answer -- no deletions this run.
            if (malformedLines) seen.Incomplete = true;

            var storytellerName = Scalar(fields, "storyteller");
            var storyteller = string.IsNullOrWhiteSpace(storytellerName)
                ? null
                : MatchOnDisk(storytellers, s => s.Name, storytellerName);
            if (storyteller is null)
            {
                Console.Error.WriteLine(
                    $"warn: draft references unknown storyteller '{storytellerName}', skipping: {file.FullName}");
                seen.Incomplete = true;
                continue;
            }

            // The filename is derived and sanitized; front matter is authoritative.
            var title = Scalar(fields, "title");
            var createdAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? DateTime.UtcNow;
            var updatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "updated")) ?? createdAt;

            seen.Drafts.Add(DraftKey(ArchiveLayout.Sanitize(storytellerName!), title, createdAt));

            var existing = await db.Drafts.Include(d => d.Messages).FirstOrDefaultAsync(d =>
                d.StorytellerID == storyteller.Id && d.Title == title && d.CreatedAt == createdAt);
            if (existing is not null)
            {
                if (mirror) UpdateDraft(existing, fields, body);
                continue;
            }

            var draft = new Draft
            {
                StorytellerID = storyteller.Id,
                Storyteller = storyteller,
                Title = title,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
            };
            draft.Messages.AddRange(ConversationFormat.Read(body).Select(t => new DraftMessage
            {
                Role = t.Role, Content = t.Content, SortOrder = t.SortOrder,
            }));

            db.Drafts.Add(draft);
            result.DraftsCreated++;
        }
    }

    private static void UpdateDraft(Draft draft, Dictionary<string, string> fields, string body)
    {
        draft.UpdatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "updated")) ?? draft.UpdatedAt;
        draft.Messages.Clear();
        draft.Messages.AddRange(ConversationFormat.Read(body).Select(t => new DraftMessage
        {
            Role = t.Role, Content = t.Content, SortOrder = t.SortOrder,
        }));
    }

    /// <summary>
    /// Key identifying a draft by content rather than row id, matching what
    /// <see cref="ImportDraftsAsync"/> dedupes new drafts against. Timestamps
    /// are normalized through <see cref="FrontMatter.FormatTimestamp"/> so a
    /// round-tripped DateTime (which may lose sub-second precision on disk)
    /// still compares equal to the value read back from that same file. The
    /// storyteller component is always the ON-DISK name (see
    /// <see cref="OnDiskName"/>), on both the archive side and the database
    /// side, so the two agree about which draft a file describes.
    /// </summary>
    private static string DraftKey(string storytellerDiskName, string? title, DateTime createdAt) =>
        $"{storytellerDiskName}|{title}|{FrontMatter.FormatTimestamp(createdAt)}";

    /// <summary>
    /// The name a row is stored under on disk. Export puts every name through
    /// <see cref="ArchiveLayout.Sanitize"/>, so a row whose name contains a
    /// slash, a trailing space or a control character is NOT named on disk
    /// what it is named in the database — and matching the raw name against a
    /// directory would classify that row as "absent from the archive" and
    /// delete it.
    /// </summary>
    private static string OnDiskName(string name) => ArchiveLayout.Sanitize(name);

    /// <summary>
    /// Finds the row a directory or file name refers to: an exact name match
    /// first, then a match on the sanitized name. Returns null when nothing
    /// matches.
    /// </summary>
    private static T? MatchOnDisk<T>(IEnumerable<T> rows, Func<T, string> name, string diskName)
        where T : class
    {
        var candidates = rows as IReadOnlyCollection<T> ?? rows.ToList();

        foreach (var row in candidates)
            if (string.Equals(name(row), diskName, StringComparison.Ordinal)) return row;

        foreach (var row in candidates)
            if (string.Equals(OnDiskName(name(row)), diskName, StringComparison.OrdinalIgnoreCase)) return row;

        return null;
    }

    /// <summary>
    /// Refuses to mirror when two rows in one scope would claim the same
    /// on-disk name. Export resolves such a clash with a " (2)" suffix, which
    /// leaves no way back: nothing in the archive says which row the suffixed
    /// file came from. Since mirror deletes rows, guessing wrong destroys the
    /// user's work — so this stops the run instead and asks for a rename.
    /// The comparison is case-insensitive to match <see cref="NameAllocator"/>,
    /// which is what actually decides whether two names collide on disk.
    /// </summary>
    private static void GuardAgainstAmbiguousNames(IEnumerable<string> names, string kind, string scope)
    {
        var clash = names
            .GroupBy(OnDiskName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (clash is null) return;

        throw new InvalidOperationException(
            $"Cannot mirror: {kind} in {scope} share the on-disk name '{clash.Key}' " +
            $"({string.Join(", ", clash.Select(n => $"'{n}'"))}). Rename one of them, " +
            "then run the import again.");
    }

    /// <summary>
    /// The rows a mirror import would delete, together with the plan that
    /// describes them. Plan and rows are built together, from one traversal,
    /// so what the user confirms is exactly what gets removed.
    /// </summary>
    private sealed record MirrorDeletions(
        MirrorPlan Plan,
        List<Category> Categories,
        List<Story> Stories,
        List<StoryVersion> Versions,
        List<Prompt> Prompts,
        List<Draft> Drafts,
        List<Storyteller> Storytellers,
        List<Draft> SurvivingDrafts);

    /// <summary>
    /// Deletions are off for this run because part of the archive could not
    /// be read. List replacement already happened in pass one, so it stays in
    /// the plan — the user still gets to confirm (or cancel) that much.
    /// </summary>
    private static MirrorDeletions SuppressedDeletions(SeenPaths seen)
    {
        Console.Error.WriteLine(
            "warn: skipping all deletions: part of the archive could not be read (see the " +
            "warnings above). Rows missing from the archive have been left alone, because a " +
            "file that cannot be read is indistinguishable from a file that was removed. Fix " +
            "or remove those files and run the import again to apply deletions.");

        return new MirrorDeletions(
            new MirrorPlan([], [], [], [], [], [], seen.ListLosses),
            [], [], [], [], [], [], []);
    }

    private static async Task<MirrorDeletions> BuildMirrorDeletionsAsync(
        FabulisDbContext db, SeenPaths seen)
    {
        var all = await db.Categories
            .Include(c => c.Stories).ThenInclude(s => s.Versions)
            .Include(c => c.Prompts)
            .Include(c => c.OneLiners)
            .Include(c => c.Tropes)
            .ToListAsync();
        var allStorytellers = await db.Storytellers.ToListAsync();

        // Before planning a single deletion: every scope must map to disk
        // names unambiguously, or we cannot know which row a directory is.
        GuardAgainstAmbiguousNames(all.Select(c => c.Name), "categories", $"'{ArchiveLayout.LibraryDir}'");
        foreach (var category in all)
        {
            GuardAgainstAmbiguousNames(
                category.Stories.Select(s => s.Title), "stories", $"category '{category.Name}'");
            GuardAgainstAmbiguousNames(
                category.Prompts.Select(p => p.Title), "prompts", $"category '{category.Name}'");
        }
        GuardAgainstAmbiguousNames(
            allStorytellers.Select(s => s.Name), "storytellers", $"'{ArchiveLayout.StorytellersDir}'");

        var categoryRows = new List<Category>();
        var storyRows = new List<Story>();
        var versionRows = new List<StoryVersion>();
        var promptRows = new List<Prompt>();

        var categories = new List<string>();
        var stories = new List<string>();
        var versions = new List<string>();
        var prompts = new List<string>();
        var lists = new List<string>(seen.ListLosses);

        foreach (var category in all)
        {
            var storyLabels = category.Stories
                .Select(s => (Story: s, Label: $"{category.Name}/{s.Title}"))
                .ToList();

            if (!seen.Categories.Contains(OnDiskName(category.Name)))
            {
                categoryRows.Add(category);
                categories.Add(category.Name);

                // Everything under the category is destroyed with it. Listing
                // only the category would have the user confirm far less than
                // actually happens.
                foreach (var (story, label) in storyLabels)
                {
                    stories.Add(label);
                    foreach (var version in story.Versions)
                        versions.Add($"{label}/Version {version.VersionNumber}");
                }
                foreach (var prompt in category.Prompts)
                    prompts.Add($"{category.Name}/{prompt.Title}");
                if (category.OneLiners.Count > 0)
                    lists.Add($"{category.Name}/{ArchiveLayout.OneLinersFile} " +
                              $"({category.OneLiners.Count} of {category.OneLiners.Count} items)");
                if (category.Tropes.Count > 0)
                    lists.Add($"{category.Name}/{ArchiveLayout.TropesFile} " +
                              $"({category.Tropes.Count} of {category.Tropes.Count} items)");
                continue;
            }

            var categoryDisk = OnDiskName(category.Name);

            foreach (var (story, label) in storyLabels)
            {
                var storyDisk = $"{categoryDisk}/{OnDiskName(story.Title)}";
                if (!seen.Stories.Contains(storyDisk))
                {
                    storyRows.Add(story);
                    stories.Add(label);
                    foreach (var version in story.Versions)
                        versions.Add($"{label}/Version {version.VersionNumber}");
                    continue;
                }

                foreach (var version in story.Versions)
                {
                    if (seen.Versions.Contains($"{storyDisk}/{version.VersionNumber}")) continue;
                    versionRows.Add(version);
                    versions.Add($"{label}/Version {version.VersionNumber}");
                }
            }

            foreach (var prompt in category.Prompts)
            {
                if (seen.Prompts.Contains($"{categoryDisk}/{OnDiskName(prompt.Title)}")) continue;
                promptRows.Add(prompt);
                prompts.Add($"{category.Name}/{prompt.Title}");
            }
        }

        // Drafts before storytellers, so a storyteller freed by a deleted
        // draft can go in the same run. survivingDrafts is compared by
        // storyteller NAME (not the StorytellerID scalar) below: comparing
        // by id is fragile here because a draft created earlier in this same
        // run could in principle carry a stale/unset id depending on when it
        // was flushed, and the whole point of this guard is to catch exactly
        // that same-run reference reliably.
        var draftRows = new List<Draft>();
        var survivingDrafts = new List<Draft>();
        var drafts = new List<string>();
        foreach (var draft in await db.Drafts.Include(d => d.Storyteller).ToListAsync())
        {
            if (seen.Drafts.Contains(
                    DraftKey(OnDiskName(draft.Storyteller.Name), draft.Title, draft.CreatedAt)))
            {
                survivingDrafts.Add(draft);
                continue;
            }

            draftRows.Add(draft);
            drafts.Add(draft.Title ?? "(untitled)");
        }

        var storytellerRows = allStorytellers
            .Where(s => !seen.Storytellers.Contains(OnDiskName(s.Name)))
            .ToList();

        var plan = new MirrorPlan(
            categories, stories, versions, prompts, drafts,
            storytellerRows.Select(s => s.Name).ToList(), lists);

        return new MirrorDeletions(
            plan, categoryRows, storyRows, versionRows, promptRows,
            draftRows, storytellerRows, survivingDrafts);
    }

    private static void ApplyMirrorDeletions(FabulisDbContext db, MirrorDeletions deletions)
    {
        db.Categories.RemoveRange(deletions.Categories);
        db.Stories.RemoveRange(deletions.Stories);
        db.StoryVersions.RemoveRange(deletions.Versions);
        db.Prompts.RemoveRange(deletions.Prompts);
        db.Drafts.RemoveRange(deletions.Drafts);

        foreach (var storyteller in deletions.Storytellers)
        {
            if (deletions.SurvivingDrafts.Any(d => d.Storyteller.Name == storyteller.Name))
                throw new InvalidOperationException(
                    $"Cannot delete storyteller '{storyteller.Name}': a draft in the archive still " +
                    "references it. Remove that draft from the archive, or restore the storyteller file.");

            db.Storytellers.Remove(storyteller);
        }
    }

    /// <summary>
    /// What the first pass saw on disk. Names are exactly as they appear in
    /// the archive; the database side compares against
    /// <see cref="OnDiskName"/> of its own names, so the two meet in the
    /// middle. Comparisons ignore case, matching <see cref="NameAllocator"/>
    /// and the case-insensitive file systems this archive will land on.
    /// </summary>
    private sealed class SeenPaths
    {
        public HashSet<string> Storytellers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Keyed "<category>/<story>" and "<category>/<story>/<version>".
        public HashSet<string> Stories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Prompts { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Keyed per DraftKey: "<storytellerDiskName>|<title>|<createdAt:O>".
        public HashSet<string> Drafts { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Set when any file in the archive could not be read or understood.
        /// Suppresses the whole deletion pass: an unreadable file looks
        /// exactly like a deleted one, and guessing wrong destroys data.
        /// </summary>
        public bool Incomplete { get; set; }

        /// <summary>
        /// Wholesale list replacements that drop items, for the mirror plan.
        /// </summary>
        public List<string> ListLosses { get; } = [];
    }

    private static string? Scalar(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static double? Double(Dictionary<string, string> fields, string key) =>
        double.TryParse(Scalar(fields, key), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Int(Dictionary<string, string> fields, string key) =>
        int.TryParse(Scalar(fields, key), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) ? value : null;
}

public class ImportResult
{
    public int CategoriesCreated { get; set; }
    public int StoriesCreated { get; set; }
    public int VersionsCreated { get; set; }
    public int PromptsCreated { get; set; }
    public int OneLinersCreated { get; set; }
    public int TropesCreated { get; set; }
    public int DraftsCreated { get; set; }
    public int StorytellersCreated { get; set; }
    public int SettingsApplied { get; set; }
    public MirrorPlan? Deleted { get; set; }
    public bool Cancelled { get; set; }
}
