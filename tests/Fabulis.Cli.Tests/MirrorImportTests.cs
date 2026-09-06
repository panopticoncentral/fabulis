using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Cli.Tests;

public class MirrorImportTests : IDisposable
{
    private readonly VaultFixture _source = new();
    private readonly VaultFixture _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    /// <summary>Seeds, exports, and imports once so both sides match.</summary>
    private async Task<string> SyncedArchiveAsync()
    {
        await _source.SeedFullVaultAsync();
        var archive = _source.TempDir();
        await new VaultExporter().ExportAsync(_source.Db, archive);
        await new VaultImporter().ImportAsync(_target.Db, archive);
        return archive;
    }

    private static ImportOptions Mirror(Func<MirrorPlan, bool>? confirm = null) =>
        new(Mirror: true, ConfirmDeletions: confirm ?? (_ => true));

    [Fact]
    public async Task DeletesCategoryAbsentFromDisk()
    {
        var archive = await SyncedArchiveAsync();
        _target.Db.Categories.Add(new Category { Name = "Extra", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.False(await _target.Db.Categories.AnyAsync(c => c.Name == "Extra"));
        Assert.Contains("Extra", result.Deleted!.Categories);
    }

    [Fact]
    public async Task DeletesStoryAndVersionAbsentFromDisk()
    {
        var archive = await SyncedArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "Empty Story"), recursive: true);
        File.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "The Fox", "Version 1.md"));

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.False(await _target.Db.Stories.AnyAsync(s => s.Title == "Empty Story"));
        Assert.Equal(0, await _target.Db.StoryVersions.CountAsync());
        Assert.Contains("Empty Story", string.Join("|", result.Deleted!.Stories));
        Assert.Single(result.Deleted.Versions);
    }

    [Fact]
    public async Task ReplacesListItemsRatherThanMerging()
    {
        var archive = await SyncedArchiveAsync();
        var path = Path.Combine(archive, ArchiveLayout.LibraryDir, "Openers",
            ArchiveLayout.OneLinersFile);
        await File.WriteAllTextAsync(path, ItemListFormat.Write(["Only this one."]));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var openers = await _target.Db.Categories
            .Include(c => c.OneLiners).SingleAsync(c => c.Name == "Openers");
        Assert.Equal(["Only this one."], openers.OneLiners.Select(o => o.Text));
    }

    [Fact]
    public async Task MissingListFileEmptiesTheListUnderMirror()
    {
        var archive = await SyncedArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Openers",
            ArchiveLayout.OneLinersFile));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var openers = await _target.Db.Categories
            .Include(c => c.OneLiners).SingleAsync(c => c.Name == "Openers");
        Assert.Empty(openers.OneLiners);
    }

    [Fact]
    public async Task UpdatesExistingRowsToMatchDisk()
    {
        var archive = await SyncedArchiveAsync();
        var storytellerFile = Path.Combine(archive, ArchiveLayout.StorytellersDir, "Storyteller.md");
        // VaultFixture seeds Temperature = 0.83, not the importer's 0.7
        // fallback (deliberately, per its own comment), so that is the
        // value actually on disk here.
        await File.WriteAllTextAsync(storytellerFile,
            (await File.ReadAllTextAsync(storytellerFile)).Replace("temperature: 0.83", "temperature: 1.1"));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var storyteller = await _target.Db.Storytellers.SingleAsync(s => s.Name == "Storyteller");
        Assert.Equal(1.1, storyteller.Temperature);
    }

    [Fact]
    public async Task RefusesToDeleteAStorytellerStillReferencedByASurvivingDraft()
    {
        var archive = await SyncedArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.StorytellersDir), recursive: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new VaultImporter().ImportAsync(_target.Db, archive, Mirror()));

        Assert.Contains("Storyteller", ex.Message);
        Assert.True(await _target.Db.Storytellers.AnyAsync());
    }

    // The brief's guard fixture above only exercises a draft that already
    // existed in the target vault before this run (it was created by
    // SyncedArchiveAsync's own prior import). A buggy guard that compares
    // `Draft.StorytellerID` against `Storyteller.Id` can pass that fixture
    // while still missing a draft that is CREATED during this very run: a
    // freshly-added Draft is only visible to the deletion pass's queries
    // once pass one's changes have been flushed, and comparing raw ids
    // adds a second, independent way to get that comparison wrong. This
    // test adds a brand new draft file (absent from the target vault before
    // this import) that references the storyteller whose file is removed,
    // so the only surviving reference is the one created in this same run.
    [Fact]
    public async Task RefusesToDeleteAStorytellerReferencedOnlyByADraftCreatedInThisSameRun()
    {
        var archive = await SyncedArchiveAsync();

        foreach (var file in Directory.GetFiles(Path.Combine(archive, ArchiveLayout.DraftsDir)))
            File.Delete(file);

        var newDraftCreated = VaultFixture.Epoch.AddDays(1);
        var draftText = FrontMatter.Serialize(
            [
                new("storyteller", "Storyteller"),
                new("title", "Brand New Draft"),
                new("created", FrontMatter.FormatTimestamp(newDraftCreated)),
                new("updated", FrontMatter.FormatTimestamp(newDraftCreated)),
            ],
            ConversationFormat.Write([new ConversationFormat.Turn(MessageRole.Prompt, "Begin.", 0)]));
        await File.WriteAllTextAsync(
            Path.Combine(archive, ArchiveLayout.DraftsDir, "brand-new-draft.md"), draftText);

        Directory.Delete(Path.Combine(archive, ArchiveLayout.StorytellersDir), recursive: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new VaultImporter().ImportAsync(_target.Db, archive, Mirror()));

        Assert.Contains("Storyteller", ex.Message);
        Assert.True(await _target.Db.Storytellers.AnyAsync(s => s.Name == "Storyteller"));
        // Whole run rolls back together: the new draft must not have been
        // half-committed while the storyteller deletion was refused.
        Assert.False(await _target.Db.Drafts.AnyAsync(d => d.Title == "Brand New Draft"));
    }

    [Fact]
    public async Task DeletesStorytellerOnceItsDraftsAreAlsoGone()
    {
        var archive = await SyncedArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.StorytellersDir), recursive: true);
        foreach (var file in Directory.GetFiles(Path.Combine(archive, ArchiveLayout.DraftsDir)))
            File.Delete(file);

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.Empty(await _target.Db.Storytellers.ToListAsync());
        Assert.Empty(await _target.Db.Drafts.ToListAsync());
    }

    [Fact]
    public async Task NeverDeletesSettings()
    {
        var archive = await SyncedArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.SettingsFile));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.NotNull(await _target.Db.AppSettings.FindAsync("AutoLockMinutes"));
    }

    // Task 5 changed merge mode to skip an existing setting key so a stale
    // backup can't clobber live server config; mirror mode is where
    // updating a changed setting belongs instead.
    [Fact]
    public async Task UpdatesAnExistingSettingToMatchDiskUnderMirror()
    {
        var archive = await SyncedArchiveAsync();
        var settingsFile = Path.Combine(archive, ArchiveLayout.SettingsFile);
        await File.WriteAllTextAsync(settingsFile,
            (await File.ReadAllTextAsync(settingsFile)).Replace("AutoLockMinutes: 15", "AutoLockMinutes: 30"));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var autoLock = await _target.Db.AppSettings.FindAsync("AutoLockMinutes");
        Assert.Equal("30", autoLock!.Value);
    }

    [Fact]
    public async Task LeavesSummariesOnSurvivingStories()
    {
        var archive = await SyncedArchiveAsync();
        var story = await _target.Db.Stories.FirstAsync(s => s.Title == "The Fox");
        story.SummaryText = "kept";
        story.SummaryStatus = SummaryStatus.Ready;
        await _target.Db.SaveChangesAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var after = await _target.Db.Stories.FirstAsync(s => s.Title == "The Fox");
        Assert.Equal("kept", after.SummaryText);
    }

    [Fact]
    public async Task CancellingTheConfirmationAbortsWithoutDeleting()
    {
        var archive = await SyncedArchiveAsync();
        _target.Db.Categories.Add(new Category { Name = "Extra", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();

        var result = await new VaultImporter().ImportAsync(
            _target.Db, archive, Mirror(confirm: _ => false));

        Assert.True(result.Cancelled);
        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Extra"));
    }

    [Fact]
    public async Task DoesNotPromptWhenThereIsNothingToDelete()
    {
        var archive = await SyncedArchiveAsync();
        var prompted = false;

        var result = await new VaultImporter().ImportAsync(
            _target.Db, archive, Mirror(confirm: _ => { prompted = true; return true; }));

        Assert.False(prompted);
        Assert.False(result.Cancelled);
        Assert.True(result.Deleted!.IsEmpty);
    }

    // Every test below clears the change tracker before the mirror import.
    // SyncedArchiveAsync leaves the target context warm from its own merge
    // import, and EF's fixup then attaches child collections the importer
    // never explicitly loaded — masking exactly the bugs these cover. The
    // real CLI builds a fresh FabulisDbContext per invocation
    // (src/Fabulis.Cli/Program.cs), so it always takes the cold path.

    [Fact]
    public async Task DoesNotDuplicateMessagesWhenUpdatingFromAColdContext()
    {
        var archive = await SyncedArchiveAsync();
        _target.Db.ChangeTracker.Clear();

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        _target.Db.ChangeTracker.Clear();
        Assert.Equal(2, await _target.Db.StoryMessages.CountAsync());
        Assert.Equal(2, await _target.Db.PromptMessages.CountAsync());
        Assert.Equal(1, await _target.Db.DraftMessages.CountAsync());
        Assert.Equal([0, 1], await _target.Db.StoryMessages
            .OrderBy(m => m.SortOrder).Select(m => m.SortOrder).ToListAsync());
    }

    /// <summary>
    /// Seeds a vault whose names cannot survive a round trip verbatim: a
    /// category with a slash and a story with a trailing space both get
    /// rewritten by <see cref="ArchiveLayout.Sanitize"/> on the way to disk.
    /// </summary>
    private static async Task SeedAwkwardlyNamedVaultAsync(FabulisDbContext db)
    {
        var storyteller = new Storyteller
        {
            Name = "Storyteller",
            Prompt = "Tell stories.",
            TitlingPrompt = "Title it.",
            ModelName = "anthropic/claude-sonnet-4",
            CreatedAt = VaultFixture.Epoch,
        };
        db.Storytellers.Add(storyteller);

        var category = new Category { Name = "Sci-Fi/Fantasy", CreatedAt = VaultFixture.Epoch };
        db.Categories.Add(category);

        var story = new Story
        {
            Title = "The Hare ",
            CreatedAt = VaultFixture.Epoch,
            Category = category,
            SummaryText = "kept",
            SummaryStatus = SummaryStatus.Ready,
        };
        var version = new StoryVersion
        {
            VersionNumber = 1,
            ModelName = "anthropic/claude-sonnet-4",
            CreatedAt = VaultFixture.Epoch,
            Story = story,
        };
        version.Messages.Add(new StoryMessage
        {
            Role = MessageRole.Prompt, Content = "Write about a hare.", SortOrder = 0,
        });
        story.Versions.Add(version);
        category.Stories.Add(story);

        var prompt = new Prompt
        {
            Title = "Cold open",
            CreatedAt = VaultFixture.Epoch,
            UpdatedAt = VaultFixture.Epoch,
            Category = category,
        };
        prompt.Messages.Add(new PromptMessage { Content = "Start in motion.", SortOrder = 0 });
        category.Prompts.Add(prompt);

        category.OneLiners.Add(new OneLiner
        {
            Text = "The hare did not wait.", CreatedAt = VaultFixture.Epoch, UpdatedAt = VaultFixture.Epoch,
        });
        category.Tropes.Add(new Trope
        {
            Text = "Reluctant mentor", CreatedAt = VaultFixture.Epoch, UpdatedAt = VaultFixture.Epoch,
        });

        var draft = new Draft
        {
            Storyteller = storyteller,
            Title = "A Night In The Fens",
            CreatedAt = VaultFixture.Epoch,
            UpdatedAt = VaultFixture.Epoch,
        };
        draft.Messages.Add(new DraftMessage
        {
            Role = MessageRole.Prompt, Content = "Set the scene.", SortOrder = 0,
        });
        db.Drafts.Add(draft);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MirroringAFreshExportBackIntoTheSameVaultDeletesNothing()
    {
        await SeedAwkwardlyNamedVaultAsync(_target.Db);
        var archive = _target.TempDir();
        await new VaultExporter().ExportAsync(_target.Db, archive);
        _target.Db.ChangeTracker.Clear();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.True(result.Deleted!.IsEmpty, "planned deletions: " + Describe(result.Deleted));

        _target.Db.ChangeTracker.Clear();
        Assert.Equal(["Sci-Fi/Fantasy"], await _target.Db.Categories.Select(c => c.Name).ToListAsync());
        var story = await _target.Db.Stories.SingleAsync();
        Assert.Equal("The Hare ", story.Title);
        // The summary is not in the archive, so a delete-and-recreate under a
        // mangled name would silently destroy it.
        Assert.Equal("kept", story.SummaryText);
        Assert.Equal(SummaryStatus.Ready, story.SummaryStatus);
        Assert.Equal(1, await _target.Db.StoryVersions.CountAsync());
        Assert.Equal(1, await _target.Db.Prompts.CountAsync());
        Assert.Equal(1, await _target.Db.OneLiners.CountAsync());
        Assert.Equal(1, await _target.Db.Tropes.CountAsync());
        Assert.Equal(1, await _target.Db.Drafts.CountAsync());
        Assert.Equal(1, await _target.Db.Storytellers.CountAsync());
    }

    [Fact]
    public async Task RefusesToMirrorWhenTwoRowsClaimTheSameOnDiskName()
    {
        var archive = await SyncedArchiveAsync();
        // "Fables/" sanitizes to "Fables", so the directory library/Fables
        // could belong to either row and the archive says nothing about which.
        _target.Db.Categories.Add(new Category { Name = "Fables/", CreatedAt = VaultFixture.Epoch });
        _target.Db.Categories.Add(new Category { Name = "Extra", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();
        _target.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new VaultImporter().ImportAsync(_target.Db, archive, Mirror()));

        Assert.Contains("Fables", ex.Message);
        Assert.Contains("Rename", ex.Message);

        // Nothing was deleted: not the ambiguous pair, and not "Extra",
        // which a completed run would have removed.
        _target.Db.ChangeTracker.Clear();
        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Extra"));
        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Fables/"));
        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Fables"));
        Assert.Equal(1, await _target.Db.StoryVersions.CountAsync());
    }

    [Fact]
    public async Task AMalformedDraftFileSuppressesEveryDeletion()
    {
        var archive = await SyncedArchiveAsync();
        var draftFile = Directory.GetFiles(Path.Combine(archive, ArchiveLayout.DraftsDir)).Single();
        // Front matter that opens and never closes: unreadable, not absent.
        await File.WriteAllTextAsync(draftFile, "---\nstoryteller: Storyteller\n");

        // Everything below would be deleted by a run that trusted this archive.
        _target.Db.Categories.Add(new Category { Name = "Extra", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();
        _target.Db.ChangeTracker.Clear();

        var prompted = false;
        var result = await new VaultImporter().ImportAsync(
            _target.Db, archive, Mirror(confirm: _ => { prompted = true; return true; }));

        Assert.False(prompted);
        Assert.True(result.Deleted!.IsEmpty, "planned deletions: " + Describe(result.Deleted));

        _target.Db.ChangeTracker.Clear();
        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Extra"));
        // The draft whose file could not be read survives too.
        Assert.Equal(1, await _target.Db.Drafts.CountAsync());
        Assert.Equal(1, await _target.Db.Storytellers.CountAsync());
    }

    [Fact]
    public async Task ClearingAListIsPlannedAndConfirmedLikeAnyOtherDeletion()
    {
        var archive = await SyncedArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Openers",
            ArchiveLayout.OneLinersFile));
        _target.Db.ChangeTracker.Clear();

        MirrorPlan? seenPlan = null;
        var result = await new VaultImporter().ImportAsync(
            _target.Db, archive, Mirror(confirm: plan => { seenPlan = plan; return false; }));

        Assert.NotNull(seenPlan);
        Assert.Contains(ArchiveLayout.OneLinersFile, string.Join("|", seenPlan!.Lists));
        Assert.Contains("Openers", string.Join("|", seenPlan.Lists));
        Assert.True(result.Cancelled);

        // Cancelling means the list is still there.
        _target.Db.ChangeTracker.Clear();
        var openers = await _target.Db.Categories
            .Include(c => c.OneLiners).SingleAsync(c => c.Name == "Openers");
        Assert.Equal(2, openers.OneLiners.Count);
    }

    [Fact]
    public async Task PlanListsTheChildrenACategoryDeletionTakesWithIt()
    {
        var archive = await SyncedArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Fables"), recursive: true);
        _target.Db.ChangeTracker.Clear();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());
        var plan = result.Deleted!;

        Assert.Equal(["Fables"], plan.Categories);
        Assert.Contains("Fables/The Fox", plan.Stories);
        Assert.Contains("Fables/Empty Story", plan.Stories);
        Assert.Contains("Fables/The Fox/Version 1", plan.Versions);
        Assert.Contains("Fables/Cold open", plan.Prompts);

        // And the plan matches what the run actually destroyed.
        _target.Db.ChangeTracker.Clear();
        Assert.Equal(0, await _target.Db.Stories.CountAsync());
        Assert.Equal(0, await _target.Db.StoryVersions.CountAsync());
        Assert.Equal(0, await _target.Db.Prompts.CountAsync());
    }

    // The three tests below close a gap an adversarial review found: the
    // storyteller scope and the draft-storyteller key both route through
    // OnDiskName/Sanitize on every side of the comparison, but every existing
    // fixture uses the plain name "Storyteller", which needs no sanitizing —
    // so nothing exercised that code. Each test below seeds a name containing
    // '/', which ArchiveLayout.Sanitize rewrites to '-' on export, so the
    // on-disk file stem differs from the raw database name.

    [Fact]
    public async Task StorytellerNameNeedingSanitizingSurvivesMirrorRoundTrip()
    {
        var storyteller = new Storyteller
        {
            Name = "Sci-Fi/Fantasy Narrator",
            Prompt = "Tell stories.",
            TitlingPrompt = "Title it.",
            ModelName = "anthropic/claude-sonnet-4",
            CreatedAt = VaultFixture.Epoch,
        };
        _target.Db.Storytellers.Add(storyteller);
        await _target.Db.SaveChangesAsync();

        var archive = _target.TempDir();
        await new VaultExporter().ExportAsync(_target.Db, archive);
        _target.Db.ChangeTracker.Clear();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.DoesNotContain("Sci-Fi/Fantasy Narrator", result.Deleted!.Storytellers);
        _target.Db.ChangeTracker.Clear();
        Assert.True(await _target.Db.Storytellers.AnyAsync(s => s.Name == "Sci-Fi/Fantasy Narrator"));
    }

    [Fact]
    public async Task DraftOfASanitizeNeedingStorytellerSurvivesMirrorRoundTrip()
    {
        var storyteller = new Storyteller
        {
            Name = "Sci-Fi/Fantasy Narrator",
            Prompt = "Tell stories.",
            TitlingPrompt = "Title it.",
            ModelName = "anthropic/claude-sonnet-4",
            CreatedAt = VaultFixture.Epoch,
        };
        _target.Db.Storytellers.Add(storyteller);

        var draft = new Draft
        {
            Storyteller = storyteller,
            Title = "Nebula Notes",
            CreatedAt = VaultFixture.Epoch,
            UpdatedAt = VaultFixture.Epoch,
        };
        draft.Messages.Add(new DraftMessage { Role = MessageRole.Prompt, Content = "Begin.", SortOrder = 0 });
        _target.Db.Drafts.Add(draft);
        await _target.Db.SaveChangesAsync();

        var archive = _target.TempDir();
        await new VaultExporter().ExportAsync(_target.Db, archive);
        _target.Db.ChangeTracker.Clear();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        // Neither the draft nor its storyteller was ever planned for
        // deletion, and the storyteller-reference guard — which would throw
        // InvalidOperationException if it thought the storyteller was being
        // removed out from under a surviving draft — never had a reason to
        // fire.
        Assert.DoesNotContain("Nebula Notes", result.Deleted!.Drafts);
        Assert.Empty(result.Deleted!.Storytellers);

        _target.Db.ChangeTracker.Clear();
        Assert.True(await _target.Db.Drafts.AnyAsync(d => d.Title == "Nebula Notes"));
        Assert.True(await _target.Db.Storytellers.AnyAsync(s => s.Name == "Sci-Fi/Fantasy Narrator"));
    }

    [Fact]
    public async Task StorytellerNameNeedingSanitizingIsDeletedWhenGenuinelyAbsent()
    {
        // A survivor with a name that also needs sanitizing, so a predicate
        // that matched too loosely (e.g. treating "sanitizing needed" as a
        // blanket exemption from deletion) would be caught here too. Neither
        // storyteller has a draft referencing it, so the deletion guard has
        // no reason to fire either way.
        var survivor = new Storyteller
        {
            Name = "Sci-Fi/Fantasy Narrator",
            Prompt = "Tell stories.",
            TitlingPrompt = "Title it.",
            ModelName = "anthropic/claude-sonnet-4",
            CreatedAt = VaultFixture.Epoch,
        };
        var removed = new Storyteller
        {
            Name = "Horror/Thriller Voice",
            Prompt = "Tell scary stories.",
            TitlingPrompt = "Title it.",
            ModelName = "anthropic/claude-sonnet-4",
            CreatedAt = VaultFixture.Epoch,
        };
        _target.Db.Storytellers.AddRange(survivor, removed);
        await _target.Db.SaveChangesAsync();

        var archive = _target.TempDir();
        await new VaultExporter().ExportAsync(_target.Db, archive);
        // "Horror/Thriller Voice" sanitizes on export to "Horror-Thriller Voice.md".
        File.Delete(Path.Combine(archive, ArchiveLayout.StorytellersDir, "Horror-Thriller Voice.md"));
        _target.Db.ChangeTracker.Clear();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.Equal(["Horror/Thriller Voice"], result.Deleted!.Storytellers);

        _target.Db.ChangeTracker.Clear();
        Assert.False(await _target.Db.Storytellers.AnyAsync(s => s.Name == "Horror/Thriller Voice"));
        Assert.True(await _target.Db.Storytellers.AnyAsync(s => s.Name == "Sci-Fi/Fantasy Narrator"));
    }

    // A file whose front matter opens and closes but carries a line the
    // parser cannot make sense of is just as unreadable as one that never
    // closes: the fields it was supposed to carry are gone. For a draft that
    // is fatal, because a draft's identity IS its front matter — drop
    // `created` and the file describes a different draft, so the real row
    // reads as "absent from the archive" and mirror deletes it. The parser
    // warns about the bad line, so the run must be flagged incomplete for the
    // same reason an unclosed fence is.
    [Fact]
    public async Task AMalformedFrontMatterLineInADraftSuppressesEveryDeletion()
    {
        var archive = await SyncedArchiveAsync();
        var draftFile = Directory.GetFiles(Path.Combine(archive, ArchiveLayout.DraftsDir)).Single();
        // A hand-edit that loses the colon: "created <timestamp>" is neither
        // a key nor a value, so the draft's created timestamp disappears.
        await File.WriteAllTextAsync(draftFile,
            (await File.ReadAllTextAsync(draftFile)).Replace("created: ", "created "));

        // Everything below would be deleted by a run that trusted this archive.
        _target.Db.Categories.Add(new Category { Name = "Extra", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();
        _target.Db.ChangeTracker.Clear();

        var prompted = false;
        var result = await new VaultImporter().ImportAsync(
            _target.Db, archive, Mirror(confirm: _ => { prompted = true; return true; }));

        Assert.False(prompted);
        Assert.True(result.Deleted!.IsEmpty, "planned deletions: " + Describe(result.Deleted));

        _target.Db.ChangeTracker.Clear();
        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Extra"));
        // The original draft row survives, with the CreatedAt it was exported
        // with -- not deleted and recreated at import time.
        Assert.True(await _target.Db.Drafts.AnyAsync(
            d => d.Title == "A Night In The Fens" && d.CreatedAt == VaultFixture.Epoch));
    }

    private static string Describe(MirrorPlan plan) => string.Join("; ", new[]
    {
        "categories=" + string.Join(",", plan.Categories),
        "stories=" + string.Join(",", plan.Stories),
        "versions=" + string.Join(",", plan.Versions),
        "prompts=" + string.Join(",", plan.Prompts),
        "drafts=" + string.Join(",", plan.Drafts),
        "storytellers=" + string.Join(",", plan.Storytellers),
        "lists=" + string.Join(",", plan.Lists),
    });
}
