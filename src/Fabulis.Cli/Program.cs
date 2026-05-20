using Fabulis.Cli;
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];

try
{
    switch (command)
    {
        case "export":
            if (args.Length < 2) { PrintUsage(); return 1; }
            return await RunVaultCommandAsync("export", args[1]);

        case "import":
            if (args.Length < 2) { PrintUsage(); return 1; }
            return await RunVaultCommandAsync("import", args[1]);

        case "sillytavern":
            if (args.Length < 3) { PrintUsage(); return 1; }
            return await RunSillyTavernAsync(args[1], args[2]);

        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static async Task<int> RunVaultCommandAsync(string command, string path)
{
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
        await db.Database.OpenConnectionAsync();
    }
    catch (SqliteException ex)
    {
        Console.Error.WriteLine($"error: could not open vault ({ex.Message})");
        return 1;
    }

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

static async Task<int> RunSillyTavernAsync(string sourcePath, string destPath)
{
    var result = await new SillyTavernConvertService().ConvertAsync(sourcePath, destPath);
    Console.WriteLine(
        $"Converted: {result.DraftsWritten} drafts written, " +
        $"{result.FilesSkipped} skipped, {result.FilesFailed} failed");
    return 0;
}

static void PrintUsage()
{
    Console.Error.WriteLine("usage: fabulis-cli <verb> <args...>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  export <destination>");
    Console.Error.WriteLine("      Write the vault to a directory tree (must not exist).");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  import <source>");
    Console.Error.WriteLine("      Read a directory tree of categories (and optional _drafts/)");
    Console.Error.WriteLine("      into the vault. <source> may also be a single category or a");
    Console.Error.WriteLine("      folder named _drafts. Idempotent.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  sillytavern <source> <destination>");
    Console.Error.WriteLine("      Convert a directory of SillyTavern .jsonl chat files into");
    Console.Error.WriteLine("      Fabulis draft markdown files, written to <destination>/_drafts/");
    Console.Error.WriteLine("      for manual review before import. Does not touch the vault.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Database location (export/import only):");
    Console.Error.WriteLine("  Set FABULIS_DB_PATH to point at the SQLCipher .db file. If unset,");
    Console.Error.WriteLine("  the CLI walks up from its own directory looking for Fabulis.slnx");
    Console.Error.WriteLine("  and uses src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db.");
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
