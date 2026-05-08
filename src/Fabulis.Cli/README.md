# Fabulis.Cli

Command-line backup and restore for the Fabulis vault. Runs on the same
machine as the server, opens the SQLCipher database directly, and reads or
writes a directory tree of markdown files.

## Commands

```
dotnet run --project src/Fabulis.Cli -- export <destination>
dotnet run --project src/Fabulis.Cli -- import <source>
```

Both commands prompt for the vault password (no echo).

- `export` writes a directory tree at `<destination>`, which must not exist.
- `import` reads a directory tree from `<source>`. The CLI auto-detects
  whether `<source>` is:
  - a **library root** — each immediate subdirectory is treated as a
    category, and an optional `_drafts/` directory alongside the
    categories is read back into the `Drafts` table; or
  - a **single category** — the immediate subdirectories are treated as
    stories and the category name is the basename of `<source>`. Drafts
    are not read in this mode.

  Detection rule, applied in order: a `_drafts/` child means library
  root; otherwise, if any immediate subdirectory contains
  `Version N [<Model>].md` files directly, the source is treated as a
  single category; otherwise, if any grand-subdirectory contains those
  files, the source is treated as a library root. If none of these
  match, import errors out.

Import is idempotent:
- existing category / story / version rows are reused; matching
  `VersionNumber` values are skipped
- drafts are deduped by `(StorytellerId, Title, CreatedAt)`

## On-disk format

```
<root>/
  <CategoryName>/
    <StoryTitle>/
      Version 1 [<ModelName>].md
      Version 2 [<ModelName>].md
  _drafts/
    Draft <CreatedAt> - <Title>.md
```

The `<CreatedAt>` stamp uses the compact ISO form `yyyyMMddTHHmmssZ` so
filenames are stable across re-exports (the previous id-stamped form
isn't, since auto-increment ids change on re-import). Legacy id-stamped
filenames from older archives are still accepted on import.

Each story version file is a sequence of `**Me:**` / `**StoryTeller:**`
turns. Each draft file has a four-line header (`Storyteller:`, `Model:`,
`Created:`, `Updated:`) followed by the same conversation format. On
import, the legacy aliases `**Paul:**` and `**Chat:**` are also accepted.

Drafts whose `Storyteller:` header does not match an existing storyteller
in the DB are skipped with a warning — the export does not capture the
storyteller's system prompt or tuning, so they cannot be safely
auto-created.

## Database location

By default the CLI walks up from its own assembly directory until it finds
`Fabulis.slnx`, then opens
`src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db`. To point at a
different file (release build, deployed location, alternate vault), set:

```
export FABULIS_DB_PATH=/path/to/fabulis.db
```

## Running alongside the server

SQLite + WAL allows concurrent reads, so `export` is safe to run while the
server is up. Avoid running `import` against a vault the server is
actively writing to — interleaved writes can produce inconsistent results.
Stop the server (or call `POST /api/v1/auth/lock`) before importing.
