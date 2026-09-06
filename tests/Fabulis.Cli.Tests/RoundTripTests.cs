using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Cli.Tests;

public class RoundTripTests : IDisposable
{
    private readonly VaultFixture _source = new();
    private readonly VaultFixture _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    private async Task<string> ExportSourceAsync(bool includeSecrets = false)
    {
        var dest = _source.TempDir();
        await new VaultExporter().ExportAsync(_source.Db, dest, includeSecrets);
        return dest;
    }

    [Fact]
    public async Task FullVaultSurvivesExportAndImport()
    {
        await _source.SeedFullVaultAsync();
        var archive = await ExportSourceAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var sourceSnapshot = await VaultSnapshot.CaptureAsync(_source.Db);
        // Guard against a no-op fixture: if SeedFullVaultAsync ever stopped
        // seeding content, both snapshots would be identical empty headers
        // and the equality assertion below would pass vacuously.
        Assert.Contains("story=", sourceSnapshot);
        Assert.Contains("draft=", sourceSnapshot);

        Assert.Equal(
            sourceSnapshot,
            await VaultSnapshot.CaptureAsync(_target.Db));
    }

    [Fact]
    public async Task ExportingTheImportedVaultProducesTheSameSnapshot()
    {
        await _source.SeedFullVaultAsync();
        var first = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, first);

        var second = _target.TempDir();
        await new VaultExporter().ExportAsync(_target.Db, second);

        using var third = new VaultFixture();
        await new VaultImporter().ImportAsync(third.Db, second);

        Assert.Equal(
            await VaultSnapshot.CaptureAsync(_target.Db),
            await VaultSnapshot.CaptureAsync(third.Db));
    }

    [Theory]
    [InlineData("**Me:**")]
    [InlineData("**StoryTeller:**")]
    [InlineData("---")]
    [InlineData("\\---")]
    [InlineData("- bullet at the start of a line")]
    [InlineData("## Looks like a section heading")]
    [InlineData("trailing whitespace   ")]
    [InlineData("unicode: café — über ☃")]
    [InlineData("key: value")]
    public async Task AdversarialMessageContentSurvivesRoundTrip(string payload)
    {
        var storyteller = new Storyteller
        {
            Name = "Storyteller",
            Prompt = "p",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = "m",
            CreatedAt = VaultFixture.Epoch,
        };
        var category = new Category { Name = "C", CreatedAt = VaultFixture.Epoch };
        var story = new Story { Title = "S", CreatedAt = VaultFixture.Epoch, Category = category };
        var version = new StoryVersion
        {
            VersionNumber = 1, ModelName = "m", CreatedAt = VaultFixture.Epoch, Story = story,
        };
        version.Messages.Add(new StoryMessage
        {
            Role = MessageRole.Prompt, SortOrder = 0, Content = $"before\n{payload}\nafter",
        });
        story.Versions.Add(version);
        category.Stories.Add(story);
        _source.Db.Storytellers.Add(storyteller);
        _source.Db.Categories.Add(category);
        await _source.Db.SaveChangesAsync();

        var archive = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.StoryVersions.Include(v => v.Messages).SingleAsync();
        Assert.Equal($"before\n{payload}\nafter", imported.Messages.Single().Content);
    }

    /// <summary>
    /// Content whose first or last line is blank or whitespace-only loses
    /// those edge lines on import: <c>ConversationFormat.TrimBlankEdges</c>
    /// strips leading and trailing blank/whitespace-only lines when reading a
    /// message back out of the block format, because a written message is
    /// always framed by a blank line on each side (see
    /// <c>ConversationFormat.Write</c>) and that framing is indistinguishable
    /// from genuine edge whitespace once round-tripped. This is a deliberate,
    /// already-ruled-on tradeoff — not a bug: <c>TrimBlankEdges</c> is shared
    /// by all four archive format classes, whose write shapes differ, so
    /// reworking it risks regressions elsewhere. The loss is cosmetic edge
    /// whitespace only; inner content survives exactly, and the value is
    /// stable after the first round (a second export/import is a no-op).
    /// This test pins that behavior as known and intentional, the same way
    /// <see cref="DocumentedLossyFieldsAreActuallyLost"/> pins the summary
    /// fields, so nobody mistakes it for an accidental regression later.
    /// </summary>
    [Fact]
    public async Task EdgeBlankLinesInMessageContentAreStrippedByDesign()
    {
        var storyteller = new Storyteller
        {
            Name = "Storyteller",
            Prompt = "p",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = "m",
            CreatedAt = VaultFixture.Epoch,
        };
        var category = new Category { Name = "C", CreatedAt = VaultFixture.Epoch };
        var story = new Story { Title = "S", CreatedAt = VaultFixture.Epoch, Category = category };
        var version = new StoryVersion
        {
            VersionNumber = 1, ModelName = "m", CreatedAt = VaultFixture.Epoch, Story = story,
        };
        version.Messages.Add(new StoryMessage
        {
            Role = MessageRole.Prompt,
            SortOrder = 0,
            // Leading blank line, then real content, then a trailing
            // whitespace-only line.
            Content = "\nindented poem\n   ",
        });
        story.Versions.Add(version);
        category.Stories.Add(story);
        _source.Db.Storytellers.Add(storyteller);
        _source.Db.Categories.Add(category);
        await _source.Db.SaveChangesAsync();

        var archive = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.StoryVersions.Include(v => v.Messages).SingleAsync();
        Assert.Equal("indented poem", imported.Messages.Single().Content);
    }

    [Fact]
    public async Task CrlfContentIsNormalizedToLfAndStaysStable()
    {
        await _source.SeedFullVaultAsync();
        var version = await _source.Db.StoryVersions.Include(v => v.Messages).FirstAsync();
        version.Messages.First().Content = "line one\r\nline two";
        await _source.Db.SaveChangesAsync();

        var archive = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.StoryVersions
            .Include(v => v.Messages)
            .FirstAsync(v => v.VersionNumber == 1);
        Assert.Equal("line one\nline two",
            imported.Messages.OrderBy(m => m.SortOrder).First().Content);
    }

    [Fact]
    public async Task SecretsRoundTripOnlyWhenRequested()
    {
        await _source.SeedFullVaultAsync();
        var archive = await ExportSourceAsync(includeSecrets: true);

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var key = await _target.Db.AppSettings.FindAsync("OpenRouterApiKey");
        Assert.Equal("sk-secret-value", key!.Value);
    }

    /// <summary>The other half of <see cref="SecretsRoundTripOnlyWhenRequested"/>:
    /// the default (no <c>includeSecrets</c>) export must not carry the key
    /// at all, and importing it into a vault that already has a key must not
    /// clobber that key.</summary>
    [Fact]
    public async Task SecretsDoNotRoundTripWhenNotRequested()
    {
        await _source.SeedFullVaultAsync();
        var archive = await ExportSourceAsync(); // includeSecrets defaults to false

        await new VaultImporter().ImportAsync(_target.Db, archive);
        Assert.Null(await _target.Db.AppSettings.FindAsync("OpenRouterApiKey"));

        using var targetWithExistingKey = new VaultFixture();
        targetWithExistingKey.Db.AppSettings.Add(
            new AppSetting { Key = "OpenRouterApiKey", Value = "sk-existing" });
        await targetWithExistingKey.Db.SaveChangesAsync();

        await new VaultImporter().ImportAsync(targetWithExistingKey.Db, archive);

        var preserved = await targetWithExistingKey.Db.AppSettings.FindAsync("OpenRouterApiKey");
        Assert.Equal("sk-existing", preserved!.Value);
    }

    [Fact]
    public async Task DocumentedLossyFieldsAreActuallyLost()
    {
        await _source.SeedFullVaultAsync();
        var story = await _source.Db.Stories.FirstAsync(s => s.Title == "The Fox");
        story.SummaryText = "A fox goes out and comes back.";
        story.SummaryStatus = SummaryStatus.Ready;
        story.SummarizedThroughVersion = 1;
        story.SummaryUpdatedAt = VaultFixture.Epoch;
        await _source.Db.SaveChangesAsync();

        var archive = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.Stories.FirstAsync(s => s.Title == "The Fox");
        Assert.Null(imported.SummaryText);
        Assert.Equal(SummaryStatus.None, imported.SummaryStatus);
        // Ids are not preserved either; identity is the path.
        Assert.NotEqual(0, imported.Id);
    }

    /// <summary>
    /// Draft.Title is nullable and stays null until a draft's first prompt
    /// message arrives, so an abandoned draft has no title. The format
    /// represents that by omitting the `title` key (FrontMatter.Serialize
    /// skips nulls); the importer must read the absence back as null rather
    /// than coercing it into the literal string "Untitled" (that string is
    /// only ever used to derive a filename, never stored as data). Seeded
    /// separately from SeedFullVaultAsync so its fixed counts, relied on by
    /// other tests, do not shift.
    /// </summary>
    [Fact]
    public async Task NullTitleDraftRoundTripsAsNullNotUntitled()
    {
        var storyteller = new Storyteller
        {
            Name = "Storyteller",
            Prompt = "p",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = "m",
            CreatedAt = VaultFixture.Epoch,
        };
        _source.Db.Storytellers.Add(storyteller);

        var titled = new Draft
        {
            Storyteller = storyteller,
            Title = "Has A Title",
            CreatedAt = VaultFixture.Epoch,
            UpdatedAt = VaultFixture.Epoch,
        };
        titled.Messages.Add(new DraftMessage
        {
            Role = MessageRole.Prompt, Content = "Set the scene.", SortOrder = 0,
        });
        _source.Db.Drafts.Add(titled);

        var abandoned = new Draft
        {
            Storyteller = storyteller,
            Title = null,
            CreatedAt = VaultFixture.Epoch.AddMinutes(5),
            UpdatedAt = VaultFixture.Epoch.AddMinutes(5),
        };
        _source.Db.Drafts.Add(abandoned);

        await _source.Db.SaveChangesAsync();

        var archive = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.Drafts.ToListAsync();
        var importedAbandoned = Assert.Single(imported, d => d.Title is null);
        Assert.Null(importedAbandoned.Title);
        Assert.DoesNotContain(imported, d => d.Title == "Untitled");
    }
}
