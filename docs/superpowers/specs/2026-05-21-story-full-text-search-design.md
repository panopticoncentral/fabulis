# Full-text search over saved stories

**Date:** 2026-05-21
**Status:** Approved design

## Goal

Let the user type search terms, see a list of **story versions** whose content
contains those terms (story title + version + a highlighted snippet per row),
and tap a result to open that exact version in the reader.

Search covers **all message content** of a saved story version — both the
user's prompts and the assistant's story text. Drafts are explicitly out of
scope; only saved `Story` / `StoryVersion` data is searchable.

## Constraints from the existing architecture

- The server owns all story data in an encrypted SQLite database (SQLCipher via
  `SQLitePCLRaw.bundle_e_sqlcipher`). The SwiftUI client only ever fetches one
  category / story / version at a time over REST. **Search must be a
  server-side capability** — the client never holds the full corpus.
- FTS5 is compiled into the SQLCipher bundle (verified: `snippet()` works), so
  full-text indexing, ranked results, and highlighted snippets are available.
- Story tables (`Stories`, `StoryVersions`, `StoryMessages`) are created by
  `FabulisDbContext.Database.EnsureCreatedAsync()` on unlock with EF's default
  cascade foreign keys; the extra tables are then added by
  `EnsureSchemaUpdatedAsync()`. SQLite enforces those cascades (foreign_keys is
  on by default in Microsoft.Data.Sqlite), so deleting a category, story, or
  version deletes the underlying `StoryMessages` rows — which is what triggers
  index cleanup.
- Wire format: REST under `/api/v1/*`. Server DTOs are PascalCase records
  serialized to camelCase JSON by ASP.NET defaults; client Codable structs use
  camelCase property names.

## Decisions

| Question | Decision |
| --- | --- |
| What text matches | All message content (user prompts + assistant text) |
| Result unit | One row per matching **version** |
| Result detail | Story title + version + highlighted snippet |
| Search entry point | Global, across the whole library |
| Mechanism | SQLite **FTS5** virtual table maintained by triggers |
| Index granularity | One FTS row per `StoryMessage`, grouped to version in the query |
| Client UI | Search **sheet** presented from the Library toolbar |

Rejected alternatives: a `LIKE` scan (no ranking, hand-rolled snippet and
highlight code, dynamic multi-term SQL) and client-side search (the client
doesn't have the corpus).

## 1. Schema

Added to `FabulisDbContext.EnsureSchemaUpdatedAsync()` (idempotent, runs every
unlock). A standalone FTS5 table indexing message content, one FTS row per
`StoryMessage`, keyed by the message `Id` as `rowid`:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS StoryMessageSearch USING fts5(
    Content,
    StoryVersionId UNINDEXED,
    tokenize = 'unicode61 remove_diacritics 2'
);
```

Three triggers keep it in sync with `StoryMessages`. Because they fire on the
table itself, they cover every write path uniformly — API save, CLI bulk
import, and cascade deletes:

```sql
CREATE TRIGGER IF NOT EXISTS StoryMessages_ai AFTER INSERT ON StoryMessages BEGIN
  INSERT INTO StoryMessageSearch(rowid, Content, StoryVersionId)
  VALUES (new.Id, new.Content, new.StoryVersionId);
END;

CREATE TRIGGER IF NOT EXISTS StoryMessages_ad AFTER DELETE ON StoryMessages BEGIN
  DELETE FROM StoryMessageSearch WHERE rowid = old.Id;
END;

CREATE TRIGGER IF NOT EXISTS StoryMessages_au AFTER UPDATE ON StoryMessages BEGIN
  UPDATE StoryMessageSearch SET Content = new.Content WHERE rowid = old.Id;
END;
```

One-time backfill for data that predates the index: if `StoryMessageSearch` is
empty while `StoryMessages` is not, populate it once.

```sql
INSERT INTO StoryMessageSearch(rowid, Content, StoryVersionId)
SELECT Id, Content, StoryVersionId FROM StoryMessages;
```

The backfill is guarded by a `SELECT count(*) FROM StoryMessageSearch = 0`
check so it runs at most once and startup stays cheap thereafter.

**Why per-message rather than per-version:** keeping one FTS row per message
makes the triggers a trivial 1:1 mirror. Grouping up to the version level
happens in the search query. The only tradeoff is that a matched phrase cannot
span two messages (turn boundaries), which is acceptable.

## 2. Query sanitization

A pure helper turns free-typed user input into a safe FTS5 `MATCH` expression:

```csharp
static string BuildMatchQuery(string raw)
```

Rules:
- Split on whitespace; drop empty tokens.
- Wrap each token as a quoted FTS5 string literal (double any internal `"`),
  then append `*` for prefix matching, e.g. `light` → `"light"*` so it finds
  "lighthouse".
- Join tokens with a space → FTS5 implicit AND (every term must appear in a
  version).
- If the input is empty or all-whitespace, the endpoint returns an empty result
  list **without** running a `MATCH` query.

This helper is the unit of logic most worth testing in isolation.

## 3. Search endpoint

New `src/Fabulis.Server/Api/SearchEndpoints.cs`, mapped in `Program.cs`
alongside the other groups:

```
GET /api/v1/search?q={query}&limit={n}
```

- `RequireSession()` like the other story endpoints.
- `limit` defaults to 50.
- Empty `q` → `200 OK` with an empty `Results` list.

Runs a single raw SQL query through the DbContext connection
(`db.Database.GetDbConnection()`), mapping rows to the DTO manually (the FTS
table is not an EF entity):

```sql
SELECT s.Id, s.Title, s.CategoryId, c.Name, v.Id, v.VersionNumber, f.Snippet
FROM (
  SELECT StoryVersionId,
         snippet(StoryMessageSearch, 0, char(2), char(3), '…', 12) AS Snippet,
         min(bm25(StoryMessageSearch)) AS Rank
  FROM StoryMessageSearch
  WHERE StoryMessageSearch MATCH @q
  GROUP BY StoryVersionId
) f
JOIN StoryVersions v ON v.Id = f.StoryVersionId
JOIN Stories s       ON s.Id = v.StoryId
JOIN Categories c    ON c.Id = s.CategoryId
ORDER BY f.Rank
LIMIT @limit;
```

How it produces one ranked row per version with a relevant snippet:
- `WHERE … MATCH @q` returns every matching message row.
- `GROUP BY StoryVersionId` collapses to one row per version.
- `min(bm25(...))` selects the best-matching message in that version (FTS5
  `bm25` is more negative for better matches). SQLite's bare-column rule then
  returns the `snippet()` from precisely that best-matching row.
- `ORDER BY f.Rank` sorts versions by their best match.

`snippet()` wraps matched terms in `char(2)` / `char(3)` — STX/ETX control
characters chosen as sentinels because they won't appear in story prose. The
client converts them into highlighted runs.

New DTOs in `Dtos.cs`:

```csharp
public sealed record SearchResultDto(
    int StoryId,
    string StoryTitle,
    int CategoryId,
    string CategoryName,
    int VersionId,
    int VersionNumber,
    string Snippet);

public sealed record SearchResponse(IReadOnlyList<SearchResultDto> Results);
```

## 4. Client

- **`Models/APIDtos.swift`** — add camelCase `SearchResult` and `SearchResponse`
  Codable structs mirroring the server DTOs. `SearchResult` is `Identifiable`
  (by `versionId`).
- **`Services/FabulisAPIClient.swift`** — add
  `func search(query: String, limit: Int = 50) async throws -> SearchResponse`
  hitting `GET /search`. Extend the private `buildRequest` to accept optional
  URL query items so `q` is properly percent-encoded via
  `URLComponents.queryItems` rather than concatenated into the path.
- **`Views/Story/StoryView.swift`** — add an optional `initialVersion: Int?`
  init parameter. Provide an explicit `init` that seeds
  `_selectedVersion = State(initialValue: initialVersion)`. The existing
  `loadStory` logic already prefers an existing `selectedVersion` over the
  latest, so a search result opens its specific version while the default
  (latest) behavior is unchanged when `initialVersion` is nil.
- **New `Views/Search/SearchView.swift`** — presented as a `.sheet` from a
  search icon added to the `LibraryView` toolbar. Contains its own
  `NavigationStack` with `.searchable`, a debounced (~250 ms) query that calls
  the API, and a `List` of result rows. Each row shows the story title,
  "Version N", the category name, and the snippet rendered as an
  `AttributedString` where the STX/ETX markers become highlighted (bold/tinted)
  runs. Tapping a row pushes
  `StoryView(storyId:initialVersion:fallbackTitle:)` via `navigationDestination`.
  Empty query shows guidance; no results shows a `ContentUnavailableView`;
  errors are surfaced inline with a retry, consistent with the other views.

## 5. Testing

The repository currently has no test project; add a minimal one for the
server-side logic.

- **`BuildMatchQuery` unit tests**: quoting, prefix `*` appended, multi-token
  AND, internal quote escaping, empty/whitespace input, tokens with FTS special
  characters.
- **Search integration test**: seed a temp SQLCipher database with two stories
  / versions through `FabulisDbContext`, run the search query, and assert
  version-level grouping, rank ordering, and presence of the STX/ETX snippet
  markers. Confirms triggers populate the index and the grouping query behaves.
- **Manual end-to-end**: run the server, search from the simulator/Catalyst
  client, and confirm results open the correct version with highlighted
  snippets.

## Out of scope

- Searching drafts.
- Filtering search by category (global only for now).
- Fuzzy / typo-tolerant matching and stemming beyond the default tokenizer.
- Pagination beyond the fixed `limit` cap.
