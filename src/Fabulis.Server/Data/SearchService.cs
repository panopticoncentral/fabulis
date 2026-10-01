using System.Text.RegularExpressions;
using Fabulis.Server.Api;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public static partial class SearchService
{
    public const int MaxQueryLength = 500;
    public const int MaxPageSize = 100;

    // Treat operators and punctuation as ordinary input, not FTS query syntax.
    // Unicode letters/numbers plus combining marks preserve accented words.
    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}\p{M}]*")]
    private static partial Regex Words();

    internal static string BuildMatchQuery(string query) => string.Join(" AND ",
        Words().Matches(query).Select(m => $"\"{m.Value}\"*"));

    public static async Task<SearchResponse> SearchAsync(FabulisDbContext db, string query,
        int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        if (query.Length > MaxQueryLength) throw new ArgumentException("Search is limited to 500 characters.", nameof(query));
        var match = BuildMatchQuery(query);
        if (match.Length == 0) return new SearchResponse([], false);
        limit = Math.Clamp(limit, 1, MaxPageSize);
        offset = Math.Max(0, offset);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT d.Kind, d.EntityId, d.ItemId, d.Title, d.CategoryId, c.Name,
                       d.VersionNumber, snippet(LibrarySearch, -1, char(2), char(3), '…', 28),
                       d.Kind = 'story' AND instr(highlight(LibrarySearch, 1, char(2), char(3)), char(2)) > 0
                FROM LibrarySearch
                JOIN SearchDocuments d ON d.Id = LibrarySearch.rowid
                LEFT JOIN Categories c ON c.Id = d.CategoryId
                WHERE LibrarySearch MATCH @query
                ORDER BY bm25(LibrarySearch, 5.0, 1.0), d.Kind, d.EntityId
                LIMIT @limit OFFSET @offset
                """;
            foreach (var (name, value) in new (string, object)[] {
                ("@query", match), ("@limit", limit + 1), ("@offset", offset) })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            var results = new List<SearchResultDto>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(new SearchResultDto(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetString(7), reader.GetBoolean(8)));
            var hasMore = results.Count > limit;
            if (hasMore) results.RemoveAt(results.Count - 1);
            return new SearchResponse(results, hasMore);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
