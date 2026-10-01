# Directory storage implementation

The lossless conversation body codec and the first settings-storage boundary
are implemented. SQLite remains the active backend; no user library has been
migrated.

The decided architecture retains the Fabulis server and existing Swift client.
The server will use a directory as the sole source of truth and build an
in-memory catalog from it. Cryptomator may protect that directory externally;
API authentication remains a separate server responsibility. The earlier
direct Mac client proposal is obsolete. Dates on old design files do not
override the current decision.

## Implemented

- A dependency-free C# library in `src/Fabulis.Library`, for future migration or
  server storage code.
- Fixtures in `tests/Fixtures/conversation-body.json` pin exact UTF-8 output and logical content, including
  decomposed Unicode, mixed line endings, repeated backslashes, empty messages,
  whitespace-only messages, nonalternating roles, and nonsequential sort orders.
- Strict failures for malformed framing, missing/duplicate IDs, duplicate JSON
  keys, invalid sort orders, unescaped structural content, and unsupported keys.
- The first live server storage boundary: `IVaultStore`, currently implemented
  by `SqliteVaultStore` and registered in server dependency injection. All runtime
  settings reads and writes now pass through this interface, including settings
  endpoints, OpenRouter, narration, summary configuration, and auto-lock settings.
  Settings updates are complete atomic batches; omitted keys stay unchanged and
  unknown keys/empty values survive. Runtime effects occur after a successful save.

The exploratory Swift codec source was removed once the server architecture was
confirmed. The client will continue to use the server API.

These codecs only read/write conversation **bodies**. They do not accept complete
documents, validate document versions, access the filesystem, or substitute for
the existing archive codec. The document layer must validate kind/version before
calling them. Unknown message metadata is rejected until preservation is
implemented; it is never silently dropped during a rewrite.

## Body grammar

An empty conversation is an empty string. Each message is:

```text
<!-- fabulis:message {"id":"<UUID>","sortOrder":<Int32>} --> LF
<speaker label> LF LF
<escaped content> LF LF
<!-- fabulis:end --> LF
```

The labels are exactly `**Me:**` and `**StoryTeller:**`. Join complete message
blocks with one additional LF. There is no additional separator after the last
block. Consume exactly the framing bytes: never trim content or normalize its
newlines. File order is message order; retain `sortOrder` without sorting it.
IDs must be nonzero UUIDs, unique within a conversation. Writers emit lowercase
hyphenated UUIDs and metadata keys in `id`, `sortOrder` order. Readers accept
either key order, JSON string escapes, uppercase UUIDs, and JSON whitespace
within the single metadata line. Integers use JSON integer notation, without
fractional or exponent forms, and fit a signed 32-bit value.

For escaping, split content on LF while retaining every byte. On each line,
ignore leading backslashes and a single terminal CR only when testing whether
the line is reserved. Exact speaker labels and any line starting with
`<!-- fabulis:` are reserved, including inside code fences. Add one leading
backslash when writing; require and remove exactly one when reading. Do not
change the original line ending. This reserves future section/item comments too.
Metadata comments cannot contain raw double hyphens.

## Still needed

Extend `IVaultStore` to library content, drafts, storytellers, and search; define
the minimal live-format additions to the archive; implement the directory store
and in-memory catalog; run common storage contract tests against both backends;
add explicit export/cutover with validation before retiring SQLite. Keep the
existing API and client behavior wherever possible. Finalize server credentials
independently of Cryptomator. No real vault should be switched automatically.
Cryptomator mount behavior has not been tested.

Summaries must be preserved as durable content: the current app allows manual
summary edits, so treating all summary text as disposable cache would lose data.

## Verification

```sh
dotnet test tests/Fabulis.Cli.Tests
dotnet test tests/Fabulis.Server.Tests
```

Codec fixtures guard the body grammar. Server tests cover the active SQLite
adapter and existing API/service behavior. Legacy archive tests remain in place
for compatibility.
