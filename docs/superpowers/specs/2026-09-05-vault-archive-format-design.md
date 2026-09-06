# Vault Archive Format: Full-Fidelity Export and Import

## Context

`Fabulis.Cli` exports categories, stories, story versions and drafts to a tree
of markdown files. It has not been touched since `ecaa3a4`, and four features
have added data to the vault since:

| Data | Added in |
|---|---|
| `Prompts` + `PromptMessages` | `4187f49` Add Prompts library category |
| Story summaries | `6df6e90` Add per-story summaries |
| `OneLiners` | `dd419bb` Add One-liners library kind |
| `Tropes` | `b3f0a64` Add Tropes library kind |

None of them are exported. Neither are `Storytellers` (only the name and model
survive, as draft header text) or `AppSettings`. Two further losses are
structural rather than drift: a category holding no stories is skipped before
its directory is created, so prompt/one-liner/trope-only categories leave no
trace at all; and story-version files have no header block, so
`Category.CreatedAt`, `Story.CreatedAt` and `StoryVersion.CreatedAt` reset to
import time on restore.

Separately, the user is considering dropping SQLite and working directly from a
filesystem directory. That reframes the archive: it is no longer just a backup,
it is a candidate system of record. The format therefore has to be
hand-editable, diffable, and unambiguous about identity.

## Foundational decisions (locked in)

- **Scope**: everything content-bearing round-trips. Derived state — row ids,
  story summaries, and per-item one-liner/trope timestamps — is treated as
  cache and regenerates.
- **Secrets**: `OpenRouterApiKey` is omitted by default and written only under
  an explicit `--include-secrets` flag.
- **Metadata carrier**: YAML front matter at the top of each markdown file.
  Not sidecar JSON (doubles the file count and hides the interesting part) and
  not a root manifest (a second source of truth that a hand-edit silently
  invalidates).
- **Short-string kinds**: one-liners and tropes are a markdown bullet list, one
  file per kind per category. Per-item timestamps are dropped.
- **Import semantics**: additive and idempotent by default; `--mirror` makes
  the vault match the directory exactly.
- **Root layout**: namespaced (`library/`, `drafts/`, `storytellers/`), so a
  category can never collide with a reserved name.
- **No legacy support**: the existing shape-detection heuristic, the
  `**Paul:**`/`**Chat:**` aliases and the id-stamped draft filename fallback
  are deleted. Existing archives become unreadable; re-export from the live
  vault before upgrading.
- **Identity is the path.** A category, story, prompt or storyteller is
  identified by its directory or file name. Renaming a folder renames the
  thing. Nothing on disk carries a row id.

## On-disk layout

```
<root>/
  fabulis.md                     format version + exported-at
  settings.md                    AppSettings (key omitted unless --include-secrets)
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

Every directory level is written even when empty, so a category holding only
tropes still round-trips. `stories/` and `prompts/` are always created inside a
category. `one-liners.md` and `tropes.md` never carry front matter — they are
bullet lists and nothing else — and are written even when the list is empty, so
that an empty list and a missing file are distinguishable.

## Front matter

A deliberately minimal YAML subset, hand-rolled rather than taking a
dependency:

- The file opens with a line that is exactly `---` and the block ends at the
  next line that is exactly `---`.
- Each line inside is `key: value`. No nesting, no lists, no anchors, no
  multi-document streams.
- Values are scalars: string, integer, double, or an ISO-8601 UTC timestamp in
  round-trip (`O`) format, e.g. `2026-04-11T09:03:12.1234567Z`.
- A string is emitted bare unless it has leading or trailing whitespace,
  contains `: `, or begins with a character that would change the parse
  (`-`, `#`, `"`, `'`, `[`, `{`). In those cases it is double-quoted with `\"`
  and `\\` escapes.
- A null value omits its key entirely; no blank keys are written.
- Unknown keys are ignored with a warning rather than treated as an error.
  Forward compatibility is guarded by `formatVersion`, not by key checking.

`fabulis.md` carries `formatVersion: 1` and `exportedAt`. Import reads it first
and refuses a `formatVersion` it does not recognize. This is what makes the
no-legacy decision safe going forward: the next format change produces a clear
error instead of a silent misparse.

## Per-file shapes

### Story version — `library/<Category>/stories/<Story>/Version <N>.md`

```markdown
---
model: anthropic/claude-sonnet-4
created: 2026-04-11T09:03:12.1234567Z
---

**Me:**

...

**StoryTeller:**

...
```

The version number lives in the filename and nowhere else; the model name moves
into front matter, which removes the filename-escaping problem that model names
containing `/` create today. `Story.CreatedAt` is derived on import as the
earliest version `created`, and `Category.CreatedAt` as the earliest of its
children. A story with no versions gets `DateTime.UtcNow`.

### Storyteller — `storytellers/<Name>.md`

```markdown
---
model: anthropic/claude-sonnet-4
temperature: 0.7
topK: 40
created: 2026-03-02T18:22:04.0000000Z
---

## System prompt

...

## Titling prompt

...
```

`topP`, `maxTokens`, `minP`, `topA` and `reasoningEffort` appear only when
non-null. The two prompts are multi-line, so they are body sections rather than
front-matter values. The file stem is the storyteller name.

Exporting storytellers in full retires the README's caveat that drafts
referencing an unknown storyteller are skipped because their prompt and tuning
were never captured.

### Draft — `drafts/<stamp> - <Title>.md`

```markdown
---
storyteller: Storyteller
title: A Night In The Fens
created: 2026-04-11T09:03:12.1234567Z
updated: 2026-04-12T20:14:55.0000000Z
---
```

Draft titles are model-generated and unconstrained, so `title` in front matter
is authoritative and the filename is a derived, sanitized convenience. The
`Model:` header is dropped: it was never stored on `Draft`, it was read off the
storyteller, and storytellers now export in full.

### Prompt — `library/<Category>/prompts/<Title>.md`

```markdown
---
created: 2026-06-04T11:00:00.0000000Z
updated: 2026-06-04T11:00:00.0000000Z
---

first message

---

second message
```

`PromptMessage` has no role, only `Content` and `SortOrder`, so messages are
separated by a line that is exactly `---` and ordered by position in the file.

### One-liners and tropes — `library/<Category>/one-liners.md`, `tropes.md`

```markdown
- first item
- second item, whose text
  runs onto another line
```

File order is display order. Continuation lines are indented two spaces and
dedented on read, so an item whose own text contains a line starting with `- `
is unambiguous without escaping.

### Settings — `settings.md`

`AppSettings` is an open key/value table, so the mapping is generic rather than
a hardcoded field list: every row becomes a front-matter key, except values
containing a newline, which become `## <Key>` body sections (this is what
`SummaryPrompt` hits). New settings are therefore covered automatically.

`OpenRouterApiKey` is omitted unless `--include-secrets` is passed. On import,
an absent key leaves the existing vault value untouched — restoring a redacted
archive never clears a working key.

## Escaping

Both delimiters can legitimately occur in content. The current format has this
bug already for `**Me:**`; since the format is being versioned, both are fixed
now so that the round-trip is genuinely lossless:

- A content line that exactly matches the turn-delimiter pattern is written
  prefixed with `\` and read back with the prefix removed.
- A content line that is exactly `---` inside a prompt message is written as
  `\---` and read back as `---`.

## Name sanitization

Path segments derive from user- or model-supplied text. Sanitization replaces
each run of `/`, `\` and control characters (U+0000–U+001F) with a single `-`,
trims leading whitespace and trailing whitespace, dots and dashes, and falls
back to `Untitled` when nothing survives. Collisions after sanitization get a
` (2)`, ` (3)` suffix, matching the convention already used by
`SillyTavernConvertService`.
Export warns on stderr whenever sanitization changed a name.

Sanitization is a safety net for data already in the vault. Going forward the
server prevents the situation: `LibraryEndpoints` gains validation for category
names and story titles, and `PromptEndpoints` and `StorytellerEndpoints` do the
same for prompt titles and storyteller names — the four values that become path
segments. Draft titles stay unvalidated, because a draft's authoritative title
lives in front matter.

The validation rejects exactly what sanitization would rewrite, so that an
accepted name is written to disk verbatim and the row never desyncs from its
directory: path separators and control characters anywhere in the name, a
leading whitespace character, a trailing whitespace character, `.` or `-`, and
the segments `.` and `..`. Endpoints validate the name they are about to store
— i.e. after trimming — so surrounding whitespace is still forgiven rather than
rejected. The rejection message names all of this; it is pinned by a test,
since a message that described only the separator half would leave a user
rejected for naming a category `Sci-Fi -` with nothing to act on.

## Import semantics

Default is the current behavior extended to the new kinds: create what is
missing, skip what exists, never delete. Matching is by path identity —
category name, story title plus version number, prompt title, storyteller name.
Drafts continue to dedupe on `(StorytellerId, Title, CreatedAt)`.

`--mirror` additionally deletes rows absent from the directory. Deletion order
is drafts, then storytellers, so a storyteller freed by a deleted draft can go;
a storyteller still referenced by a surviving draft is an error, not a cascade.
Settings are never deleted, since an absent key means "unspecified" rather than
"removed". Summaries on surviving stories are left alone — regenerable, but
there is no reason to discard one that is still valid.

A missing `one-liners.md` or `tropes.md` means "unspecified" under the default
merge (no-op) and "empty list" under `--mirror` (delete that category's items).
Export always writes both files, so a missing one in a real archive is a
deliberate hand-edit.

Because `--mirror` deletes user data, it prints the full list of what it will
remove and asks for confirmation on the console. `--yes` skips the prompt for
scripted use.

## Code structure

`CategoryExportService` and `CategoryImportService` are flat classes that mix
format knowledge with traversal, and would roughly triple in size if six kinds
were bolted on. They are renamed (neither has been category-scoped since drafts
landed) and the format primitives are extracted:

```
src/Fabulis.Cli/
  Archive/
    ArchiveLayout.cs        tree shape + sanitization: one source of truth for paths
    FrontMatter.cs          the YAML subset above, read and write
    ConversationFormat.cs   **Me:**/**StoryTeller:** turns, absorbing DraftMarkdownWriter
                            and CategoryImportService.ParseConversation
    ItemListFormat.cs       bullet lists with indented continuations
  VaultExporter.cs          traversal only, one method per kind
  VaultImporter.cs          traversal only, mirroring the exporter
```

The four `Archive/` types are pure functions over strings, so the format is
testable without a database. `DetectImportShape` and its enum are deleted;
detection is now a check for `library/`.

`Program.cs` gains flag parsing for `--mirror` and `--include-secrets`, which
the current pure-positional parser cannot express. `SillyTavernConvertService`
is updated to emit the new draft shape, or its output stops importing.

## Testing

There is no test project for the CLI. Add `tests/Fabulis.Cli.Tests`, built
test-first:

- **Round-trip equality** — the test that answers the question that started
  this. Build a vault holding one of every entity, export it, import into an
  empty vault, and compare field by field over a content projection that
  excludes the four documented lossy fields. Anything not exported fails here.
- **Adversarial content** — messages containing `---`, `**Me:**`, `**Paul:**`,
  CRLF line endings, trailing whitespace, and non-ASCII text.
- **Format primitives** — front-matter quoting and null omission, conversation
  turns, item-list continuations, sanitization and collision suffixes.
- **Empty shapes** — a category with only tropes; a story with no versions; an
  empty one-liners list.
- **`--mirror`** deletes absent rows, refuses a referenced storyteller, and
  leaves settings and summaries alone. Default merge deletes nothing.
- **Secrets** — key absent by default, present with `--include-secrets`, and a
  redacted import leaving an existing key intact.
- **`formatVersion`** — an unknown version is refused.

Server-side validation is tested in the existing `Fabulis.Server.Tests`.

## Known lossy fields

Stated explicitly so the round-trip test can assert them rather than paper over
them:

1. Row ids change on import; identity is the path.
2. `Story.CreatedAt` and `Category.CreatedAt` are derived from their earliest
   child rather than preserved.
3. Story summaries (`SummaryText`, `SummaryStatus`, `SummarizedThroughVersion`,
   `SummaryError`, `SummaryUpdatedAt`) are not exported and regenerate.
4. `OneLiner` and `Trope` `CreatedAt`/`UpdatedAt` reset to import time.
5. Names are path-derived, so a name needing sanitization is rewritten on
   round trip (e.g. "Sci-Fi / Fantasy" becomes "Sci-Fi - Fantasy"), and two
   names that sanitize to the same stem get a NameAllocator " (2)" suffix.
   Inherent to identity being the path.
6. Message `SortOrder` is reindexed to 0..n on import. Relative order always
   survives; the original absolute values do not.
7. Leading and trailing blank or whitespace-only lines in message content are
   stripped. Inner blank lines survive; the loss converges after one round.

## Out of scope

- Making the filesystem the actual system of record. This spec produces a
  format capable of it; the server still reads and writes SQLite.
- A file watcher or any live directory/vault synchronization.
- Client-side changes. Nothing in the SwiftUI app is affected.
