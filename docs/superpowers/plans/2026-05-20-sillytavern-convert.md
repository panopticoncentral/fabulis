# SillyTavern → Draft Markdown Conversion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `sillytavern` verb to `Fabulis.Cli` that converts a directory of SillyTavern `.jsonl` chat files into a directory of Fabulis draft `.md` files (under a `_drafts/` subdirectory), ready for the existing `import` verb after a manual review pass.

**Architecture:** New `SillyTavernConvertService` does the conversion. Existing draft-markdown formatting is extracted from `CategoryExportService` into a new shared `DraftMarkdownWriter` so both verbs go through one writer. `Program.cs` is refactored so DB/password setup is per-verb (the new verb does neither).

**Tech Stack:** .NET 10, top-level statements, `System.Text.Json` (`JsonDocument`), `System.Text.RegularExpressions` (`[GeneratedRegex]`), existing `MessageRole` enum.

**Spec:** `docs/superpowers/specs/2026-05-20-sillytavern-convert-design.md`

**Note on testing:** `Fabulis.Cli` has no test project (per spec). Each implementation task ends with a `dotnet build` check; the final task is end-to-end verification using the user's real SillyTavern sample file at `/Volumes/Untitled/AI/SillyTavern/data/default-user/chats/StoryTeller/StoryTeller - 2025-08-02@22h41m55s.jsonl`.

---

## File Structure

- **Create** `src/Fabulis.Cli/DraftMarkdownWriter.cs` — pure static formatter shared by `CategoryExportService` and `SillyTavernConvertService`.
- **Modify** `src/Fabulis.Cli/CategoryExportService.cs` — delete the private `FormatDraft`/`FormatConversation` and call `DraftMarkdownWriter` instead.
- **Create** `src/Fabulis.Cli/SillyTavernConvertService.cs` — file enumeration, jsonl parsing, header derivation, title sanitization, file emission.
- **Modify** `src/Fabulis.Cli/Program.cs` — refactor dispatch; add `sillytavern` verb; skip DB/password setup when it's the active verb.
- **Modify** `src/Fabulis.Cli/README.md` — document the new verb.

No new dependencies, no project structural changes, no DB schema changes.

---

### Task 1: Extract `DraftMarkdownWriter` from `CategoryExportService`

Pure refactor. The export verb continues to work; no behavioral change.

**Files:**
- Create: `src/Fabulis.Cli/DraftMarkdownWriter.cs`
- Modify: `src/Fabulis.Cli/CategoryExportService.cs`

- [ ] **Step 1: Create `src/Fabulis.Cli/DraftMarkdownWriter.cs`**

Exact contents:

```csharp
using System.Text;
using Fabulis.Server.Data;

namespace Fabulis.Cli;

/// <summary>
/// Single source of truth for the on-disk draft markdown shape used by
/// the export and sillytavern verbs.
/// </summary>
internal static class DraftMarkdownWriter
{
    public static string FormatDraft(
        string storytellerName,
        string modelName,
        DateTime createdUtc,
        DateTime updatedUtc,
        IEnumerable<(MessageRole Role, string Content, int SortOrder)> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Storyteller: {storytellerName}");
        sb.AppendLine($"Model: {modelName}");
        sb.AppendLine($"Created: {createdUtc:O}");
        sb.AppendLine($"Updated: {updatedUtc:O}");
        sb.AppendLine();
        sb.Append(FormatConversation(messages));
        return sb.ToString();
    }

    public static string FormatConversation(
        IEnumerable<(MessageRole Role, string Content, int SortOrder)> messages)
    {
        var ordered = messages.OrderBy(m => m.SortOrder).ToList();
        var sb = new StringBuilder();

        foreach (var message in ordered)
        {
            var label = message.Role switch
            {
                MessageRole.Prompt => "**Me:**",
                MessageRole.Response => "**StoryTeller:**",
                _ => throw new InvalidOperationException($"Unknown role: {message.Role}")
            };

            sb.AppendLine(label);
            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: In `src/Fabulis.Cli/CategoryExportService.cs`, delete the private `FormatDraft` method**

Delete the entire method (currently the one starting `private static string FormatDraft(Draft draft)`). Also delete the `private static string FormatConversation(...)` method directly below it.

- [ ] **Step 3: Update the version-file write site to call `DraftMarkdownWriter.FormatConversation`**

In `CategoryExportService.ExportAsync`, find the `foreach (var version in exportableVersions)` block. Replace the line:

```csharp
                    var content = FormatConversation(
                        version.Messages.Select(m => (m.Role, m.Content, m.SortOrder)));
```

with:

```csharp
                    var content = DraftMarkdownWriter.FormatConversation(
                        version.Messages.Select(m => (m.Role, m.Content, m.SortOrder)));
```

- [ ] **Step 4: Update the draft-file write site to call `DraftMarkdownWriter.FormatDraft`**

In `CategoryExportService.ExportAsync`, find the `foreach (var draft in exportableDrafts)` block. Replace the line:

```csharp
                var content = FormatDraft(draft);
```

with:

```csharp
                var storytellerName = draft.Storyteller?.Name ?? "(unknown)";
                var modelName = draft.Storyteller?.ModelName ?? "(unknown)";
                var createdUtc = DateTime.SpecifyKind(draft.CreatedAt, DateTimeKind.Utc);
                var updatedUtc = DateTime.SpecifyKind(draft.UpdatedAt, DateTimeKind.Utc);
                var content = DraftMarkdownWriter.FormatDraft(
                    storytellerName, modelName, createdUtc, updatedUtc,
                    draft.Messages.Select(m => (m.Role, m.Content, m.SortOrder)));
```

- [ ] **Step 5: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds. The export verb's behavior is unchanged because the new helper produces byte-identical output to what `CategoryExportService.FormatDraft` produced.

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Cli/DraftMarkdownWriter.cs src/Fabulis.Cli/CategoryExportService.cs
git commit -m "Extract DraftMarkdownWriter helper from CategoryExportService

Pure refactor. Two private formatters (FormatDraft, FormatConversation)
move to a new shared DraftMarkdownWriter so the upcoming sillytavern
verb has one source of truth for the on-disk draft shape."
```

---

### Task 2: Refactor `Program.cs` dispatch; add `sillytavern` verb wired to a stub

The new verb takes two arguments (source + dest) and skips DB/password setup. After this task, `dotnet run --project src/Fabulis.Cli -- sillytavern <src> <dst>` runs but immediately exits with "not implemented yet".

**Files:**
- Modify: `src/Fabulis.Cli/Program.cs`
- Create: `src/Fabulis.Cli/SillyTavernConvertService.cs` (stub)

- [ ] **Step 1: Create stub `src/Fabulis.Cli/SillyTavernConvertService.cs`**

Exact contents:

```csharp
namespace Fabulis.Cli;

public class SillyTavernConvertService
{
    public Task<ConvertResult> ConvertAsync(string sourcePath, string destPath)
    {
        throw new NotImplementedException(
            "SillyTavernConvertService.ConvertAsync is not implemented yet.");
    }
}

public class ConvertResult
{
    public int DraftsWritten { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }
}
```

- [ ] **Step 2: Replace `src/Fabulis.Cli/Program.cs` with the dispatching version**

Replace the entire file with:

```csharp
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
```

The diff against the previous `Program.cs`:
- The top-level `if/else` is now a `switch` over `command`.
- DB resolution, password prompt, and `DbContext` setup move into `RunVaultCommandAsync`. The two existing verbs share it; `sillytavern` does not call it.
- `RunSillyTavernAsync` calls into the stub service and prints the summary line.
- `PrintUsage` is expanded to describe all three verbs and notes that the DB-path env var is only relevant to the two vault verbs.
- A top-level `try/catch` keeps the existing behavior of "unexpected exception → exit 1 with `error: ...` to stderr".

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Verify the new dispatch path runs and the stub throws as expected**

```bash
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/does-not-matter /tmp/also-does-not-matter
```

Expected: prints `error: SillyTavernConvertService.ConvertAsync is not implemented yet.` to stderr and exits with code 1.

- [ ] **Step 5: Verify the existing verbs still print usage when given no args**

```bash
dotnet run --project src/Fabulis.Cli
```

Expected: prints the usage block listing all three verbs to stderr and exits with code 1.

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Cli/Program.cs src/Fabulis.Cli/SillyTavernConvertService.cs
git commit -m "Wire up 'sillytavern' verb dispatch with a stub service

Refactors Program.cs to dispatch by verb before opening the vault, so
the new verb skips DB resolution and the password prompt. The verb
currently throws NotImplementedException; the conversion logic lands
in subsequent commits."
```

---

### Task 3: Preconditions and `<dest>/_drafts/` setup in `SillyTavernConvertService`

After this task, the verb errors out cleanly for the three precondition violations (`<source>` missing, `<dest>` exists, no `*.jsonl` files) and produces an empty `<dest>/_drafts/` directory for a valid source.

**Files:**
- Modify: `src/Fabulis.Cli/SillyTavernConvertService.cs`

- [ ] **Step 1: Replace the stub with the preconditions-only implementation**

Replace the entire contents of `src/Fabulis.Cli/SillyTavernConvertService.cs` with:

```csharp
namespace Fabulis.Cli;

public class SillyTavernConvertService
{
    public async Task<ConvertResult> ConvertAsync(string sourcePath, string destPath)
    {
        var source = new DirectoryInfo(sourcePath);
        if (!source.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");

        if (Directory.Exists(destPath) || File.Exists(destPath))
            throw new IOException($"Destination already exists: {destPath}");

        var jsonlFiles = source.GetFiles("*.jsonl").OrderBy(f => f.Name).ToArray();
        if (jsonlFiles.Length == 0)
            throw new InvalidOperationException(
                $"No .jsonl files found in '{sourcePath}'.");

        var draftsDir = Path.Combine(destPath, "_drafts");
        Directory.CreateDirectory(draftsDir);

        var result = new ConvertResult();
        foreach (var file in jsonlFiles)
        {
            // Conversion logic lands in Tasks 4-6.
            _ = file;
        }

        await Task.CompletedTask;
        return result;
    }
}

public class ConvertResult
{
    public int DraftsWritten { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Verify the missing-source precondition**

```bash
rm -rf /tmp/st-test-dest
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/does-not-exist /tmp/st-test-dest
```

Expected: `error: Source directory not found: /tmp/does-not-exist` on stderr, exit 1, no `/tmp/st-test-dest` created.

- [ ] **Step 4: Verify the dest-exists precondition**

```bash
mkdir -p /tmp/st-existing-dest /tmp/st-empty-source
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-empty-source /tmp/st-existing-dest
```

Expected: `error: Destination already exists: /tmp/st-existing-dest` on stderr, exit 1.

- [ ] **Step 5: Verify the no-jsonl precondition**

```bash
rm -rf /tmp/st-test-dest
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-empty-source /tmp/st-test-dest
```

Expected: `error: No .jsonl files found in '/tmp/st-empty-source'.` on stderr, exit 1, no `/tmp/st-test-dest` created.

- [ ] **Step 6: Verify the happy-path skeleton**

```bash
rm -rf /tmp/st-test-dest
mkdir -p /tmp/st-source-with-jsonl
touch /tmp/st-source-with-jsonl/empty.jsonl
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-source-with-jsonl /tmp/st-test-dest
ls /tmp/st-test-dest/
```

Expected: `Converted: 0 drafts written, 0 skipped, 0 failed` on stdout, exit 0, and `/tmp/st-test-dest/_drafts/` exists and is empty.

- [ ] **Step 7: Cleanup test directories**

```bash
rm -rf /tmp/st-test-dest /tmp/st-existing-dest /tmp/st-empty-source /tmp/st-source-with-jsonl
```

- [ ] **Step 8: Commit**

```bash
git add src/Fabulis.Cli/SillyTavernConvertService.cs
git commit -m "SillyTavernConvertService: preconditions and _drafts setup

Errors out cleanly for missing source, existing destination, or a
source with no .jsonl files. On a valid source, creates an empty
<dest>/_drafts/ directory and exits with all-zero counts."
```

---

### Task 4: Parse `.jsonl` files into an internal turn list

After this task, every line of every input file is read and classified. Malformed JSON lines produce a stderr warning. Files that fail to open or contain no parseable lines bump `FilesFailed`. No `.md` files are written yet.

**Files:**
- Modify: `src/Fabulis.Cli/SillyTavernConvertService.cs`

- [ ] **Step 1: Replace `src/Fabulis.Cli/SillyTavernConvertService.cs` with the parsing version**

Exact contents:

```csharp
using System.Globalization;
using System.Text.Json;

namespace Fabulis.Cli;

public class SillyTavernConvertService
{
    public async Task<ConvertResult> ConvertAsync(string sourcePath, string destPath)
    {
        var source = new DirectoryInfo(sourcePath);
        if (!source.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");

        if (Directory.Exists(destPath) || File.Exists(destPath))
            throw new IOException($"Destination already exists: {destPath}");

        var jsonlFiles = source.GetFiles("*.jsonl").OrderBy(f => f.Name).ToArray();
        if (jsonlFiles.Length == 0)
            throw new InvalidOperationException(
                $"No .jsonl files found in '{sourcePath}'.");

        var draftsDir = Path.Combine(destPath, "_drafts");
        Directory.CreateDirectory(draftsDir);

        var result = new ConvertResult();
        foreach (var file in jsonlFiles)
        {
            List<ParsedTurn>? turns;
            try
            {
                turns = await ParseFileAsync(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: could not read ({ex.Message})");
                result.FilesFailed++;
                continue;
            }

            if (turns is null || turns.Count == 0)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: no conversation turns found, skipped");
                result.FilesFailed++;
                continue;
            }

            // Output (Tasks 5-6) lands here. For now, drop the turns on the floor.
            _ = turns;
        }

        return result;
    }

    private static async Task<List<ParsedTurn>?> ParseFileAsync(FileInfo file)
    {
        // Any IOException from ReadAllLinesAsync propagates up to ConvertAsync,
        // which classifies the file as Failed.
        var turns = new List<ParsedTurn>();
        var lines = await File.ReadAllLinesAsync(file.FullName);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                Console.Error.WriteLine($"warn: {file.FullName}:{i + 1}: invalid JSON, skipped");
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                // Skip the chat-header line that starts the file.
                if (root.TryGetProperty("chat_metadata", out _)) continue;

                // Skip system turns (SillyTavern internal commands).
                if (root.TryGetProperty("is_system", out var isSystemElem) &&
                    isSystemElem.ValueKind == JsonValueKind.True)
                    continue;

                if (!root.TryGetProperty("name", out var nameElem) ||
                    !root.TryGetProperty("mes", out var mesElem))
                    continue;

                var isUser = root.TryGetProperty("is_user", out var isUserElem) &&
                             isUserElem.ValueKind == JsonValueKind.True;

                DateTime? sendDate = null;
                if (root.TryGetProperty("send_date", out var dateElem) &&
                    dateElem.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(dateElem.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var parsedDate))
                {
                    sendDate = parsedDate.Kind == DateTimeKind.Utc
                        ? parsedDate
                        : parsedDate.ToUniversalTime();
                }

                string? apiModel = null;
                if (root.TryGetProperty("extra", out var extraElem) &&
                    extraElem.ValueKind == JsonValueKind.Object &&
                    extraElem.TryGetProperty("model", out var modelElem) &&
                    modelElem.ValueKind == JsonValueKind.String)
                {
                    var m = modelElem.GetString();
                    if (!string.IsNullOrWhiteSpace(m))
                        apiModel = m;
                }

                turns.Add(new ParsedTurn(
                    LineNumber: i + 1,
                    Name: nameElem.GetString() ?? "",
                    IsUser: isUser,
                    Message: mesElem.GetString() ?? "",
                    SendDate: sendDate,
                    ApiModel: apiModel));
            }
        }

        return turns;
    }

    private record ParsedTurn(
        int LineNumber,
        string Name,
        bool IsUser,
        string Message,
        DateTime? SendDate,
        string? ApiModel);
}

public class ConvertResult
{
    public int DraftsWritten { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }
}
```

Notes on the design:
- A file with zero valid turns is counted as `FilesFailed`, not `FilesSkipped`, because a `.jsonl` with no parseable conversation content is malformed input rather than valid-but-skippable. The "no user messages" / "greeting-only" classifications happen in Task 6 once the greeting-skip rule is applied.
- The `IOException` rethrow keeps the `catch` in `ConvertAsync` as the single place that classifies a file-open failure.
- `ParsedTurn` is a private nested record; it's an implementation detail of this service.

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Verify a file with only a `chat_metadata` line is reported as failed**

```bash
rm -rf /tmp/st-test-dest /tmp/st-meta-only
mkdir -p /tmp/st-meta-only
echo '{"chat_metadata":{"integrity":"abc"},"user_name":"u","character_name":"c"}' > /tmp/st-meta-only/meta.jsonl
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-meta-only /tmp/st-test-dest
```

Expected on stderr: `warn: <abs>/meta.jsonl: no conversation turns found, skipped`. On stdout: `Converted: 0 drafts written, 0 skipped, 1 failed`. Exit 0.

- [ ] **Step 4: Verify invalid-JSON lines produce a warning but don't fail the file**

```bash
rm -rf /tmp/st-test-dest /tmp/st-mixed
mkdir -p /tmp/st-mixed
cat > /tmp/st-mixed/mixed.jsonl <<'EOF'
{"chat_metadata":{"integrity":"abc"}}
not-json
{"name":"X","is_user":false,"mes":"hi","send_date":"2026-05-20T10:00:00.000Z"}
EOF
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-mixed /tmp/st-test-dest
```

Expected on stderr: a line containing `:2: invalid JSON, skipped`. On stdout: `Converted: 0 drafts written, 0 skipped, 0 failed`. Exit 0. (One valid turn was parsed; the actual write happens in Task 6.)

- [ ] **Step 5: Cleanup**

```bash
rm -rf /tmp/st-test-dest /tmp/st-meta-only /tmp/st-mixed
```

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Cli/SillyTavernConvertService.cs
git commit -m "SillyTavernConvertService: parse jsonl into typed turns

Reads each line, parses JSON, skips chat_metadata and is_system rows,
and pulls out name / is_user / mes / send_date / extra.model into a
ParsedTurn record. Malformed lines produce a stderr warning and are
dropped; files that produce zero turns count as failed."
```

---

### Task 5: Derive header fields, title, and filename

After this task, every parseable file produces a set of header values and a target filename — but they're still computed and discarded (no write yet).

**Files:**
- Modify: `src/Fabulis.Cli/SillyTavernConvertService.cs`

- [ ] **Step 1: Add `using System.Text.RegularExpressions;` at the top of the file**

In `src/Fabulis.Cli/SillyTavernConvertService.cs`, add to the existing using block so the file's `using` directives are:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
```

- [ ] **Step 2: Change the class declaration to `public partial class`**

In the same file, find:

```csharp
public class SillyTavernConvertService
```

and replace with:

```csharp
public partial class SillyTavernConvertService
```

`partial` is required because the title-sanitization helpers use `[GeneratedRegex]`, which requires a partial declaration.

- [ ] **Step 3: Add the regex declarations as private partial methods**

In `src/Fabulis.Cli/SillyTavernConvertService.cs`, immediately after the opening brace of the `SillyTavernConvertService` class (above `ConvertAsync`), add:

```csharp
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"[/\\:*?""<>|]")]
    private static partial Regex FilesystemUnsafe();

    [GeneratedRegex(@"[\p{P}\s]+$")]
    private static partial Regex TrailingPunctuation();

```

- [ ] **Step 4: Add the four derivation helpers above the `ParsedTurn` record**

In `src/Fabulis.Cli/SillyTavernConvertService.cs`, add these four private static methods immediately above the `private record ParsedTurn(...)` line, inside the class:

```csharp
    private static string? DeriveStorytellerName(List<ParsedTurn> turns, string filePath)
    {
        var storytellerNames = turns
            .Where(t => !t.IsUser && !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => t.Name)
            .ToList();

        if (storytellerNames.Count == 0)
            return null;

        var first = storytellerNames[0];
        var distinct = storytellerNames.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > 1)
        {
            Console.Error.WriteLine(
                $"warn: {filePath}: mixed storyteller names ({string.Join(", ", distinct)}), used '{first}'");
        }
        return first;
    }

    private static string DeriveModel(List<ParsedTurn> turns, string filePath)
    {
        var lastModel = turns
            .Where(t => !t.IsUser && !string.IsNullOrWhiteSpace(t.ApiModel))
            .Select(t => t.ApiModel!)
            .LastOrDefault();

        if (lastModel is null)
        {
            Console.Error.WriteLine($"warn: {filePath}: no model metadata, wrote 'Model: (unknown)'");
            return "(unknown)";
        }
        return lastModel;
    }

    private static (DateTime CreatedUtc, DateTime UpdatedUtc) DeriveTimestamps(
        List<ParsedTurn> turns, FileInfo file)
    {
        var firstSendDate = turns.Count > 0 ? turns[0].SendDate : null;
        var lastSendDate = turns.Count > 0 ? turns[^1].SendDate : null;
        var fallback = DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc);

        DateTime created;
        if (firstSendDate is not null)
        {
            created = firstSendDate.Value;
        }
        else
        {
            Console.Error.WriteLine(
                $"warn: {file.FullName}: send_date missing on first turn, used file mtime for Created");
            created = fallback;
        }

        DateTime updated;
        if (lastSendDate is not null)
        {
            updated = lastSendDate.Value;
        }
        else
        {
            Console.Error.WriteLine(
                $"warn: {file.FullName}: send_date missing on last turn, used file mtime for Updated");
            updated = fallback;
        }

        return (created, updated);
    }

    private static string DeriveTitle(List<ParsedTurn> turnsAfterGreetingSkip)
    {
        var firstUser = turnsAfterGreetingSkip.FirstOrDefault(t => t.IsUser);
        if (firstUser is null) return "Untitled";

        var collapsed = WhitespaceRun().Replace(firstUser.Message, " ").Trim();
        if (collapsed.Length == 0) return "Untitled";

        const int Max = 60;
        string truncated;
        if (collapsed.Length <= Max)
        {
            truncated = collapsed;
        }
        else
        {
            var cut = collapsed[..Max];
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > Max / 2) cut = cut[..lastSpace];
            truncated = cut + "…";
        }

        truncated = FilesystemUnsafe().Replace(truncated, "");
        truncated = TrailingPunctuation().Replace(truncated, "");
        if (string.IsNullOrWhiteSpace(truncated)) return "Untitled";
        return truncated;
    }

    private static string MakeUniqueFileName(string baseFileName, HashSet<string> taken)
    {
        if (taken.Add(baseFileName))
            return baseFileName;

        var stem = Path.GetFileNameWithoutExtension(baseFileName);
        var ext = Path.GetExtension(baseFileName);
        for (int n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (taken.Add(candidate))
                return candidate;
        }
    }

```

- [ ] **Step 5: Wire the helpers into `ConvertAsync` (still without writing files)**

In `src/Fabulis.Cli/SillyTavernConvertService.cs`, replace the `foreach (var file in jsonlFiles)` block in `ConvertAsync` with:

```csharp
        var takenFileNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in jsonlFiles)
        {
            List<ParsedTurn>? turns;
            try
            {
                turns = await ParseFileAsync(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: could not read ({ex.Message})");
                result.FilesFailed++;
                continue;
            }

            if (turns is null || turns.Count == 0)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: no conversation turns found, skipped");
                result.FilesFailed++;
                continue;
            }

            var storytellerName = DeriveStorytellerName(turns, file.FullName);
            if (storytellerName is null)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: no storyteller turns found, skipped");
                result.FilesSkipped++;
                continue;
            }

            var modelName = DeriveModel(turns, file.FullName);
            var (createdUtc, updatedUtc) = DeriveTimestamps(turns, file);

            // Greeting-skip and body emit land in Task 6.
            _ = storytellerName;
            _ = modelName;
            _ = createdUtc;
            _ = updatedUtc;
            _ = takenFileNames;
        }
```

- [ ] **Step 6: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 7: Verify "no storyteller turns" classification**

```bash
rm -rf /tmp/st-test-dest /tmp/st-user-only
mkdir -p /tmp/st-user-only
cat > /tmp/st-user-only/u.jsonl <<'EOF'
{"chat_metadata":{}}
{"name":"Paul","is_user":true,"mes":"hi","send_date":"2026-05-20T10:00:00.000Z"}
EOF
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-user-only /tmp/st-test-dest
```

Expected on stderr: `warn: <abs>/u.jsonl: no storyteller turns found, skipped`. On stdout: `Converted: 0 drafts written, 1 skipped, 0 failed`. Exit 0.

- [ ] **Step 8: Cleanup**

```bash
rm -rf /tmp/st-test-dest /tmp/st-user-only
```

- [ ] **Step 9: Commit**

```bash
git add src/Fabulis.Cli/SillyTavernConvertService.cs
git commit -m "SillyTavernConvertService: derive header fields and title

Adds private helpers that pull the Storyteller name (first non-user
'name', warning on mixed names), Model (last non-empty extra.model,
falling back to '(unknown)'), Created/Updated (first/last send_date
with file mtime fallback), Title (first user message, collapsed and
sanitized to 60 chars), and a filename-collision deduplicator. Files
with no storyteller turns are skipped."
```

---

### Task 6: Drop the greeting, format the body, write the `.md` file

After this task the verb does its job end-to-end. Files where the greeting-skip leaves no user turns are classified as Skipped.

**Files:**
- Modify: `src/Fabulis.Cli/SillyTavernConvertService.cs`

- [ ] **Step 1: Replace the per-file loop in `ConvertAsync` with the full-conversion version**

In `src/Fabulis.Cli/SillyTavernConvertService.cs`, replace the entire `foreach (var file in jsonlFiles)` block in `ConvertAsync` with:

```csharp
        var takenFileNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in jsonlFiles)
        {
            List<ParsedTurn>? turns;
            try
            {
                turns = await ParseFileAsync(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: could not read ({ex.Message})");
                result.FilesFailed++;
                continue;
            }

            if (turns is null || turns.Count == 0)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: no conversation turns found, skipped");
                result.FilesFailed++;
                continue;
            }

            var storytellerName = DeriveStorytellerName(turns, file.FullName);
            if (storytellerName is null)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: no storyteller turns found, skipped");
                result.FilesSkipped++;
                continue;
            }

            var modelName = DeriveModel(turns, file.FullName);
            var (createdUtc, updatedUtc) = DeriveTimestamps(turns, file);

            // Drop the greeting: the first non-user turn that precedes any user turn.
            var bodyTurns = turns.ToList();
            var firstUserIndex = bodyTurns.FindIndex(t => t.IsUser);
            if (firstUserIndex < 0)
            {
                Console.Error.WriteLine($"warn: {file.FullName}: greeting-only chat, skipped");
                result.FilesSkipped++;
                continue;
            }
            var greetingIndex = -1;
            for (int i = 0; i < firstUserIndex; i++)
            {
                if (!bodyTurns[i].IsUser)
                {
                    greetingIndex = i;
                    break;
                }
            }
            if (greetingIndex >= 0)
                bodyTurns.RemoveAt(greetingIndex);

            var title = DeriveTitle(bodyTurns);
            var stamp = createdUtc.ToString("yyyyMMddTHHmmssZ");
            var baseFileName = $"Draft {stamp} - {title}.md";
            var fileName = MakeUniqueFileName(baseFileName, takenFileNames);

            var messages = bodyTurns.Select((t, idx) => (
                Role: t.IsUser ? MessageRole.Prompt : MessageRole.Response,
                Content: t.Message,
                SortOrder: idx));

            var content = DraftMarkdownWriter.FormatDraft(
                storytellerName, modelName, createdUtc, updatedUtc, messages);

            var outputPath = Path.Combine(draftsDir, fileName);
            await File.WriteAllTextAsync(outputPath, content);
            result.DraftsWritten++;
        }
```

- [ ] **Step 2: Add `using Fabulis.Server.Data;` to the top of the file so `MessageRole` is in scope**

The using block at the top of `src/Fabulis.Cli/SillyTavernConvertService.cs` should now read:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fabulis.Server.Data;
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Verify the greeting-only skip**

```bash
rm -rf /tmp/st-test-dest /tmp/st-greeting-only
mkdir -p /tmp/st-greeting-only
cat > /tmp/st-greeting-only/g.jsonl <<'EOF'
{"chat_metadata":{}}
{"name":"StoryTeller","is_user":false,"mes":"What shall we write?","send_date":"2026-05-20T10:00:00.000Z"}
EOF
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-greeting-only /tmp/st-test-dest
ls /tmp/st-test-dest/_drafts/
```

Expected on stderr: `warn: <abs>/g.jsonl: greeting-only chat, skipped`. On stdout: `Converted: 0 drafts written, 1 skipped, 0 failed`. Exit 0. The `_drafts/` directory exists and is empty.

- [ ] **Step 5: Verify a minimal end-to-end conversion**

```bash
rm -rf /tmp/st-test-dest /tmp/st-minimal
mkdir -p /tmp/st-minimal
cat > /tmp/st-minimal/m.jsonl <<'EOF'
{"chat_metadata":{}}
{"name":"StoryTeller","is_user":false,"mes":"Greeting that should be skipped.","send_date":"2026-05-20T10:00:00.000Z"}
{"name":"Paul","is_user":true,"mes":"Tell me about a forest.","send_date":"2026-05-20T10:01:00.000Z"}
{"name":"StoryTeller","is_user":false,"mes":"The forest is dark and deep.","send_date":"2026-05-20T10:02:00.000Z","extra":{"model":"moonshotai/kimi-k2-0905"}}
EOF
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-minimal /tmp/st-test-dest
ls /tmp/st-test-dest/_drafts/
```

Expected on stdout: `Converted: 1 drafts written, 0 skipped, 0 failed`. Exactly one file is written, with a name like `Draft 20260520T100000Z - Tell me about a forest.md`.

Now print the file to confirm shape:

```bash
cat "/tmp/st-test-dest/_drafts/Draft 20260520T100000Z - Tell me about a forest.md"
```

Expected output (exact, treating `<CR><LF>` or `<LF>` as equivalent depending on platform):

```
Storyteller: StoryTeller
Model: moonshotai/kimi-k2-0905
Created: 2026-05-20T10:00:00.0000000Z
Updated: 2026-05-20T10:02:00.0000000Z

**Me:**

Tell me about a forest.

**StoryTeller:**

The forest is dark and deep.

```

Key things to confirm by eye:
- Four-line header is present and in the order `Storyteller / Model / Created / Updated`.
- Body starts with `**Me:**` (the greeting was dropped).
- `Created` matches the greeting's `send_date` (not the first user message's).
- `Updated` matches the last storyteller message's `send_date`.

- [ ] **Step 6: Verify filename collision handling**

```bash
rm -rf /tmp/st-test-dest /tmp/st-collisions
mkdir -p /tmp/st-collisions
# Two files with the same Created timestamp and same first user message
for n in 1 2; do
cat > /tmp/st-collisions/$n.jsonl <<EOF
{"chat_metadata":{}}
{"name":"StoryTeller","is_user":false,"mes":"hi","send_date":"2026-05-20T10:00:00.000Z"}
{"name":"Paul","is_user":true,"mes":"same prompt","send_date":"2026-05-20T10:01:00.000Z"}
{"name":"StoryTeller","is_user":false,"mes":"reply $n","send_date":"2026-05-20T10:02:00.000Z"}
EOF
done
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-collisions /tmp/st-test-dest
ls /tmp/st-test-dest/_drafts/
```

Expected on stdout: `Converted: 2 drafts written, 0 skipped, 0 failed`. The directory contains two files; one named `Draft 20260520T100000Z - same prompt.md` and one named `Draft 20260520T100000Z - same prompt (2).md`.

- [ ] **Step 7: Cleanup**

```bash
rm -rf /tmp/st-test-dest /tmp/st-greeting-only /tmp/st-minimal /tmp/st-collisions
```

- [ ] **Step 8: Commit**

```bash
git add src/Fabulis.Cli/SillyTavernConvertService.cs
git commit -m "SillyTavernConvertService: emit draft markdown files

Drops the first storyteller turn (the character-card greeting), maps
the remaining turns to MessageRole.Prompt / .Response, and writes the
draft via DraftMarkdownWriter to <dest>/_drafts/. Filename collisions
get a ' (2)', ' (3)', ... suffix. Greeting-only files are skipped."
```

---

### Task 7: Update `src/Fabulis.Cli/README.md`

After this task, the README documents the new verb.

**Files:**
- Modify: `src/Fabulis.Cli/README.md`

- [ ] **Step 1: Add the new verb to the usage block at the top of the README**

In `src/Fabulis.Cli/README.md`, find the `## Commands` section's code block:

```
dotnet run --project src/Fabulis.Cli -- export <destination>
dotnet run --project src/Fabulis.Cli -- import <source>
```

Replace it with:

```
dotnet run --project src/Fabulis.Cli -- export <destination>
dotnet run --project src/Fabulis.Cli -- import <source>
dotnet run --project src/Fabulis.Cli -- sillytavern <source> <destination>
```

- [ ] **Step 2: Add a `sillytavern` section after the `import` description**

In `src/Fabulis.Cli/README.md`, immediately above the `## On-disk format` heading, insert:

```markdown
The `sillytavern` verb is a one-way file conversion (no vault access,
no password prompt). It reads `<source>/*.jsonl` (non-recursive — point
it at one SillyTavern character directory at a time) and writes draft
markdown files to `<destination>/_drafts/`. The destination must not
exist. After reviewing the output, import it with
`fabulis-cli import <destination>`.

Per file, the conversion:

- skips the chat-metadata line and any `is_system` rows;
- pulls `Storyteller:` from the `name` field of the first non-user
  message (warns on mixed names);
- pulls `Model:` from the last non-empty `extra.model` on a non-user
  message (falls back to `(unknown)` with a warning);
- sets `Created` to the first message's `send_date` and `Updated` to
  the last surviving message's `send_date` (falls back to the file's
  mtime if `send_date` is missing or unparseable);
- drops the first storyteller turn (the character-card greeting) from
  the body; if no user message remains, the file is skipped;
- derives the draft title from the first user message (collapsed
  whitespace, trimmed to 60 chars at the nearest word boundary, with
  filesystem-unsafe characters stripped);
- emits the surviving turns as `**Me:**` / `**StoryTeller:**` blocks,
  preserving the message text verbatim.

Filename collisions inside `<destination>/_drafts/` get a ` (2)`,
` (3)`, ... suffix added to the title. SillyTavern swipes are not
preserved; only the selected swipe (already mirrored into `mes`) is
written. Per-message reasoning, generation timings, and persona /
world-info metadata are dropped.
```

- [ ] **Step 3: Update the database-location note to mention the new verb is excluded**

In `src/Fabulis.Cli/README.md`, find the `## Database location` heading. The paragraph below it starts with `By default the CLI walks up from its own assembly directory...`. Replace that paragraph's first sentence:

```
By default the CLI walks up from its own assembly directory until it finds
```

with:

```
The `export` and `import` verbs open the vault; the `sillytavern` verb
does not. By default the CLI walks up from its own assembly directory until it finds
```

- [ ] **Step 4: Verify the README renders sensibly**

```bash
cat src/Fabulis.Cli/README.md | head -60
```

Eyeball the result: the usage block at the top has three lines, the new section sits above `## On-disk format`, and the database-location paragraph notes that `sillytavern` doesn't open the vault.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Cli/README.md
git commit -m "Document the sillytavern verb in Fabulis.Cli README"
```

---

### Task 8: End-to-end verification against the SillyTavern sample

Manual, against the user's real sample file and a running vault. This is the final acceptance test.

**Files:**
- No code changes.

- [ ] **Step 1: Run the conversion against the user's sample directory**

```bash
rm -rf /tmp/st-acceptance
dotnet run --project src/Fabulis.Cli -- sillytavern \
  "/Volumes/Untitled/AI/SillyTavern/data/default-user/chats/StoryTeller" \
  /tmp/st-acceptance
```

Expected on stdout: a `Converted: N drafts written, M skipped, K failed` line. Any warnings (e.g. `no model metadata`) appear on stderr.

- [ ] **Step 2: Inspect the produced files**

```bash
ls /tmp/st-acceptance/_drafts/
```

For each filename, confirm:
- It matches the `Draft <stamp> - <Title>.md` pattern (the regex in `CategoryImportService.DraftFileNamePattern`).
- The `<stamp>` looks like `yyyyMMddTHHmmssZ`.
- The `<Title>` reads as a recognisable human-meaningful prefix of the first user message (no slashes or other unsafe characters).

- [ ] **Step 3: Inspect one file's contents**

Pick any `.md` and:

```bash
head -10 /tmp/st-acceptance/_drafts/Draft*.md
```

Expected: each file's first four lines are `Storyteller:`, `Model:`, `Created:`, `Updated:` in that order; line 5 is blank; line 6 begins with `**Me:**` (greeting was dropped — confirms task 6 worked on real data); subsequent body alternates with `**StoryTeller:**`.

- [ ] **Step 4: Run `import` against the converted output**

```bash
# Start the server if it isn't already running, so a SQLite file exists at the
# default path. Then stop it (or call /api/v1/auth/lock) before importing.
dotnet run --project src/Fabulis.Cli -- import /tmp/st-acceptance
```

Enter the vault password when prompted. Expected: `Imported: 0 categories, 0 stories, 0 versions, N drafts` where N matches the `drafts written` count from Step 1 (minus any whose `Storyteller:` value doesn't match an existing storyteller in the vault — those produce a `warn: draft references unknown storyteller '<name>', skipping: ...` line on stderr).

If the user has no matching storyteller, every draft will be skipped. In that case:
1. Note the storyteller name from one of the `.md` files (the value after `Storyteller:`).
2. Create a matching storyteller in the vault via the SwiftUI client (Settings → Storytellers).
3. Re-run the import. Expected: this time the drafts land (the importer is idempotent so already-imported rows are not duplicated).

- [ ] **Step 5: Re-run the import to confirm idempotency**

```bash
dotnet run --project src/Fabulis.Cli -- import /tmp/st-acceptance
```

Enter the password again. Expected: `Imported: 0 categories, 0 stories, 0 versions, 0 drafts`. No duplicates appear in the client.

- [ ] **Step 6: Sanity-check the failure modes**

```bash
# Source does not exist
dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/no-such-dir /tmp/whatever
# Destination already exists
dotnet run --project src/Fabulis.Cli -- sillytavern \
  "/Volumes/Untitled/AI/SillyTavern/data/default-user/chats/StoryTeller" /tmp/st-acceptance
# Source has no .jsonl
mkdir -p /tmp/st-empty && dotnet run --project src/Fabulis.Cli -- sillytavern /tmp/st-empty /tmp/nope
```

Expected for each: a single `error: ...` line on stderr, exit code 1, no partial output.

- [ ] **Step 7: Cleanup**

```bash
rm -rf /tmp/st-acceptance /tmp/st-empty
```

- [ ] **Step 8: Final commit (BACKLOG / nothing left)**

No code changes in this task. If the user moved any items off of the backlog as part of shipping this feature, commit those edits now; otherwise this step is a no-op.

```bash
git status
# If the working tree is clean, skip the commit. Otherwise:
# git add -A && git commit -m "..."
```

---

## Self-review notes (post-implementation)

The implementer should not modify these — they're a record of what to look for. The plan author already worked through:

- All five spec sections (verb shape, per-file conversion, error handling, README, testing) are covered by tasks 2-8.
- No `TBD`/`TODO`/`fill in details` placeholders. Forward-references (`// Conversion logic lands in Tasks 4-6.`) are explicit pointers, not placeholders.
- Type / method name consistency: `ParsedTurn` (record) and `ConvertResult` (counters class) are introduced in Task 4 and reused by name in Tasks 5-6. `DraftMarkdownWriter.FormatDraft` / `FormatConversation` signatures match between Task 1 (definition) and Task 6 (call site).
- Filename pattern `Draft <stamp> - <Title>.md` matches `CategoryImportService.DraftFileNamePattern` (`^Draft\s+(\d{8}T\d{6}Z|\d+)\s+-\s+.+\.md$`) so the importer round-trips it.
- Warning text is consistent across the spec, the code, and the test expectations.
