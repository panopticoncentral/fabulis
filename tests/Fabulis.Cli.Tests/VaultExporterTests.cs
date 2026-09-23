using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Cli.Tests;

public class VaultExporterTests : IDisposable
{
    private readonly VaultFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<string> ExportSeededAsync(bool includeSecrets = false)
    {
        await _fixture.SeedFullVaultAsync();
        var dest = _fixture.TempDir();
        await new VaultExporter().ExportAsync(_fixture.Db, dest, includeSecrets);
        return dest;
    }

    [Fact]
    public async Task RefusesAnExistingDestination()
    {
        var dest = _fixture.TempDir();
        Directory.CreateDirectory(dest);

        await Assert.ThrowsAsync<IOException>(
            () => new VaultExporter().ExportAsync(_fixture.Db, dest));
    }

    [Fact]
    public async Task WritesManifestWithFormatVersion()
    {
        var dest = await ExportSeededAsync();

        var (fields, _) = FrontMatter.Parse(
            await File.ReadAllTextAsync(Path.Combine(dest, ArchiveLayout.ManifestFile)));

        Assert.Equal("1", fields["formatVersion"]);
        Assert.NotNull(FrontMatter.ParseTimestamp(fields["exportedAt"]));
    }

    [Fact]
    public async Task WritesNamespacedTopLevelDirectories()
    {
        var dest = await ExportSeededAsync();

        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.LibraryDir)));
        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.DraftsDir)));
        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.StorytellersDir)));
    }

    [Fact]
    public async Task ExportsCategoryHoldingNoStories()
    {
        var dest = await ExportSeededAsync();

        var openers = Path.Combine(dest, ArchiveLayout.LibraryDir, "Openers");
        Assert.True(Directory.Exists(openers));
        Assert.Equal(2, ItemListFormat.Read(
            await File.ReadAllTextAsync(Path.Combine(openers, ArchiveLayout.OneLinersFile))).Count);
        Assert.Single(ItemListFormat.Read(
            await File.ReadAllTextAsync(Path.Combine(openers, ArchiveLayout.TropesFile))));
    }

    [Fact]
    public async Task WritesEmptyListFilesRatherThanOmittingThem()
    {
        var dest = await ExportSeededAsync();

        var fables = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables");
        Assert.True(File.Exists(Path.Combine(fables, ArchiveLayout.OneLinersFile)));
        Assert.Empty(ItemListFormat.Read(
            await File.ReadAllTextAsync(Path.Combine(fables, ArchiveLayout.OneLinersFile))));
    }

    [Fact]
    public async Task WritesStoryVersionWithModelInFrontMatter()
    {
        var dest = await ExportSeededAsync();

        var path = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "The Fox", "Version 1.md");
        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal("Generated", fields["origin"]);
        Assert.Equal("anthropic/claude-sonnet-4", fields["model"]);
        Assert.Equal(VaultFixture.Epoch, FrontMatter.ParseTimestamp(fields["created"]));
        Assert.Equal(2, ConversationFormat.Read(body).Count);
    }

    [Fact]
    public async Task WritesImportedStoryWithoutPretendModelName()
    {
        await _fixture.SeedFullVaultAsync();
        var version = await _fixture.Db.StoryVersions.SingleAsync();
        version.Origin = StoryOrigin.Imported;
        version.ModelName = null;
        await _fixture.Db.SaveChangesAsync();

        var dest = _fixture.TempDir();
        await new VaultExporter().ExportAsync(_fixture.Db, dest);
        var path = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "The Fox", "Version 1.md");
        var (fields, _) = FrontMatter.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal("Imported", fields["origin"]);
        Assert.False(fields.ContainsKey("model"));
    }

    [Fact]
    public async Task WritesStoryDirectoryEvenWithNoVersions()
    {
        var dest = await ExportSeededAsync();

        var dir = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "Empty Story");

        Assert.True(Directory.Exists(dir));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public async Task WritesStorytellerTuningAndPrompts()
    {
        var dest = await ExportSeededAsync();

        var path = Path.Combine(dest, ArchiveLayout.StorytellersDir, "Storyteller.md");
        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));
        var sections = BodySections.Read(body);

        Assert.Equal("anthropic/claude-sonnet-4", fields["model"]);
        Assert.Equal("0.83", fields["temperature"]);
        Assert.Equal("40", fields["topK"]);
        Assert.Equal("Medium", fields["reasoningEffort"]);
        Assert.False(fields.ContainsKey("topP"));
        Assert.False(fields.ContainsKey("maxTokens"));
        Assert.StartsWith("You are a helpful storyteller.", sections[ArchiveLayout.SystemPromptSection]);
        Assert.Equal("Name this story in exactly three words. No punctuation.",
            sections[ArchiveLayout.TitlingPromptSection]);
    }

    [Fact]
    public async Task WritesPromptMessagesAsBlocks()
    {
        var dest = await ExportSeededAsync();

        var path = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.PromptsDir, "Cold open.md");
        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal(VaultFixture.Epoch, FrontMatter.ParseTimestamp(fields["created"]));
        Assert.Equal(2, BlockListFormat.Read(body).Count);
    }

    [Fact]
    public async Task WritesDraftWithAuthoritativeTitleInFrontMatter()
    {
        var dest = await ExportSeededAsync();

        var path = Path.Combine(dest, ArchiveLayout.DraftsDir,
            "20260411T090312Z - A Night In The Fens.md");
        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal("Storyteller", fields["storyteller"]);
        Assert.Equal("A Night In The Fens", fields["title"]);
        Assert.False(fields.ContainsKey("model"));
        Assert.Single(ConversationFormat.Read(body));
    }

    [Fact]
    public async Task RedactsApiKeyByDefault()
    {
        var dest = await ExportSeededAsync();

        var text = await File.ReadAllTextAsync(Path.Combine(dest, ArchiveLayout.SettingsFile));
        var (fields, body) = FrontMatter.Parse(text);

        Assert.DoesNotContain("sk-secret-value", text);
        Assert.False(fields.ContainsKey("OpenRouterApiKey"));
        Assert.Equal("15", fields["AutoLockMinutes"]);
        Assert.Equal("Summarize this.\n\nBe brief.", BodySections.Read(body)["SummaryPrompt"]);
    }

    [Fact]
    public async Task WritesApiKeyWhenSecretsRequested()
    {
        var dest = await ExportSeededAsync(includeSecrets: true);

        var (fields, _) = FrontMatter.Parse(
            await File.ReadAllTextAsync(Path.Combine(dest, ArchiveLayout.SettingsFile)));

        Assert.Equal("sk-secret-value", fields["OpenRouterApiKey"]);
    }

    [Fact]
    public async Task SanitizesUnsafeNamesAndCountsEverything()
    {
        await _fixture.SeedFullVaultAsync();
        _fixture.Db.Categories.Add(new Category { Name = "Slash/Name", CreatedAt = VaultFixture.Epoch });
        await _fixture.Db.SaveChangesAsync();
        var dest = _fixture.TempDir();

        var result = await new VaultExporter().ExportAsync(_fixture.Db, dest);

        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.LibraryDir, "Slash-Name")));
        Assert.Equal(3, result.Categories);
        Assert.Equal(2, result.Stories);
        Assert.Equal(1, result.Versions);
        Assert.Equal(1, result.Drafts);
        Assert.Equal(1, result.Prompts);
        Assert.Equal(2, result.OneLiners);
        Assert.Equal(1, result.Tropes);
        Assert.Equal(1, result.Storytellers);
        Assert.True(result.SettingsWritten);
    }

    [Fact]
    public async Task CategoriesCollidingAfterSanitizeGetSuffixedDirectories()
    {
        _fixture.Db.Categories.Add(new Category { Name = "A/B", CreatedAt = VaultFixture.Epoch });
        _fixture.Db.Categories.Add(new Category { Name = "A-B", CreatedAt = VaultFixture.Epoch });
        await _fixture.Db.SaveChangesAsync();
        var dest = _fixture.TempDir();

        await new VaultExporter().ExportAsync(_fixture.Db, dest);

        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.LibraryDir, "A-B")));
        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.LibraryDir, "A-B (2)")));
    }

    [Fact]
    public async Task StoriesSharingATitleAcrossCategoriesBothKeepUnsuffixedNames()
    {
        var catA = new Category { Name = "Cat A", CreatedAt = VaultFixture.Epoch };
        var catB = new Category { Name = "Cat B", CreatedAt = VaultFixture.Epoch };
        catA.Stories.Add(new Story { Title = "Duplicate", CreatedAt = VaultFixture.Epoch, Category = catA });
        catB.Stories.Add(new Story { Title = "Duplicate", CreatedAt = VaultFixture.Epoch, Category = catB });
        _fixture.Db.Categories.AddRange(catA, catB);
        await _fixture.Db.SaveChangesAsync();
        var dest = _fixture.TempDir();

        await new VaultExporter().ExportAsync(_fixture.Db, dest);

        Assert.True(Directory.Exists(Path.Combine(
            dest, ArchiveLayout.LibraryDir, "Cat A", ArchiveLayout.StoriesDir, "Duplicate")));
        Assert.True(Directory.Exists(Path.Combine(
            dest, ArchiveLayout.LibraryDir, "Cat B", ArchiveLayout.StoriesDir, "Duplicate")));
    }

    [Fact]
    public async Task DraftStorytellerReferenceMatchesAllocatedStorytellerStem()
    {
        var storyteller = new Storyteller
        {
            Name = "Sci-Fi/Fantasy Narrator",
            Prompt = "You are a helpful storyteller.",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = "anthropic/claude-sonnet-4",
            Temperature = 0.7,
            CreatedAt = VaultFixture.Epoch,
        };
        _fixture.Db.Storytellers.Add(storyteller);

        var draft = new Draft
        {
            Storyteller = storyteller,
            Title = "Some Draft",
            CreatedAt = VaultFixture.Epoch,
            UpdatedAt = VaultFixture.Epoch,
        };
        _fixture.Db.Drafts.Add(draft);
        await _fixture.Db.SaveChangesAsync();
        var dest = _fixture.TempDir();

        await new VaultExporter().ExportAsync(_fixture.Db, dest);

        var storytellerFile = Assert.Single(
            Directory.GetFiles(Path.Combine(dest, ArchiveLayout.StorytellersDir)));
        var stem = Path.GetFileNameWithoutExtension(storytellerFile);
        Assert.Equal("Sci-Fi-Fantasy Narrator", stem);

        var draftFile = Assert.Single(Directory.GetFiles(Path.Combine(dest, ArchiveLayout.DraftsDir)));
        var (fields, _) = FrontMatter.Parse(await File.ReadAllTextAsync(draftFile));
        Assert.Equal(stem, fields["storyteller"]);
    }

    // Nothing in the schema stops two StoryVersion rows in one story from
    // sharing a VersionNumber (a concurrent double-save can produce it), and
    // both would be written to the same "Version N.md" -- the second silently
    // overwriting the first. A " (2)" suffix would not help: that name does
    // not parse back as a version file. So the loss is at least made visible.
    [Fact]
    public async Task WarnsWhenTwoVersionsShareAVersionNumber()
    {
        await _fixture.SeedFullVaultAsync();
        var story = await _fixture.Db.Stories
            .Include(s => s.Versions)
            .FirstAsync(s => s.Title == "The Fox");
        story.Versions.Add(new StoryVersion
        {
            VersionNumber = 1,
            ModelName = "anthropic/claude-opus-4",
            CreatedAt = VaultFixture.Epoch.AddHours(1),
            Story = story,
        });
        await _fixture.Db.SaveChangesAsync();

        var dest = _fixture.TempDir();
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            await new VaultExporter().ExportAsync(_fixture.Db, dest);
        }
        finally
        {
            Console.SetError(original);
        }

        // Matched line by line, and on a phrase unique to this warning: other
        // test classes run in parallel and write their own warnings (some
        // carrying paths that mention this very story) into the same writer.
        var line = captured.ToString()
            .Split('\n')
            .FirstOrDefault(l => l.Contains("duplicate version number", StringComparison.Ordinal));
        Assert.NotNull(line);
        Assert.Contains("The Fox", line);
        Assert.Contains("Version 1.md", line);

        // One file, as before -- the point is that the overwrite is reported.
        Assert.Single(Directory.GetFiles(Path.Combine(
            dest, ArchiveLayout.LibraryDir, "Fables", ArchiveLayout.StoriesDir, "The Fox")));
    }
}
