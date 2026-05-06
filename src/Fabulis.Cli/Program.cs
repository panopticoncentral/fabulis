using Fabulis.Cli;
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

if (args.Length < 2)
{
    PrintUsage();
    return 1;
}

var command = args[0];
var path = args[1];

if (command is not ("export" or "import"))
{
    PrintUsage();
    return 1;
}

string dbPath;
try
{
    dbPath = ResolveDatabasePath();
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

var password = PasswordPrompt.Read("Vault password: ");
if (string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("error: no password provided");
    return 1;
}

var optionsBuilder = new DbContextOptionsBuilder<FabulisDbContext>();
optionsBuilder.UseSqlite($"Data Source={dbPath};Password={password}");

await using var db = new FabulisDbContext(optionsBuilder.Options);

try
{
    // Force a connection so a wrong password fails before doing real work.
    await db.Database.OpenConnectionAsync();
}
catch (SqliteException ex)
{
    Console.Error.WriteLine($"error: could not open vault ({ex.Message})");
    return 1;
}

try
{
    if (command == "export")
    {
        var result = await new CategoryExportService().ExportAsync(db, path);
        Console.WriteLine(
            $"Exported: {result.CategoriesExported} categories, {result.StoriesExported} stories, " +
            $"{result.VersionsExported} versions, {result.DraftsExported} drafts");
        return 0;
    }
    else
    {
        var result = await new CategoryImportService().ImportAsync(db, path);
        Console.WriteLine(
            $"Imported: {result.CategoriesCreated} categories, {result.StoriesCreated} stories, " +
            $"{result.VersionsCreated} versions, {result.DraftsCreated} drafts");
        return 0;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("usage: fabulis-cli <export|import> <path>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  export <destination>   Write the vault to a directory tree (must not exist).");
    Console.Error.WriteLine("  import <source>        Read a directory tree of categories (and optional");
    Console.Error.WriteLine("                         _drafts/) into the vault. Idempotent.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Database location:");
    Console.Error.WriteLine("  Set FABULIS_DB_PATH to point at the SQLCipher .db file. If unset, the CLI");
    Console.Error.WriteLine("  walks up from its own directory looking for Fabulis.slnx and uses");
    Console.Error.WriteLine("  src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db.");
}

static string ResolveDatabasePath()
{
    var fromEnv = Environment.GetEnvironmentVariable("FABULIS_DB_PATH");
    if (!string.IsNullOrEmpty(fromEnv))
    {
        if (!File.Exists(fromEnv))
            throw new FileNotFoundException($"FABULIS_DB_PATH points at a non-existent file: {fromEnv}");
        return fromEnv;
    }

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Fabulis.slnx")))
        dir = dir.Parent;

    if (dir is null)
        throw new FileNotFoundException(
            "Could not locate Fabulis.slnx by walking up from the CLI directory. " +
            "Set FABULIS_DB_PATH to point at the database file.");

    var candidate = Path.Combine(
        dir.FullName, "src", "Fabulis.Server", "bin", "Debug", "net10.0", "data", "fabulis.db");

    if (!File.Exists(candidate))
        throw new FileNotFoundException(
            $"Database not found at the default location: {candidate}. " +
            "Build and run the server at least once, or set FABULIS_DB_PATH.");

    return candidate;
}
