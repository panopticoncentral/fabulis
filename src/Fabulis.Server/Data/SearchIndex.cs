using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

/// <summary>
/// A derived index inside the SQLCipher vault. Database triggers cover API writes,
/// CLI imports, message moves, and cascading deletes without application hooks.
/// Each conversation is one document so terms may match across its messages.
/// </summary>
internal static class SearchIndex
{
    private sealed record Source(string Kind, string Table, string Title, string Content,
        string ItemId = "x.Id", string CategoryId = "NULL", string Version = "NULL", string Join = "")
    {
        public string Select => $"SELECT '{Kind}', x.Id, {ItemId}, {CategoryId}, {Version}, {Title}, coalesce({Content}, '') FROM {Table} x {Join}";
    }

    private static string Messages(string table, string parent) =>
        $"(SELECT group_concat(Content, char(10)) FROM (SELECT Content FROM {table} WHERE {parent} = x.Id ORDER BY SortOrder, Id))";

    private static readonly Source[] Sources =
    [
        new("category", "Categories", "x.Name", "''", CategoryId: "x.Id"),
        new("story", "Stories", "x.Title", "x.SummaryText", CategoryId: "x.CategoryId"),
        new("storyVersion", "StoryVersions", "story.Title",
            Messages("StoryMessages", "StoryVersionId"), "x.StoryId",
            "story.CategoryId", "x.VersionNumber", "JOIN Stories story ON story.Id = x.StoryId"),
        new("draft", "Drafts", "coalesce(x.Title, 'Untitled Draft')", Messages("DraftMessages", "DraftId")),
        new("prompt", "Prompts", "x.Title", Messages("PromptMessages", "PromptId"), CategoryId: "x.CategoryId"),
        new("oneLiner", "OneLiners", "'One-liner'", "x.Text", CategoryId: "x.CategoryId"),
        new("trope", "Tropes", "'Trope'", "x.Text", CategoryId: "x.CategoryId"),
        new("storyteller", "Storytellers", "x.Name", "x.Prompt || char(10) || x.TitlingPrompt || char(10) || x.ModelName"),
    ];

    public static async Task EnsureCreatedAsync(FabulisDbContext db)
    {
        // The transaction makes schema, triggers, and the initial backfill atomic.
        // Never infer index existence from a row count: an empty library is valid.
        await using var transaction = await db.Database.BeginTransactionAsync();
        var exists = await db.Database.SqlQueryRaw<long>(
            "SELECT count(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'LibrarySearch'").SingleAsync();
        if (exists != 0)
        {
            await transaction.CommitAsync();
            return;
        }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE SearchDocuments (
                Id INTEGER PRIMARY KEY,
                Kind TEXT NOT NULL,
                EntityId INTEGER NOT NULL,
                ItemId INTEGER NOT NULL,
                CategoryId INTEGER,
                VersionNumber INTEGER,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                UNIQUE (Kind, EntityId)
            );
            CREATE VIRTUAL TABLE LibrarySearch USING fts5(
                Title, Content, content = 'SearchDocuments', content_rowid = 'Id',
                tokenize = 'unicode61 remove_diacritics 2'
            );
            CREATE TRIGGER SearchDocuments_insert AFTER INSERT ON SearchDocuments BEGIN
                INSERT INTO LibrarySearch(rowid, Title, Content) VALUES (new.Id, new.Title, new.Content);
            END;
            CREATE TRIGGER SearchDocuments_delete AFTER DELETE ON SearchDocuments BEGIN
                INSERT INTO LibrarySearch(LibrarySearch, rowid, Title, Content)
                    VALUES ('delete', old.Id, old.Title, old.Content);
            END;
            CREATE TRIGGER SearchDocuments_update AFTER UPDATE ON SearchDocuments BEGIN
                INSERT INTO LibrarySearch(LibrarySearch, rowid, Title, Content)
                    VALUES ('delete', old.Id, old.Title, old.Content);
                INSERT INTO LibrarySearch(rowid, Title, Content) VALUES (new.Id, new.Title, new.Content);
            END;
            """);

        foreach (var source in Sources)
        {
            await ExecuteSchemaAsync(db, Insert(source.Select) + ";");
            await AddTriggersAsync(db, source.Table, source.Kind,
                (row) => Refresh(source, $"{row}.Id"));
        }
        foreach (var (table, parent, kind) in new[] {
            ("StoryMessages", "StoryVersionId", "storyVersion"),
            ("DraftMessages", "DraftId", "draft"),
            ("PromptMessages", "PromptId", "prompt") })
        {
            var source = Sources.Single(s => s.Kind == kind);
            await AddTriggersAsync(db, table, kind, row => Refresh(source, $"{row}.{parent}"));
        }
        // A renamed/moved story also changes the title/category of its versions.
        await AddTriggersAsync(db, "Stories", "versions", row => Refresh(
            Sources.Single(s => s.Kind == "storyVersion"),
            $"SELECT Id FROM StoryVersions WHERE StoryId = {row}.Id"));

        // Settings are explicitly allowlisted: credentials and service URLs must
        // never enter the search index. Only user-written summary instructions do.
        const string summarySelect = "SELECT 'summaryPrompt', 0, 0, NULL, NULL, 'Summary instructions', Value FROM AppSettings WHERE Key = 'SummaryPrompt'";
        await ExecuteSchemaAsync(db, Insert(summarySelect) + ";");
        await AddTriggersAsync(db, "AppSettings", "summaryPrompt", row =>
            $"DELETE FROM SearchDocuments WHERE Kind = 'summaryPrompt' AND {row}.Key = 'SummaryPrompt';" +
            Insert(summarySelect + $" AND {row}.Key = 'SummaryPrompt'") + ";");
        await transaction.CommitAsync();
    }

    private static string Insert(string select) =>
        "INSERT INTO SearchDocuments (Kind, EntityId, ItemId, CategoryId, VersionNumber, Title, Content) " + select;

    private static string Refresh(Source source, string ids) =>
        $"DELETE FROM SearchDocuments WHERE Kind = '{source.Kind}' AND EntityId IN ({ids});" +
        Insert(source.Select + $" WHERE x.Id IN ({ids})") + ";";

    private static async Task AddTriggersAsync(FabulisDbContext db, string table, string name, Func<string, string> refresh)
    {
        foreach (var operation in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            var body = operation switch {
                "INSERT" => refresh("new"),
                "DELETE" => refresh("old"),
                _ => refresh("old") + refresh("new")
            };
            await ExecuteSchemaAsync(db,
                $"CREATE TRIGGER Search_{table}_{name}_{operation} AFTER {operation} ON {table} BEGIN {body} END;");
        }
    }

    // All SQL fragments above are internal schema definitions, never user input.
#pragma warning disable EF1002
    private static Task<int> ExecuteSchemaAsync(FabulisDbContext db, string sql) => db.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
}
