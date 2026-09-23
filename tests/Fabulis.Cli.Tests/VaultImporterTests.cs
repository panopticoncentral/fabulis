using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Cli.Tests;

public class VaultImporterTests : IDisposable
{
    private readonly VaultFixture _source = new();
    private readonly VaultFixture _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    /// <summary>Seeds and exports the source vault, returning the archive path.</summary>
    private async Task<string> ArchiveAsync()
    {
        await _source.SeedFullVaultAsync();
        var dest = _source.TempDir();
        await new VaultExporter().ExportAsync(_source.Db, dest);
        return dest;
    }

    [Fact]
    public async Task RefusesAMissingDirectory()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new VaultImporter().ImportAsync(_target.Db, _target.TempDir()));
    }

    [Fact]
    public async Task RefusesAnUnknownFormatVersion()
    {
        var archive = await ArchiveAsync();
        var manifest = Path.Combine(archive, ArchiveLayout.ManifestFile);
        await File.WriteAllTextAsync(manifest,
            (await File.ReadAllTextAsync(manifest)).Replace("formatVersion: 1", "formatVersion: 99"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new VaultImporter().ImportAsync(_target.Db, archive));

        Assert.Contains("formatVersion", ex.Message);
    }

    [Fact]
    public async Task RefusesADirectoryWithNoManifest()
    {
        var archive = await ArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.ManifestFile));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new VaultImporter().ImportAsync(_target.Db, archive));
    }

    [Fact]
    public async Task ImportsEveryKind()
    {
        var archive = await ArchiveAsync();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.Equal(2, result.CategoriesCreated);
        Assert.Equal(2, result.StoriesCreated);
        Assert.Equal(1, result.VersionsCreated);
        Assert.Equal(1, result.PromptsCreated);
        Assert.Equal(2, result.OneLinersCreated);
        Assert.Equal(1, result.TropesCreated);
        Assert.Equal(1, result.DraftsCreated);
        // EnsureCreated() builds the schema but does not run
        // SeedDefaultStorytellerIfMissingAsync, so the target vault starts with
        // no storytellers and the archived one is created.
        Assert.Equal(1, result.StorytellersCreated);
        // AutoLockMinutes, KokoroBaseUrl, SummaryPrompt -- the API key is redacted.
        Assert.Equal(3, result.SettingsApplied);
    }

    [Fact]
    public async Task ImportsCategoryHoldingOnlyLists()
    {
        var archive = await ArchiveAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var openers = await _target.Db.Categories
            .Include(c => c.OneLiners).Include(c => c.Tropes).Include(c => c.Stories)
            .SingleAsync(c => c.Name == "Openers");

        Assert.Equal(2, openers.OneLiners.Count);
        Assert.Single(openers.Tropes);
        Assert.Empty(openers.Stories);
    }

    [Fact]
    public async Task PreservesEscapedContentThroughImport()
    {
        var archive = await ArchiveAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var version = await _target.Db.StoryVersions
            .Include(v => v.Messages)
            .SingleAsync();
        var messages = version.Messages.OrderBy(m => m.SortOrder).ToList();

        Assert.Equal("Write about a fox.\n**Me:**\nstill the same message", messages[0].Content);
        Assert.Equal("A fox went out.\n\n---\n\nIt returned.", messages[1].Content);
    }

    [Fact]
    public async Task ImportsExternalStoryProvenanceWithoutAModel()
    {
        var archive = await ArchiveAsync();
        var path = Path.Combine(archive, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "The Fox", "Version 1.md");
        var text = await File.ReadAllTextAsync(path);
        text = text.Replace("origin: Generated\n", "origin: Imported\n")
            .Replace("model: anthropic/claude-sonnet-4\n", "");
        await File.WriteAllTextAsync(path, text);

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var version = await _target.Db.StoryVersions.SingleAsync();
        Assert.Equal(StoryOrigin.Imported, version.Origin);
        Assert.Null(version.ModelName);
    }

    [Fact]
    public async Task DerivesStoryAndCategoryTimestampsFromChildren()
    {
        var archive = await ArchiveAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var story = await _target.Db.Stories.SingleAsync(s => s.Title == "The Fox");
        var category = await _target.Db.Categories.SingleAsync(c => c.Name == "Fables");

        Assert.Equal(VaultFixture.Epoch, story.CreatedAt);
        Assert.Equal(VaultFixture.Epoch, category.CreatedAt);
    }

    [Fact]
    public async Task ImportsStorytellerTuningWhenNameIsNew()
    {
        var archive = await ArchiveAsync();
        var storytellerFile = Path.Combine(archive, ArchiveLayout.StorytellersDir, "Storyteller.md");
        File.Move(storytellerFile,
            Path.Combine(archive, ArchiveLayout.StorytellersDir, "Second Voice.md"));

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.Storytellers.SingleAsync(s => s.Name == "Second Voice");
        Assert.Equal(1, result.StorytellersCreated);
        Assert.Equal("anthropic/claude-sonnet-4", imported.ModelName);
        Assert.Equal(0.83, imported.Temperature);
        Assert.Equal(40, imported.TopK);
        Assert.Equal(ReasoningEffort.Medium, imported.ReasoningEffort);
        Assert.Null(imported.TopP);
        Assert.StartsWith("You are a helpful storyteller.", imported.Prompt);
        Assert.Equal("Name this story in exactly three words. No punctuation.", imported.TitlingPrompt);
    }

    [Fact]
    public async Task SkipsDraftWhoseStorytellerIsUnknown()
    {
        var archive = await ArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.StorytellersDir), recursive: true);
        _target.Db.Storytellers.RemoveRange(_target.Db.Storytellers);
        await _target.Db.SaveChangesAsync();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.Equal(0, result.DraftsCreated);
    }

    [Fact]
    public async Task LeavesExistingApiKeyIntactWhenArchiveRedactsIt()
    {
        var archive = await ArchiveAsync();
        _target.Db.AppSettings.Add(new AppSetting { Key = "OpenRouterApiKey", Value = "sk-existing" });
        await _target.Db.SaveChangesAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var key = await _target.Db.AppSettings.FindAsync("OpenRouterApiKey");
        Assert.Equal("sk-existing", key!.Value);
    }

    [Fact]
    public async Task AppliesMultiLineSettingFromBodySection()
    {
        var archive = await ArchiveAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var prompt = await _target.Db.AppSettings.FindAsync("SummaryPrompt");
        Assert.Equal("Summarize this.\n\nBe brief.", prompt!.Value);
    }

    [Fact]
    public async Task IsIdempotent()
    {
        var archive = await ArchiveAsync();
        var importer = new VaultImporter();

        await importer.ImportAsync(_target.Db, archive);
        var second = await importer.ImportAsync(_target.Db, archive);

        Assert.Equal(0, second.CategoriesCreated);
        Assert.Equal(0, second.StoriesCreated);
        Assert.Equal(0, second.VersionsCreated);
        Assert.Equal(0, second.PromptsCreated);
        Assert.Equal(0, second.OneLinersCreated);
        Assert.Equal(0, second.TropesCreated);
        Assert.Equal(0, second.DraftsCreated);
        Assert.Equal(2, await _target.Db.Categories.CountAsync());
        Assert.Equal(1, await _target.Db.StoryVersions.CountAsync());
    }

    [Fact]
    public async Task MergeNeverDeletes()
    {
        var archive = await ArchiveAsync();
        _target.Db.Categories.Add(new Category { Name = "Untouched", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Untouched"));
    }

    [Fact]
    public async Task MissingListFileIsANoOpUnderMerge()
    {
        var archive = await ArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Openers",
            ArchiveLayout.OneLinersFile));

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.Equal(0, result.OneLinersCreated);
    }

    // --- Finding 1: merge mode must not overwrite an existing setting ---

    [Fact]
    public async Task DoesNotOverwriteAnExistingSettingButStillCreatesAMissingOne()
    {
        var archive = await ArchiveAsync();
        _target.Db.AppSettings.Add(new AppSetting { Key = "AutoLockMinutes", Value = "99" });
        await _target.Db.SaveChangesAsync();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        var autoLock = await _target.Db.AppSettings.FindAsync("AutoLockMinutes");
        Assert.Equal("99", autoLock!.Value);

        var kokoro = await _target.Db.AppSettings.FindAsync("KokoroBaseUrl");
        Assert.Equal("http://localhost:8880", kokoro!.Value);

        // KokoroBaseUrl and SummaryPrompt were created; AutoLockMinutes already
        // existed and is not counted; the API key is redacted.
        Assert.Equal(2, result.SettingsApplied);
    }

    // --- Finding 2: a malformed file must warn-and-skip, not abort the run ---

    [Fact]
    public async Task SkipsAMalformedStoryVersionFileAndImportsEverythingElse()
    {
        var archive = await ArchiveAsync();
        var versionFile = Path.Combine(archive, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "The Fox", ArchiveLayout.VersionFileName(1));
        // An opened-but-never-closed front-matter fence: FrontMatter.Parse throws
        // InvalidDataException on this.
        await File.WriteAllTextAsync(versionFile, "---\nmodel: anthropic/claude-sonnet-4\n");

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.Equal(0, result.VersionsCreated);
        // Everything else in the archive still imports: categories, storytellers
        // and drafts do not depend on the malformed version file.
        Assert.Equal(2, result.CategoriesCreated);
        Assert.Equal(1, result.StorytellersCreated);
        Assert.Equal(1, result.DraftsCreated);
        Assert.Equal(2, result.OneLinersCreated);
        Assert.Equal(1, result.TropesCreated);
    }

    // --- Finding 4: duplicate one-liners/tropes must survive a first import ---

    [Fact]
    public async Task DuplicateOneLinersSurviveImportIntoAnEmptyVaultButDedupeOnReimport()
    {
        var category = new Category { Name = "Dupes", CreatedAt = VaultFixture.Epoch };
        category.OneLiners.Add(new OneLiner
        {
            Text = "Same line", CreatedAt = VaultFixture.Epoch, UpdatedAt = VaultFixture.Epoch,
        });
        category.OneLiners.Add(new OneLiner
        {
            Text = "Same line", CreatedAt = VaultFixture.Epoch, UpdatedAt = VaultFixture.Epoch,
        });
        _source.Db.Categories.Add(category);
        await _source.Db.SaveChangesAsync();

        var dest = _source.TempDir();
        await new VaultExporter().ExportAsync(_source.Db, dest);

        var importer = new VaultImporter();
        var first = await importer.ImportAsync(_target.Db, dest);

        var dupes = await _target.Db.Categories.Include(c => c.OneLiners)
            .SingleAsync(c => c.Name == "Dupes");
        Assert.Equal(2, dupes.OneLiners.Count);
        Assert.Equal(2, first.OneLinersCreated);

        // Re-importing into a now-populated vault stays idempotent: the category
        // already exists, so the dedupe-by-text path applies.
        var second = await importer.ImportAsync(_target.Db, dest);
        Assert.Equal(0, second.OneLinersCreated);
    }

    // MatchOnDisk falls back to a case-INSENSITIVE comparison of on-disk
    // names, which governs merge lookups as well as mirror ones. That is
    // deliberate: the exporter's NameAllocator treats two names differing
    // only in case as one on-disk name, and the file systems this archive
    // lands on do too, so a case-differing directory has to fold into the
    // existing row rather than fork a second one.
    [Fact]
    public async Task MergeFoldsACaseDifferingDirectoryIntoTheExistingRow()
    {
        var dest = await ArchiveAsync();
        _target.Db.Categories.Add(new Category { Name = "FABLES", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();
        _target.Db.ChangeTracker.Clear();

        // The archive's directory is "Fables"; the vault's row is "FABLES".
        var result = await new VaultImporter().ImportAsync(_target.Db, dest);

        Assert.Equal(1, result.CategoriesCreated); // "Openers" only
        Assert.Equal(
            ["FABLES", "Openers"],
            await _target.Db.Categories.Select(c => c.Name).OrderBy(n => n).ToListAsync());

        // And the archive's children landed under the row that already existed.
        var category = await _target.Db.Categories
            .Include(c => c.Stories).SingleAsync(c => c.Name == "FABLES");
        Assert.Contains("The Fox", category.Stories.Select(s => s.Title));
    }

    // --- Finding 3: sillytavern output must be a valid, importable archive ---

    [Fact]
    public async Task SillyTavernConvertedOutputImportsSuccessfully()
    {
        var sourceDir = _target.TempDir();
        Directory.CreateDirectory(sourceDir);
        var lines = new[]
        {
            """{"name":"Narrator","mes":"Welcome to the tale.","is_user":false,"send_date":"2025-12-31T23:00:00Z"}""",
            """{"name":"You","mes":"Hello there","is_user":true,"send_date":"2026-01-01T00:00:00Z"}""",
            """{"name":"Narrator","mes":"General Kenobi.","is_user":false,"send_date":"2026-01-01T00:05:00Z"}""",
        };
        await File.WriteAllLinesAsync(Path.Combine(sourceDir, "chat.jsonl"), lines);

        var destDir = _target.TempDir();
        var convertResult = await new SillyTavernConvertService().ConvertAsync(sourceDir, destDir);
        Assert.Equal(1, convertResult.DraftsWritten);

        // The converter has no storyteller directory to draw from, so the
        // storyteller it references must already exist in the target vault.
        _target.Db.Storytellers.Add(new Storyteller
        {
            Name = "Narrator",
            Prompt = "",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = "anthropic/claude-sonnet-4",
            Temperature = 0.7,
            CreatedAt = VaultFixture.Epoch,
        });
        await _target.Db.SaveChangesAsync();

        var result = await new VaultImporter().ImportAsync(_target.Db, destDir);

        Assert.Equal(1, result.DraftsCreated);
        Assert.True(await _target.Db.Drafts.AnyAsync(d => d.Title == "Hello there"));
    }
}
