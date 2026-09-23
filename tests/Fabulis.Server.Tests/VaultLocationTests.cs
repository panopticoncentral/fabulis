using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Server.Tests;

public class VaultLocationTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "fabulis-loc-" + Guid.NewGuid().ToString("N"));
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveUsesTheEnvironmentOverrideVerbatim()
    {
        Assert.Equal(
            "/somewhere/else/vault.db",
            VaultLocation.Resolve("/somewhere/else/vault.db", "/Users/x/Library/Application Support"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveFallsBackToTheApplicationDirectoryWhenNoOverrideIsSet(string? override_)
    {
        Assert.Equal(
            Path.Combine("/Users/x/Library/Application Support", "Fabulis", "fabulis.db"),
            VaultLocation.Resolve(override_, "/Users/x/Library/Application Support"));
    }

    [Fact]
    public void ResolveRefusesToGuessWhenTheApplicationDataDirectoryIsUnknown()
    {
        // GetFolderPath returns "" when it cannot determine the user's home
        // directory. Composing a relative path from that is how the database
        // ended up somewhere unstable in the first place.
        var ex = Assert.Throws<InvalidOperationException>(() => VaultLocation.Resolve(null, ""));
        Assert.Contains("FABULIS_DB_PATH", ex.Message);
    }

    [Fact]
    public void MigrateMovesALegacyDatabaseToTheNewLocation()
    {
        var legacyDir = TempDir();
        var currentDir = TempDir();
        Directory.CreateDirectory(legacyDir);
        var legacy = Path.Combine(legacyDir, "fabulis.db");
        var current = Path.Combine(currentDir, "fabulis.db");
        File.WriteAllText(legacy, "the vault");

        Assert.True(VaultLocation.MigrateLegacyDatabase(legacy, current));

        Assert.Equal("the vault", File.ReadAllText(current));
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public void MigrateBringsTheWriteAheadLogSidecarsAlong()
    {
        var legacyDir = TempDir();
        var currentDir = TempDir();
        Directory.CreateDirectory(legacyDir);
        var legacy = Path.Combine(legacyDir, "fabulis.db");
        var current = Path.Combine(currentDir, "fabulis.db");
        File.WriteAllText(legacy, "the vault");
        File.WriteAllText(legacy + "-wal", "pending writes");
        File.WriteAllText(legacy + "-shm", "shared memory");

        VaultLocation.MigrateLegacyDatabase(legacy, current);

        Assert.Equal("pending writes", File.ReadAllText(current + "-wal"));
        Assert.Equal("shared memory", File.ReadAllText(current + "-shm"));
        Assert.False(File.Exists(legacy + "-wal"));
        Assert.False(File.Exists(legacy + "-shm"));
    }

    [Fact]
    public void MigrateLeavesAnExistingVaultAlone()
    {
        var legacyDir = TempDir();
        var currentDir = TempDir();
        Directory.CreateDirectory(legacyDir);
        Directory.CreateDirectory(currentDir);
        var legacy = Path.Combine(legacyDir, "fabulis.db");
        var current = Path.Combine(currentDir, "fabulis.db");
        File.WriteAllText(legacy, "stale copy");
        File.WriteAllText(current, "the real vault");

        Assert.False(VaultLocation.MigrateLegacyDatabase(legacy, current));

        Assert.Equal("the real vault", File.ReadAllText(current));
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public void MigrateDoesNothingWhenThereIsNoLegacyDatabase()
    {
        var legacyDir = TempDir();
        var currentDir = TempDir();
        var legacy = Path.Combine(legacyDir, "fabulis.db");
        var current = Path.Combine(currentDir, "fabulis.db");

        Assert.False(VaultLocation.MigrateLegacyDatabase(legacy, current));

        Assert.False(File.Exists(current));
    }
}
