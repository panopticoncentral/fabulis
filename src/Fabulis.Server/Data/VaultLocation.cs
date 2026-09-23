namespace Fabulis.Server.Data;

/// <summary>
/// Where the encrypted vault lives. The database used to sit under the
/// server's build output, so a <c>dotnet clean</c> or a Release build lost it;
/// it now lives in the user's application-data directory, outside the repo.
/// The server and the CLI both resolve it through here so the two can never
/// drift apart again.
/// </summary>
public static class VaultLocation
{
    public const string PathEnvironmentVariable = "FABULIS_DB_PATH";

    private const string DatabaseFileName = "fabulis.db";
    private const string ApplicationDirectoryName = "Fabulis";

    /// <summary>SQLite's sidecars, which have to travel with the database.</summary>
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm"];

    /// <summary>
    /// The vault path for this machine: <see cref="PathEnvironmentVariable"/>
    /// if it is set, otherwise the default under the application-data
    /// directory. The file itself need not exist yet.
    /// </summary>
    public static string DatabasePath => Resolve(
        Environment.GetEnvironmentVariable(PathEnvironmentVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>The legacy path: a <c>data/</c> directory beside the running assembly.</summary>
    public static string LegacyDatabasePath =>
        Path.Combine(AppContext.BaseDirectory, "data", DatabaseFileName);

    public static string Resolve(string? pathOverride, string localApplicationData)
    {
        if (!string.IsNullOrWhiteSpace(pathOverride))
            return pathOverride;

        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException(
                "Could not determine the application-data directory, so there is no safe " +
                $"default location for the vault. Set {PathEnvironmentVariable} to the " +
                "database file to say where it lives.");

        return Path.Combine(localApplicationData, ApplicationDirectoryName, DatabaseFileName);
    }

    /// <summary>
    /// Moves a database left at <paramref name="legacyPath"/> to
    /// <paramref name="currentPath"/>, once. Does nothing if there is nothing
    /// to move or if a vault already exists at the destination — an existing
    /// vault is never overwritten.
    /// </summary>
    /// <returns>Whether a database was moved.</returns>
    public static bool MigrateLegacyDatabase(string legacyPath, string currentPath)
    {
        if (File.Exists(currentPath) || !File.Exists(legacyPath))
            return false;

        var currentDir = Path.GetDirectoryName(currentPath);
        if (!string.IsNullOrEmpty(currentDir))
            Directory.CreateDirectory(currentDir);

        File.Move(legacyPath, currentPath);

        foreach (var suffix in SidecarSuffixes)
        {
            if (File.Exists(legacyPath + suffix))
                File.Move(legacyPath + suffix, currentPath + suffix, overwrite: true);
        }

        return true;
    }
}
