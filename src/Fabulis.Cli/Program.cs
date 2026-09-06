using Fabulis.Cli;
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var (positionals, flags) = ArgParsing.Parse(args);

var unknown = flags.Except(ArgParsing.KnownFlags).ToList();
if (unknown.Count > 0)
{
    Console.Error.WriteLine($"error: unknown flag(s): {string.Join(", ", unknown)}");
    PrintUsage();
    return 1;
}

if (positionals.Count == 0)
{
    PrintUsage();
    return 1;
}

var command = positionals[0];

if (ArgParsing.FlagsByVerb.ContainsKey(command))
{
    var invalidForVerb = ArgParsing.InvalidFlagsForVerb(command, flags);
    if (invalidForVerb.Count > 0)
    {
        var verbAgreement = invalidForVerb.Count == 1 ? "is" : "are";
        Console.Error.WriteLine(
            $"error: {string.Join(", ", invalidForVerb)} {verbAgreement} not valid for '{command}'");
        PrintUsage();
        return 1;
    }
}

try
{
    switch (command)
    {
        case "export":
            if (positionals.Count < 2) { PrintUsage(); return 1; }
            return await RunVaultCommandAsync(
                "export", positionals[1],
                includeSecrets: flags.Contains("--include-secrets"),
                mirror: false,
                assumeYes: false);

        case "import":
            if (positionals.Count < 2) { PrintUsage(); return 1; }
            return await RunVaultCommandAsync(
                "import", positionals[1],
                includeSecrets: false,
                mirror: flags.Contains("--mirror"),
                assumeYes: flags.Contains("--yes"));

        case "sillytavern":
            if (positionals.Count < 3) { PrintUsage(); return 1; }
            return await RunSillyTavernAsync(positionals[1], positionals[2]);

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

static async Task<int> RunVaultCommandAsync(
    string command, string path, bool includeSecrets, bool mirror, bool assumeYes)
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
    optionsBuilder.UseSqlite(
        $"Data Source={dbPath};Password={password}",
        sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

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
        var result = await new VaultExporter().ExportAsync(db, path, includeSecrets);
        Console.WriteLine(
            $"Exported: {result.Categories} categories, {result.Stories} stories, " +
            $"{result.Versions} versions, {result.Prompts} prompts, " +
            $"{result.OneLiners} one-liners, {result.Tropes} tropes, " +
            $"{result.Drafts} drafts, {result.Storytellers} storytellers");
        return 0;
    }
    else
    {
        var options = new ImportOptions(
            Mirror: mirror,
            ConfirmDeletions: plan =>
            {
                // Print the itemized list regardless of --yes: --yes skips
                // the PROMPT, not the RECORD of what is about to be
                // destroyed. Without this, a scripted `--mirror --yes` run
                // deletes rows with nothing printed anywhere to show what
                // was lost.
                Console.WriteLine("--mirror will delete the following from the vault:");
                foreach (var name in plan.Categories) Console.WriteLine($"  category    {name}");
                foreach (var name in plan.Stories) Console.WriteLine($"  story       {name}");
                foreach (var name in plan.Versions) Console.WriteLine($"  version     {name}");
                foreach (var name in plan.Prompts) Console.WriteLine($"  prompt      {name}");
                foreach (var name in plan.Drafts) Console.WriteLine($"  draft       {name}");
                foreach (var name in plan.Storytellers) Console.WriteLine($"  storyteller {name}");
                foreach (var name in plan.Lists) Console.WriteLine($"  list        {name}");

                if (assumeYes)
                {
                    Console.WriteLine($"Deleting {plan.Count} item(s) (--yes given, not prompting).");
                    return true;
                }

                Console.Write($"Delete {plan.Count} item(s)? [y/N] ");

                var answer = Console.ReadLine();
                return string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
            });

        var result = await new VaultImporter().ImportAsync(db, path, options);

        if (result.Cancelled)
        {
            Console.WriteLine("Cancelled; nothing was changed.");
            return 1;
        }

        // result.Deleted is non-null only when --mirror ran, so a plain
        // merge import's summary line is unchanged. When it did run, the
        // deleted count is always reported, even when it is zero, so a
        // scripted `--mirror --yes` run leaves a record of what mirroring
        // did in this run rather than only what it added.
        var deletedSuffix = result.Deleted is { } deleted
            ? $", {deleted.Count} deleted (--mirror)"
            : "";

        Console.WriteLine(
            $"Imported: {result.CategoriesCreated} categories, {result.StoriesCreated} stories, " +
            $"{result.VersionsCreated} versions, {result.PromptsCreated} prompts, " +
            $"{result.OneLinersCreated} one-liners, {result.TropesCreated} tropes, " +
            $"{result.DraftsCreated} drafts, {result.StorytellersCreated} storytellers, " +
            $"{result.SettingsApplied} settings{deletedSuffix}");
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
    Console.Error.WriteLine("  export <destination> [--include-secrets]");
    Console.Error.WriteLine("      Write the whole vault to a directory tree (must not exist).");
    Console.Error.WriteLine("      --include-secrets also writes the OpenRouter API key, which is");
    Console.Error.WriteLine("      otherwise omitted.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  import <source> [--mirror] [--yes]");
    Console.Error.WriteLine("      Read an archive directory (one containing fabulis.md) into the");
    Console.Error.WriteLine("      vault. Additive and idempotent by default. --mirror makes the");
    Console.Error.WriteLine("      vault match the directory exactly, deleting what the archive");
    Console.Error.WriteLine("      does not contain, after confirmation. --yes skips that");
    Console.Error.WriteLine("      confirmation.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  sillytavern <source> <destination>");
    Console.Error.WriteLine("      Convert a directory of SillyTavern .jsonl chat files into");
    Console.Error.WriteLine($"      Fabulis draft markdown files, written to <destination>/{Fabulis.Cli.Archive.ArchiveLayout.DraftsDir}/");
    Console.Error.WriteLine("      along with a manifest, so <destination> is itself a valid");
    Console.Error.WriteLine("      archive you can review and then run `import` on. Does not");
    Console.Error.WriteLine("      touch the vault.");
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
