using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Cli.Tests;

public class SearchImportTests
{
    [Fact]
    public async Task ImportRerunAndMirrorKeepAnExistingSearchIndexConsistent()
    {
        using var source = new VaultFixture();
        using var target = new VaultFixture();
        await source.SeedFullVaultAsync();
        var archive = source.TempDir();
        await new VaultExporter().ExportAsync(source.Db, archive);
        await target.Db.EnsureSchemaUpdatedAsync();
        await new VaultImporter().ImportAsync(target.Db, archive);
        var first = await SearchService.SearchAsync(target.Db, "fox");
        Assert.Contains(first.Results, r => r.Kind == "storyVersion");
        await new VaultImporter().ImportAsync(target.Db, archive);
        Assert.Equal(first.Results, (await SearchService.SearchAsync(target.Db, "fox")).Results);

        File.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "The Fox", "Version 1.md"));
        await new VaultImporter().ImportAsync(target.Db, archive,
            new ImportOptions(Mirror: true, ConfirmDeletions: _ => true));
        Assert.DoesNotContain((await SearchService.SearchAsync(target.Db, "fox")).Results,
            r => r.Kind == "storyVersion");
    }
}
