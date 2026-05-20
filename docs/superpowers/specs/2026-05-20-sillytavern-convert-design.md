# Convert SillyTavern Chats to Fabulis Draft Markdown

## Goal

Add a third verb to `Fabulis.Cli` that converts SillyTavern chat files
(`.jsonl`) into Fabulis draft markdown files (`.md`), arranged so the
output is ready for the existing `import` verb after a manual review
pass.

The motivation is a one-time bulk migration of a corpus of existing
SillyTavern conversations into the Fabulis vault. The user wants a
non-destructive convert-then-review-then-import workflow rather than
direct database insertion.

## User-visible behavior

```
dotnet run --project src/Fabulis.Cli -- sillytavern <source> <dest>
```

- `<source>` is a directory containing `*.jsonl` files at the top
  level (non-recursive — matches the streader tool the format work was
  originally prototyped against).
- `<dest>` is a directory that must not already exist. The verb
  creates it and writes converted files under `<dest>/_drafts/`.
- One `.md` is produced per input `.jsonl` (subject to the skip rules
  in [Per-file conversion](#per-file-conversion)).
- After review, the existing import verb picks the output up directly:

  ```
  dotnet run --project src/Fabulis.Cli -- import <dest>
  ```

  The importer's `_drafts/` child rule classifies `<dest>` as a
  library root containing only drafts.

The verb does not touch the database and does not prompt for the
vault password. This is a pure file transformation.

## Architecture

A new `SillyTavernConvertService` lives alongside
`CategoryExportService` and `CategoryImportService` in
`src/Fabulis.Cli/`. It exposes:

```csharp
public class SillyTavernConvertService
{
    public Task<ConvertResult> ConvertAsync(string sourcePath, string destPath);
}

public class ConvertResult
{
    public int DraftsWritten { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }
}
```

No `FabulisDbContext` parameter — the service has no DB dependency.

`Program.cs` is refactored so that DB-related setup (resolving the
path, prompting for the password, opening the connection) is gated on
the verb. The current top-level layout is:

```
parse args
resolve db path
prompt password
open DbContext
dispatch (export | import)
```

The new layout, expressed in pseudo-code:

```
parse args
switch verb:
  case "export": resolveDb(); promptPassword(); openDb(); ExportService...
  case "import": resolveDb(); promptPassword(); openDb(); ImportService...
  case "sillytavern": SillyTavernConvertService.ConvertAsync(src, dst)
  default: usage(); return 1
```

The cleanest implementation is to lift the DB setup into a small helper
(`OpenVaultAsync(out FabulisDbContext)` or similar) that the two DB
verbs call and the new verb skips.

### Format reuse

The four-line header + alternating `**Me:**` / `**StoryTeller:**`
body format is already produced by
`CategoryExportService.FormatDraft` / `FormatConversation`. To avoid a
second copy, those two methods are extracted into a new internal
helper class:

```csharp
internal static class DraftMarkdownWriter
{
    public static string Format(
        string storytellerName,
        string modelName,
        DateTime createdUtc,
        DateTime updatedUtc,
        IEnumerable<(MessageRole Role, string Content, int SortOrder)> messages);
}
```

`CategoryExportService.FormatDraft` becomes a one-line call into this
helper. `SillyTavernConvertService` calls it with the values it
derives from the jsonl. There is one source of truth for the on-disk
draft shape.

## Per-file conversion

For each `*.jsonl` file matched by `Directory.GetFiles(source,
"*.jsonl")` (sorted by filename for deterministic output):

### 1. Parse

Read the file line by line. Each non-empty line is one JSON object.

- The line where `chat_metadata` is a top-level key is the chat header
  — skip it.
- Lines where `is_system` is `true` are SillyTavern internal commands
  (`/sys`, world-info pushes, etc.) — skip.
- Lines that fail to parse as JSON are reported to stderr as
  `<path>:<line>: invalid JSON, skipped` and dropped.

The surviving objects are the conversation turns. Each turn is
classified by `is_user` (boolean): user turn if true, storyteller turn
otherwise. The text comes from the `mes` field. The `swipes[]` array
is **ignored** — SillyTavern keeps `mes` in sync with the currently
selected swipe, which is what was visible to the human when they
saved the chat.

### 2. Skip the greeting

The first storyteller turn — the character-card greeting that
SillyTavern inserts before any user input — is dropped from the
output body.

Concretely: walk the surviving turns in order, find the first turn
with `is_user: false` that precedes any `is_user: true` turn, and
remove only that one turn — even if more storyteller turns follow
before the first user turn. (SillyTavern emits exactly one greeting;
any consecutive storyteller turns after it would be regenerated
content the human kept.) If the file's first turn is a user turn (no
greeting present), nothing is dropped.

If, after this drop, no user turns remain at all, the file is
**skipped** (counted in `FilesSkipped`) with the reason
`greeting-only chat`.

### 3. Derive header fields

- **Storyteller name** = the `name` field of the first non-user turn
  in the file (i.e. the greeting turn before it was dropped, or the
  first storyteller reply if no greeting was present). If multiple
  non-user `name` values appear in the file, the first one wins and
  the verb prints a warning like
  `<path>: mixed storyteller names (StoryTeller, Mara), used 'StoryTeller'`.

- **Model name** = the last non-empty `extra.model` value across all
  non-user turns (document order). If no turn carries that field,
  write `(unknown)` and warn
  `<path>: no model metadata, wrote 'Model: (unknown)'`.

- **Created** = `send_date` of the file's first turn (the greeting,
  if present; otherwise the first surviving turn). Parsed as
  ISO‑8601, normalised to UTC. If parsing fails or `send_date` is
  missing, fall back to the jsonl file's last-write time and warn.

- **Updated** = `send_date` of the file's last surviving turn (after
  the system / parse-failure filters in step 1, but ignoring whether
  the greeting was dropped). Same parsing and fallback as Created.

### 4. Derive title

Take the `mes` of the first user turn. Collapse runs of whitespace
(including newlines) to single spaces, trim, then truncate to 60
characters. If the cut falls inside a word, back up to the previous
space and append `…`. Sanitise for the filesystem by removing any of
`/ \ : * ? " < > |` and stripping trailing punctuation. If the result
is empty, use `Untitled`.

### 5. Filename

```
Draft <CreatedUtc:yyyyMMddTHHmmssZ> - <Title>.md
```

This matches the filename pattern produced by the export verb and
recognised by `CategoryImportService.DraftFileNamePattern`.

If two input files produce the same filename (same Created stamp
*and* same title — possible if SillyTavern saved two chats in the
same second with very similar first user messages), append ` (2)`,
` (3)`, … to the title before `.md` to disambiguate.

### 6. Body

Walk the surviving turns in order (with the greeting already removed)
and emit, for each turn:

```
**Me:**          ← if is_user
**StoryTeller:** ← if not is_user

<mes, with leading/trailing blank lines trimmed>

```

Interior content of `mes` is preserved verbatim — any Markdown
already in the message (bold, italics, headings, asterisk-wrapped
actions) passes through unchanged.

### 7. Write

Compose the full document via `DraftMarkdownWriter.Format(...)` and
write it to `<dest>/_drafts/<Filename>`. The `_drafts` directory is
created on first write.

## Error handling

Three outcomes per input file:

| Outcome   | Trigger                                                                                   | Reporting                                              |
|-----------|-------------------------------------------------------------------------------------------|--------------------------------------------------------|
| Converted | A `.md` was produced.                                                                     | `DraftsWritten++`. Warnings (if any) go to stderr.     |
| Skipped   | File parsed but produced no draft (greeting-only, every line filtered).                   | `FilesSkipped++` plus a stderr warning naming the file. |
| Failed    | File could not be opened, or contained zero parseable JSON lines.                         | `FilesFailed++` plus a stderr warning naming the file. |

A failed file does not terminate the run — the verb continues to the
next file. The exit code is 0 as long as the preconditions in the
next section passed.

### Preconditions (checked before any file is read)

- `<source>` exists and is a directory. Otherwise: error, exit 1.
- `<dest>` does not exist. Otherwise: error, exit 1 (matches `export`).
- `<source>` contains at least one `*.jsonl` file at the top level.
  Otherwise: error "no .jsonl files found", exit 1.

### Summary line

After all files have been processed, the verb prints to stdout:

```
Converted: <DraftsWritten> drafts written, <FilesSkipped> skipped, <FilesFailed> failed
```

Warnings stream to stderr as they occur (not buffered until the end)
so a long run shows progress.

### No transactional rollback

Files are written one at a time. If the verb is interrupted halfway
through, `<dest>/_drafts/` will contain whatever was written before
the interruption. Re-running requires deleting `<dest>` first
(because of the must-not-exist precondition). This matches `export`'s
behaviour and is acceptable for a one-time migration tool.

## README update

`src/Fabulis.Cli/README.md` gains a short section after the existing
`export` / `import` description, explaining:

- The verb's purpose (one-way conversion from SillyTavern chats).
- The expected source layout (top-level `*.jsonl` files, non-recursive).
- How the conversion maps SillyTavern fields onto the draft headers
  (a short table or bulleted list).
- The follow-up workflow: review the files, then run `import` against
  `<dest>` to load them into the vault.

The usage block at the top of the README gains the new command.

## Testing

Manual, matching the existing convention for this project (no test
project for `Fabulis.Cli`):

1. Convert the sample chat into a temp directory:
   ```
   dotnet run --project src/Fabulis.Cli -- sillytavern \
     "/Volumes/Untitled/AI/SillyTavern/data/default-user/chats/StoryTeller" \
     /tmp/st-out
   ```
   Confirm `/tmp/st-out/_drafts/` contains the expected `.md` files
   and the summary line is correct.
2. Open one of the `.md` files in an editor; confirm the four-line
   header is well-formed (Storyteller, Model, Created, Updated) and
   the body alternates `**Me:**` / `**StoryTeller:**` from the first
   user turn (i.e. the greeting was dropped).
3. Run `dotnet run --project src/Fabulis.Cli -- import /tmp/st-out`
   against a vault that contains a `Storyteller` row whose `Name`
   matches the value written to the header. Confirm the drafts appear
   in the client and the message order is preserved.
4. Re-run the import against the same `/tmp/st-out`. Confirm the
   dedupe rule (Storyteller + Title + Created) skips everything — no
   duplicates appear.
5. Confirm the failure modes:
   - Point at a non-existent `<source>` → exit 1, clear message.
   - Point at a `<dest>` that already exists → exit 1, clear message.
   - Point at a `<source>` with no `.jsonl` files → exit 1, clear
     message.
   - Drop a malformed `.jsonl` file in (e.g. a half-truncated one)
     into a directory with valid files; confirm warnings on stderr
     and the valid files still convert.

## Out of scope

- Recursive `<source>` directories. SillyTavern stores chats under
  `chats/<CharacterName>/`, but the verb is non-recursive — point at
  one character's directory per run. Multiple characters can be
  converted by running the verb multiple times against different
  destinations and then merging the `_drafts/` folders by hand
  before importing.
- Conversion of SillyTavern character cards (PNG / JSON) into Fabulis
  storytellers. Storyteller rows must already exist in the vault for
  the import step to accept the drafts — the verb only warns on a
  storyteller-name mismatch via the existing importer warning; it
  does not try to create storytellers.
- Preserving alternate swipes. Only the selected swipe (the value in
  `mes`) is converted. The `swipes[]` array is ignored.
- Preserving SillyTavern extras: `extra.reasoning`,
  `time_to_first_token`, `gen_started` / `gen_finished`, persona
  metadata, world-info, author's-note state. The draft format has no
  fields for these.
- Direct import (skipping the review step). The user explicitly wants
  a non-destructive convert-then-review path.
- A `--force` / `--overwrite` flag for `<dest>`. Mirrors `export`'s
  must-not-exist behaviour; can be added later if the workflow
  demands it.
- Reading the source-format definition from `chat_metadata.user_name`
  / `character_name` (both observed as the literal string `unused` in
  the sample). All naming flows through message-level `name` fields.
