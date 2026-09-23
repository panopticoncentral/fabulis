# Fabulis.Cli

Command-line backup and restore for the Fabulis vault. Runs on the same
machine as the server, opens the SQLCipher database directly, and reads or
writes a directory tree of markdown files — a **vault archive**.

> **Archives written before this tool's current format are not readable.**
> There is no legacy support: an old export will be rejected outright. If you
> have one, re-export from the live vault with the current build before
> upgrading.

## Commands

```
dotnet run --project src/Fabulis.Cli -- export <destination> [--include-secrets]
dotnet run --project src/Fabulis.Cli -- import <source> [--mirror] [--yes]
dotnet run --project src/Fabulis.Cli -- sillytavern <source> <destination>
```

`export` and `import` prompt for the vault password (no echo). `sillytavern`
does not — it never opens the vault.

### `export <destination> [--include-secrets]`

Writes the whole vault to a directory tree at `<destination>`, which must not
exist. Every category, story, prompt, one-liner, trope, storyteller, draft
and app setting is written — nothing is skipped for having no content.

`--include-secrets` also writes the `OpenRouterApiKey` app setting **in
plaintext**. Without the flag that setting is omitted from the archive
entirely. Only pass it when you understand the archive will contain a live
credential.

### `import <source> [--mirror] [--yes]`

Reads an archive directory — one containing a `fabulis.md` manifest at its
root — into the vault. There is no shape-detection: `<source>` must be an
archive root as `export` (or `sillytavern`, see below) produces it, and an
unrecognized `formatVersion` in the manifest is a hard error.

Import is **additive and idempotent** by default:

- existing category / story / prompt / storyteller rows are matched by name
  and reused; matching `VersionNumber` values within a story are skipped
- drafts are deduped by `(Storyteller, Title, CreatedAt)`
- a missing `one-liners.md` or `tropes.md` file means "unspecified" — the
  category's existing lists are left alone
- app settings are only ever added, never overwritten or deleted, by a plain
  merge import

`--mirror` instead makes the vault match the directory **exactly**: rows
present in the vault but absent from the archive are deleted, and rows that
already exist are updated to match the file (including a full replace of
each category's one-liners and tropes). Because this destroys data, `--mirror`
prints an itemized list of every category, story, version, prompt, draft,
storyteller and one-liner/trope-list loss it is about to apply, then asks
`Delete N item(s)? [y/N]` on the console before touching the database.
`--yes` skips that prompt — but not the itemized list, which is still printed
first — for scripted use; the summary line at the end also reports how many
items were deleted, so a scripted `--mirror --yes` run leaves a record of
what it destroyed even with nothing to read on the way in. Settings are never
deleted, even under `--mirror` — an absent key means "unspecified", not
"removed" — and if a storyteller a surviving draft still references would be
deleted, the whole import fails with an error instead of silently keeping it.

**A missing `one-liners.md` or `tropes.md` behaves differently under
`--mirror` than under a plain merge import.** Under a plain import, a missing
file means "unspecified" (see above) — the category's existing list is left
alone. Under `--mirror`, a missing file counts as an **empty** file: the
category's entire one-liner or trope list is wiped, exactly as if the file
existed with zero items. This happens with no separate interactive warning —
it is reported only as a `list` line in the itemized deletion list (or,
under `--yes`, in that same list printed without a prompt) and folded into
the `Delete N item(s)?` count. `export` always writes both files (even when
empty), so a missing one in a real archive is not something export can
produce — it means the file was removed or renamed by hand after export, and
`--mirror` will treat that hand-edit as "clear this list."

**The same reasoning scales up to a whole missing top-level directory.** A
`library/`, `drafts/` or `storytellers/` directory that isn't in the archive
is not "unspecified" under `--mirror` — it is empty, so *every* category (with
all its stories, versions, prompts and lists), *every* draft, or *every*
storyteller is deleted. `export` always writes all three, so this only arises
from a hand-edit, a partial copy, or an archive assembled by something else —
including `sillytavern` output, which populates `drafts/` and nothing else and
so must never be imported with `--mirror` against a real vault. As with any
other deletion, the itemized list names each row before the `Delete N item(s)?`
prompt, so the scale of it is visible before anything is destroyed.

If any file in the archive cannot be read or parsed, the entire deletion pass
for that run is suppressed (with a warning) rather than guessing: a file that
can't be read looks exactly like a file that was removed, and mirroring
against a partially-readable archive must not delete data on that basis.
List replacement that already happened is still reported and still requires
confirmation.

If two rows in the same scope (e.g. two categories, or two stories in one
category) would sanitize to the same on-disk name, `--mirror` refuses to run
and names the conflicting rows — there would be no way to tell which row a
suffixed directory like `Name (2)` came from, and guessing wrong would delete
the wrong one. Rename one of the rows and import again.

### `sillytavern <source> <destination>`

A one-way, vault-free file conversion — no password prompt, no database
access. It reads `<source>/*.jsonl` (non-recursive — point it at one
SillyTavern character directory at a time) and writes draft markdown files to
`<destination>/drafts/`, along with a `fabulis.md` manifest at
`<destination>`, so the output directory is itself a valid archive: review
the drafts, then run `fabulis-cli import <destination>` to bring them into
the vault. `<destination>` must not exist.

Per file, the conversion:

- skips the chat-metadata line and any `is_system` rows;
- pulls the storyteller name from the `name` field of the first non-user
  message (warns on mixed names across the file);
- sets `created` to the first message's `send_date` and `updated` to the
  last surviving message's `send_date` (falls back to the file's mtime if
  `send_date` is missing or unparseable);
- drops the first storyteller turn (the character-card greeting) from the
  body; if no user message remains, the file is skipped;
- derives the draft title from the first user message (collapsed
  whitespace, trimmed to 60 chars at the nearest word boundary, with
  filesystem-unsafe characters stripped);
- emits the surviving turns in the same `**Me:**` / `**StoryTeller:**` format
  story versions and drafts use, preserving the message text verbatim.

Filename collisions inside `<destination>/drafts/` get a ` (2)`, ` (3)`, ...
suffix added to the title. SillyTavern swipes are not preserved; only the
selected swipe (already mirrored into `mes`) is written. Per-message
reasoning, generation timings, and persona / world-info metadata are
dropped. There is no `Model:` field in the output — a draft's model comes
from its storyteller, and the storyteller itself is not something this
conversion can create, so a draft naming a storyteller the vault doesn't
have is skipped on import (see below).

## On-disk format

```
<root>/
  fabulis.md                     format version + exported-at
  settings.md                    AppSettings (secret key omitted unless --include-secrets)
  storytellers/
    <Storyteller Name>.md
  library/
    <Category Name>/
      one-liners.md
      tropes.md
      prompts/
        <Prompt Title>.md
      stories/
        <Story Title>/
          Version 1.md
          Version 2.md
  drafts/
    <yyyyMMddTHHmmssZ> - <Title>.md
```

The root is namespaced (`library/`, `drafts/`, `storytellers/`) so a category
can never collide with a reserved directory name. Every level is written
even when it has no children — a category holding only tropes still gets a
`stories/` and `prompts/` directory, and `one-liners.md` / `tropes.md` are
always written, even empty, so a missing file and an empty list stay
distinguishable on import. `stories/` and `prompts/` are always created
inside a category for the same reason.

Identity is the path: a category, story, prompt or storyteller is whatever
its directory or file is named. Renaming a folder on disk renames the thing
on import; nothing on disk carries a database row id, and ids are not
preserved across a round trip.

Two consequences worth knowing about:

- A category or story with no children (no stories, or no versions) has
  nothing to derive a creation date from, so its `CreatedAt` becomes the
  import time rather than something preserved from before.
- Git cannot track an empty directory. A story exported with zero versions
  round-trips through the archive format fine, but if you commit the archive
  to git, that story's now-empty `stories/<Title>/` directory will not
  survive the commit — the story itself is still gone from the git history
  even though `export`/`import` never lost it.

### Front matter

Every non-list file opens with a small hand-rolled YAML subset: a line that
is exactly `---`, then flat `key: value` scalar lines, then a closing `---`.
No nesting, no lists, no anchors. Values are strings, integers, doubles, or
an ISO-8601 UTC timestamp in round-trip (`O`) format. A string is quoted
(with `\"` and `\\` escapes) only when it needs to be — leading/trailing
whitespace, a `: ` substring, or a leading character that would otherwise
change the parse. A null value simply omits its key.

`fabulis.md` carries `formatVersion: 1` and `exportedAt`; import reads it
first and refuses anything but the version it understands. Multi-line
values (a storyteller's system prompt, a multi-line app setting like
`SummaryPrompt`) become `## <Heading>` body sections below the front matter
instead of a front-matter value.

### Conversation turns and prompt blocks

Story versions and drafts are a sequence of `**Me:**` / `**StoryTeller:**`
turns. Prompts (which have no role, only ordered content) separate their
messages with a line that is exactly `---`. In both formats, a content line
that would itself read as a delimiter — `**Me:**`, `**StoryTeller:**`, or
`---` — is written with a leading backslash and read back with it stripped,
so a message that quotes the format round-trips intact instead of silently
corrupting.

Story-version front matter includes `origin: Generated` or
`origin: Imported`. Generated versions normally include `model`; imported
versions may omit it. An imported prose story can be represented by a single
`**StoryTeller:**` response block, which keeps it on the same reader, search,
narration, and summary paths as generated stories.

## Known lossy round trips

- Row ids change; identity is the path.
- `Story.CreatedAt` / `Category.CreatedAt` are derived from the earliest
  child rather than preserved (see above for the no-children case).
- Story summaries are not exported; they regenerate.
- One-liner and trope `CreatedAt`/`UpdatedAt` reset to import time.
- Names are path-derived, so a name needing sanitization (a slash, a
  trailing dot, ...) is rewritten on round trip, and two names that
  sanitize to the same on-disk name get a NameAllocator ` (2)` suffix.
- Message `SortOrder` is reindexed to `0..n` on import — relative order
  always survives, the original absolute values do not.
- Leading and trailing blank lines in a message's content are stripped
  (inner blank lines survive).

Drafts whose `storyteller:` front-matter value does not match an existing
storyteller in the vault are skipped with a warning. This is no longer a
fidelity limitation — a full export captures a storyteller's system prompt,
titling prompt and every tuning parameter — it only happens with a
hand-built or hand-edited archive that references a storyteller that
genuinely isn't there.

## Database location

The `export` and `import` verbs open the vault; the `sillytavern` verb does
not. The CLI resolves the database exactly the way the server does, so the
two always agree: `fabulis.db` in the `Fabulis` directory under your
application-data directory (`~/Library/Application Support/Fabulis` on macOS).
To point at a different file (deployed location, alternate vault), set:

```
export FABULIS_DB_PATH=/path/to/fabulis.db
```

## Running alongside the server

SQLite + WAL allows concurrent reads, so `export` is safe to run while the
server is up. Avoid running `import` against a vault the server is actively
writing to — interleaved writes can produce inconsistent results. Stop the
server (or call `POST /api/v1/auth/lock`) before importing.
