# Mac direct client and directory library

Status: obsolete. Superseded by the current decision to retain the Fabulis server
and Swift client and replace server database storage with a directory. The text
below is historical and must not guide implementation. See
[the current implementation notes](../../directory-storage-progress.md).

## Agreed direction

- Ship a Mac-first client that opens a library directory directly.
- Remove the running Fabulis server and its database dependency from that client.
- Let Cryptomator, or the user's chosen filesystem protection, handle encryption.
  Fabulis opens the accessible directory and does not manage vault passwords.
- Include migration of the existing SQLCipher database in the initial release.
  Migration writes a new library and leaves the source database intact.
- Use Markdown for conversations, prompts, and other written content, retaining
  the familiar `**Me:**` / `**StoryTeller:**` conversation convention. Use
  structured configuration files where appropriate.

The choices below are proposed implementation details, not additional decisions
already approved by the user. The detailed Markdown grammar and credential
destination still need to be settled before the format is frozen. The Markdown
direction itself is agreed.

## Scope and application boundary

The Swift app owns library storage, drafts, story versions, generation, titling,
summaries, and direct calls to OpenRouter and the configured narration service.
Removing the Fabulis server does not remove those external service dependencies.
Existing behavior remains the reference unless a difference is identified here.

Separate the application into a library store, application services, external
service clients, and SwiftUI presentation. Storage and application services should
be independently testable without opening windows or making network requests.
No service should rely on an HTTP endpoint or database entity as its internal API.

Start with one open library per app instance and one writing app per library.
An in-memory catalog supports browsing and search; it is rebuilt from the files.
Persistent indexes, if later needed, are disposable and contain no unique data.
Concurrent editing of cloud-synced copies is outside the initial scope.

The existing Mac client uses Catalyst. Mac-first does not itself decide whether
to keep that target or introduce native macOS. Resolve that during the first
directory-access prototype, without combining it with an unrelated UI redesign.
iPhone/iPad directory-provider access and synchronization are deferred.

## Primary format proposal

Reuse the archive's organization into library, drafts, and storytellers. Give
the live library its own versioned format, distinct from the existing Markdown
archive version 1. A Markdown archive is not automatically a live library.

Drafts and story versions are Markdown documents containing ordered speaker
blocks. Prompts, storyteller instructions, summaries, one-liners, and tropes also
keep their written content in Markdown bodies. YAML front matter carries document
metadata; small Markdown comments carry per-block identity and metadata where
needed. There is no file per message and no second authoritative JSON copy of
the prose. JSON remains suitable for the root marker, settings, and recovery
records.

The existing archive codecs normalize line endings and trim blank edges. Those
are implementation choices to replace, not limitations of Markdown. The new
format must specify exact framing and escaping and prove content preservation
with shared C#/Swift fixtures before the existing codecs can be reused.

Proposed layout (names illustrate display labels; UUIDs are full identifiers):

```text
My Library/
  fabulis-library.json
  settings.json
  storytellers/
    Storyteller--<uuid>.md
  drafts/
    A new beginning--<uuid>.md
  library/
    Adventures--<uuid>/
      category.json
      one-liners.md
      tropes.md
      prompts/
        A strange visitor--<uuid>.md
      stories/
        The crossing--<uuid>/
          story.md
          versions/
            Version 1--<uuid>.md
  .fabulis/
    recovery/
    trash/
    migration/
```

`fabulis-library.json` holds `kind: "fabulis-library"`, `formatVersion: 1`, a
`libraryId`, and the library creation timestamp. It is an identity/version marker,
not a duplicate inventory of the directory tree. Opening requires this marker;
creating a library is a separate action.

`story.md` holds story metadata in front matter and its summary in the body.
An explicit summary-presence field distinguishes a null summary from an empty
one. `category.json` contains category metadata only. One-liner/trope documents
hold ordered entries with individual IDs and timestamps, rather than dropping
those fields as the current bullet-list export does. Storyteller documents use
named system-prompt and titling-prompt sections. All prose sections use the same
lossless framing principles as conversation turns.

### Conversation convention

The following illustrates the proposed complete version-document shape. The
additional metadata comments and closing markers are proposed grammar details,
not a change to the recognizable speaker labels:

```markdown
---
formatVersion: 2
kind: "story-version"
id: "a0f01d4e-cdd2-43d7-a5f9-859e331dd9e9"
revision: "b3902635-85b3-48fc-934a-24549d49a623"
storyId: "cdb077ed-b303-4a54-8f69-705e36c45f8d"
versionNumber: 1
origin: "Generated"
modelName: "model-name"
createdAt: "2026-09-23T10:00:00.0000000Z"
---

<!-- fabulis:message {"id":"3f091a72-a1a2-4374-b7a2-3e038cd12a36","sortOrder":0} -->
**Me:**

Tell me a story about a lighthouse.

<!-- fabulis:end -->

<!-- fabulis:message {"id":"86e448e0-6931-4c10-a3d2-3bd47e35c7cc","sortOrder":1} -->
**StoryTeller:**

The lighthouse had been dark for thirty years.

<!-- fabulis:end -->
```

The document's `formatVersion: 2` distinguishes this Markdown grammar from the
existing archive grammar. The root library format has its own version, initially
1. The root version defines which document versions it supports; neither reader
guesses a format from the presence of speaker labels.

Proposed rules to freeze with fixtures:

- Each message has a metadata comment, an exact speaker-label line, a body, and
  an explicit closing comment. Comments preserve message identity without
  turning IDs into visible prose. Use `Prompt`/`Response` internally and map them
  to `**Me:**`/`**StoryTeller:**`; messages need not alternate roles.
- Structural separators use LF. After the speaker label, exactly two LF bytes
  precede the encoded body; exactly two LF bytes follow it before the closing
  comment. Strip only these framing bytes, never arbitrary blank lines. An empty
  body and a body containing only whitespace remain distinguishable. The closing
  marker gives the final message the same boundary rule as every earlier one.
- Escape content lines that match reserved speaker labels or Fabulis control
  comments with one leading backslash. Recognize those patterns after any number
  of leading backslashes, so existing backslashes are preserved by adding and
  removing exactly one. The grammar must include all control markers, including
  section/item markers, and apply inside code fences as well as ordinary prose.
- Preserve body line endings, including mixed LF/CRLF and a final bare CR, while
  scanning structural boundaries. Never run a whole-file newline normalization
  or a generic Markdown renderer before decoding the content. Structural LF and
  payload line endings must be distinguishable through the fixed framing.
- Metadata comments use single-line JSON with values escaped so they cannot
  contain a raw `--` or close the HTML comment. Parse them as data, not executable
  or instruction content. Use the same strict key rules as other metadata.
- Files missing required framing or IDs do not silently become empty documents.
  Report the problem and leave the file intact. Adding unannotated turns in an
  external editor requires an explicit import/reconciliation step that assigns
  IDs; ordinary prose edits inside existing blocks require no metadata edits.
- Legacy imported files keep a separate reader. Their absent metadata may be
  supplied during an explicit conversion, but their historical text normalization
  must not be used for database migration or new library writes.

Prompt messages use analogous role-free blocks. Storyteller sections and summary
bodies use named blocks; one-liners and tropes use individually annotated item
blocks. Freeze exact examples for these forms before implementation. Their
schemas must preserve null/empty distinctions, timestamps, and duplicate items
without storing a parallel copy of the prose in metadata.

### Encoding and identity rules

- UTF-8 Markdown with YAML front matter for written documents; UTF-8 JSON for
  configuration and bookkeeping. Use a documented YAML subset with quoted
  strings, explicit nulls, numeric scalars, and case-sensitive keys; disable
  implicit date conversion, tags, aliases, and duplicate keys. Do not inherit
  the old scalar parser's null/omission or string-escaping assumptions. Reject
  duplicate JSON keys too, and reject unsupported versions. Enum values match
  the current model; unknown values are not silently replaced with defaults.
- Each entity and nested message/item has a stable UUID `id`. References use IDs,
  never names or paths. Each document also has a `kind` and `revision` UUID.
  A revision changes on each app write; a content fingerprint detects external
  edits even when an outside editor did not change the revision.
- File names are readable labels plus IDs. Stored names and titles are exact and
  authoritative. Filename sanitization must not alter the stored display value.
  Limit label lengths and avoid case-only collisions and reserved filenames.
- Parent IDs and physical containment must agree. Moving a file outside the app
  into a different category requires explicit reconciliation; it does not
  silently change relationships. Renaming within the same parent preserves ID.
- Preserve strings exactly after decoding, including CRLF versus LF, Unicode,
  whitespace, empty strings, and null versus empty. Do not trim during migration.
- Store UTC timestamps as round-trip ISO 8601 strings preserving the original
  .NET tick precision. Swift must retain the original precision for unchanged
  values rather than round-tripping them through a lower-precision display date.
- Message blocks are displayed in file order. Migration orders by `SortOrder`,
  then original row ID for ties, and retains the original `sortOrder` value.
  Nested one-liner/trope items retain IDs and timestamps, including duplicates.
- Preserve unknown fields when editing a supported document, or refuse to write
  that document. Malformed documents remain untouched and produce an actionable
  error. An incomplete scan must never be interpreted as authoritative deletion.
- Do not traverse symlinks outside the selected root. IDs must be unique across
  the library; duplicate IDs or dangling references block affected writes.

## Database preservation contract

This table covers all twelve current `FabulisDbContext` sets. Database integer
IDs become stable UUIDs through a recorded table-and-row-ID mapping. Foreign
keys are translated through that mapping. Original IDs remain in the migration
record for audit, not as a runtime requirement.

| Source | Destination | Fields and relationships preserved |
| --- | --- | --- |
| Categories | `category.json` | Name, CreatedAt; ID |
| Stories | `story.md` front matter and summary body | CategoryId, Title, CreatedAt, SummaryText, SummaryStatus, SummarizedThroughVersion, SummaryError, SummaryUpdatedAt; ID |
| StoryVersions | version document | StoryId, VersionNumber, Origin, nullable ModelName, CreatedAt; ID |
| StoryMessages | version speaker blocks | Role, Content, SortOrder, containing StoryVersionId; ID |
| Drafts | draft document | StorytellerID, nullable Title, CreatedAt, UpdatedAt; ID |
| DraftMessages | draft speaker blocks | Role, Content, SortOrder, containing DraftId; ID |
| Prompts | prompt document | CategoryId, Title, CreatedAt, UpdatedAt; ID |
| PromptMessages | prompt content blocks | Content, SortOrder, containing PromptId; ID |
| OneLiners | category `one-liners.md` item blocks | CategoryId, Text, CreatedAt, UpdatedAt; ID |
| Tropes | category `tropes.md` item blocks | CategoryId, Text, CreatedAt, UpdatedAt; ID |
| Storytellers | storyteller document | Name, Prompt, TitlingPrompt, ModelName, Temperature, TopP, MaxTokens, MinP, TopK, TopA, ReasoningEffort, CreatedAt; ID |
| AppSettings | settings and credential disposition below | Every Key/Value, including unknown keys and empty values |

All names, titles, and strings survive independently of filesystem labels.
Empty categories, stories with no versions, empty conversations, duplicate
titles, duplicate item text, and multiple storytellers survive. Version file
IDs prevent duplicate version numbers from overwriting each other. Preserve
and report such duplicates; require reconciliation before allocating new version
numbers for an ambiguous story.

Summaries are durable content: the app allows manual editing and the database
does not distinguish those edits from generated text. Preserve every summary
and its status; do not automatically regenerate it on first open. Existing
failed state and error text are preserved without automatically retrying during
migration. SummarizedThroughVersion remains the original number, even when
duplicate version numbers require review.

### Settings and credentials

Preserve the exact settings dictionary rather than enumerating only currently
recognized keys. Activate `SummaryModel`, `SummaryPrompt`, `NarrationVoice`,
`NarrationSpeed`, and `KokoroBaseUrl` where applicable. A migrated narration URL
may be unreachable from the Mac; report connection failure without rewriting it.

Retain `AutoLockMinutes` as an inactive legacy setting. It must not be treated
as authority to lock Cryptomator. Server session tokens, narration tokens, and
in-flight tasks are transient state, not database content to migrate.

Recommended credential destination is the Mac Keychain. A directory alone then
contains the library, but credentials require separate setup on another device.
This is an explicit exception to complete directory portability.

For migration, propose a credential handoff file under `.fabulis/migration/`,
inside the selected destination, containing the exact OpenRouter key. The first
app open offers transfer to Keychain and removes that file only after successful
write and read-back verification. Until then the migration report labels the
credential transfer pending. No key, password, or credential hash appears in
logs or the ordinary report. Failure leaves the recoverable handoff intact.
Cryptomator does not protect a destination outside its mounted directory.

Alternative for review: keep credentials in the library settings permanently,
which meets complete directory portability. Do not silently omit the existing
key or claim credential migration is complete merely because the content was
converted. Unknown setting values remain intact; do not assume they are safe to
print in reports.

## Safe writes and library lifecycle

Use one serialized writer for app mutations. Acquire exclusive local library
ownership when opening for edits; a second local instance must not write. This
does not provide a distributed lock across cloud-synced copies. Require sync
completion before switching editing devices, and report conflict copies.

Before saving, verify the library ID, availability, and expected document
fingerprint. Refuse stale saves and preserve the user's unsaved edits for
reconciliation. Stage a replacement beside its destination, flush it, and
replace using supported filesystem operations. Validate replacement and
durability behavior on both a normal Mac directory and the intended Cryptomator
mount; a rename alone is not proof of crash durability on every mount type.

Multi-document operations use recoverable, idempotent operation records under
`.fabulis/recovery/`. Saving a draft to the library first commits the complete
new version, then any required story metadata, and only then retires the draft.
An operation ID connects all steps so recovery cannot create duplicate versions.
On restart, validate and complete or roll back the recorded operation. Interrupted
operations must leave at least one recoverable copy of the user's content.
Moves, category deletion, and storyteller reference changes need equivalent
recovery rules. Deletion initially moves content into library-local trash.

Treat directory loss or a missing marker as unavailable, not empty. Cancel
generation and summary tasks and stop filesystem writes. Never recreate a missing
mount path. If a save fails after the mount disappears, retain the pending text
in memory, visibly mark it unsaved, and offer retry after reopening the same
library or explicit Save As. Never silently spill it into a plaintext temp file.
An abrupt process termination can lose these unsaved in-memory changes; do not
claim otherwise.

Generation belongs to the app lifecycle. On an ordinary close or quit, cancel
work, await task completion, and save partial responses before releasing the
library. If saving fails, keep the close pending for retry or an explicit user
decision. Periodically checkpoint partial responses while the library is
available. Bind background results to the library ID and source revision so a
late callback cannot write into a newly opened library or overwrite newer edits.

Keep content-bearing recovery files and any persistent caches inside the library.
Library close releases content and active tasks from app state. It is not a
promise of cryptographic memory erasure or a command to lock Cryptomator.

## One-time migration

Retain the C# SQLCipher-capable CLI as a migration utility. The new Swift app
does not need SQLCipher or the running C# server. Add a dedicated migration
command; do not run the current Markdown exporter and label it lossless.

1. Stop writes through the old app/server for final cutover. Prompt for the
   database password without putting it in command arguments or logs. Open a
   consistent read-only SQLite snapshot through its supported API, including
   committed WAL contents. Do not copy only the main `.db` file or run schema
   creation, default seeding, or upgrade SQL against the source. If a particular
   source needs repair or upgrade, operate on a verified backup copy separately.
2. Inspect schema and validate references. Account explicitly for every source
   application table and column. Unknown schema must produce an unsupported
   schema result rather than silently dropping data; older supported schemas
   use explicit adapters. Missing references are reported, never guessed.
3. Require a new destination. Create staging inside that destination so temporary
   content receives the same filesystem protection. Do not emit a ready library
   marker yet. Allocate IDs, write all documents, and record mappings and any
   filename changes. Do not merge into an existing library.
4. Read the documents back independently and compare the complete logical model
   with the same source snapshot. Check all strings, nullable values, timestamps,
   numeric values, relationships, message order, IDs via mapping, and counts.
   Explicitly account for credentials and inactive legacy settings. Refuse
   success on any unexplained discrepancy or skipped row.
5. Write a report inside `.fabulis/migration/` with source schema description,
   source snapshot fingerprint excluding secret values, counts, ID mappings,
   filename substitutions, anomalies, validation outcome, and credential status.
   Protect the report as library content; redact console output by default.
6. Publish the root marker last, once content validation succeeds. Interrupted
   staging remains identifiable and cannot be opened as a complete library.
   A retry uses a fresh destination or explicitly discards only its own staging;
   it never overwrites a valid library. A Swift read/validation pass on first
   open must also succeed before the app offers ordinary edits.

The original encrypted database remains the rollback artifact. Migration never
deletes it. Rolling back later does not automatically bring new directory edits
back into the old database. Full read-back equality is required for content;
credential transfer has its separately visible completion state.

## Delivery and validation checkpoints

1. Freeze the detailed Markdown framing, escaping, metadata schemas, and small
   shared C#/Swift fixtures before writing the store. Document concrete sample
   files for every document kind. Resolve credential portability separately;
   Markdown as the written-content format is already agreed.
2. Build the lossless converter and independent validators. Exercise empty and
   duplicate records, every nullable field, exact whitespace and Unicode,
   literal speaker labels and control comments, repeated backslashes, code
   fences, empty final messages, mixed line endings, timestamp precision, all
   sampling settings, manually
   edited summaries, WAL snapshots, invalid relationships, and interrupted
   migration. Existing archive snapshot tests are not sufficient evidence.
3. Build the Mac directory-access slice: open a converted library, edit a draft,
   generate directly, and save a version with recovery. Test stale edits, mount
   loss, interrupted multi-file saves, and actual Cryptomator mount behavior.
4. Port the remaining browsing, editing, prompts, one-liners, tropes, storytellers,
   titling, summaries, narration, and settings behavior. Review generation and
   close behavior as an intentional consequence of removing the server.
5. Validate an explicitly selected real database, compare all content, and review
   the new app against that library before retiring the server workflow. Do not
   remove the old server or archive tools before this checkpoint.

## Evidence checked for this proposal

- `src/Fabulis.Server/Data/FabulisDbContext.cs` and its twelve entity types:
  persisted schema and relationships.
- `src/Fabulis.Server/Api/StoryEndpoints.cs`: summaries can be manually edited.
- `src/Fabulis.Server/Api/SettingsEndpoints.cs`: current settings keys.
- `src/Fabulis.Server/Data/DraftService.cs` and `GenerationManager.cs`: save
  sequence, streamed work, and partial-response persistence.
- `src/Fabulis.Cli/VaultExporter.cs`, `Archive/*.cs`, and
  `tests/Fabulis.Cli.Tests/VaultSnapshot.cs`: current export omissions, filename
  identity, duplicate-version handling, and text normalization.
- `docs/superpowers/specs/2026-09-05-vault-archive-format-design.md`: original
  archive intent and deliberately lossy fields. This proposal does not change
  the implemented archive contract.
- `client/Fabulis.xcodeproj/project.pbxproj` and
  `client/Fabulis/Services/KeychainService.swift`: current Catalyst target and
  server-oriented credential storage.

No existing database was opened or converted while preparing this proposal.
