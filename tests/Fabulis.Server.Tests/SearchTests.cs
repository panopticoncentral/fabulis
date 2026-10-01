using Fabulis.Server.Data;
using Fabulis.Server.Api;
using Fabulis.Server.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public sealed class SearchTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Password=synthetic-search-test");
    private FabulisDbContext db = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new FabulisDbContext(new DbContextOptionsBuilder<FabulisDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await db.EnsureSchemaUpdatedAsync();
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }

    private Task<int> Sql(string sql) => db.Database.ExecuteSqlRawAsync(sql);

    private Task Seed() => Sql("""
        INSERT INTO Categories (Id, Name, CreatedAt) VALUES (1, 'Nebula archive', '2026-01-01');
        INSERT INTO Stories (Id, CategoryId, Title, CreatedAt, SummaryStatus, SummaryText)
            VALUES (1, 1, 'The lighthouse', '2026-01-01', 'Ready', 'A nebula summary');
        INSERT INTO StoryVersions (Id, StoryId, VersionNumber, Origin, CreatedAt)
            VALUES (1, 1, 1, 'Imported', '2026-01-01'), (2, 1, 2, 'Imported', '2026-01-01');
        INSERT INTO StoryMessages (Id, StoryVersionId, Role, Content, SortOrder)
            VALUES (1, 1, 'Prompt', 'Find the nebula', 0), (2, 1, 'Response', 'A café on the moon', 1),
                   (3, 2, 'Response', 'An untouched island', 0);
        INSERT INTO Drafts (Id, StorytellerID, Title, CreatedAt, UpdatedAt)
            VALUES (1, 1, 'Unfinished voyage', '2026-01-01', '2026-01-01');
        INSERT INTO DraftMessages (Id, DraftId, Role, Content, SortOrder)
            VALUES (1, 1, 'Response', 'A nebula draft', 0);
        INSERT INTO Prompts (Id, CategoryId, Title, CreatedAt, UpdatedAt)
            VALUES (1, 1, 'Writing exercise', '2026-01-01', '2026-01-01');
        INSERT INTO PromptMessages (Id, PromptId, Content, SortOrder) VALUES (1, 1, 'Describe a nebula', 0);
        INSERT INTO OneLiners (Id, CategoryId, Text, CreatedAt, UpdatedAt)
            VALUES (1, 1, 'A nebula in a bottle', '2026-01-01', '2026-01-01');
        INSERT INTO Tropes (Id, CategoryId, Text, CreatedAt, UpdatedAt)
            VALUES (1, 1, 'Lost in a nebula', '2026-01-01', '2026-01-01');
        UPDATE Storytellers SET Prompt = 'Tell nebula tales' WHERE Id = 1;
        INSERT INTO AppSettings (Key, Value) VALUES ('SummaryPrompt', 'Summarize the nebula'), ('OpenRouterApiKey', 'secretcanary');
        """);

    [Fact]
    public async Task SearchesEveryContentKindWithHighlightedSnippetsAndExactVersion()
    {
        await Seed();
        var response = await SearchService.SearchAsync(db, "NEBU");
        Assert.Equal(new[] { "category", "draft", "oneLiner", "prompt", "story", "storyVersion", "storyteller", "summaryPrompt", "trope" },
            response.Results.Select(r => r.Kind).Order(StringComparer.Ordinal).ToArray());
        Assert.All(response.Results, r => Assert.Contains("\u0002", r.Snippet));
        var version = Assert.Single(response.Results, r => r.Kind == "storyVersion");
        Assert.Equal(1, version.ItemId);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("Nebula archive", version.CategoryName);
        Assert.Empty((await SearchService.SearchAsync(db, "secretcanary")).Results);
    }

    [Fact]
    public async Task TermsCanSpanTitleAndMessagesAndIgnoreAccents()
    {
        await Seed();
        var result = Assert.Single((await SearchService.SearchAsync(db, "lighthouse nebula cafe")).Results);
        Assert.Equal("storyVersion", result.Kind);
        Assert.Equal(1, result.VersionNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    [InlineData("* : \" () - +")]
    public async Task EmptyOrPunctuationQueriesAreEmpty(string input) =>
        Assert.Empty((await SearchService.SearchAsync(db, input)).Results);

    [Fact]
    public async Task FtsOperatorsAreLiteralWordsAndQueryLengthIsBounded()
    {
        Assert.Equal("\"hello\"* AND \"OR\"* AND \"world\"*", SearchService.BuildMatchQuery("hello OR \"world\"*"));
        await Seed();
        Assert.Empty((await SearchService.SearchAsync(db, "nebula OR missingword")).Results);
        await Assert.ThrowsAsync<ArgumentException>(() => SearchService.SearchAsync(db, new string('x', 501)));
    }

    [Fact]
    public async Task EditsMovesRenamesAndDeletesImmediatelyUpdateResults()
    {
        await Seed();
        await Sql("UPDATE StoryMessages SET Content = 'Replacement comet', StoryVersionId = 2 WHERE Id = 1;");
        Assert.DoesNotContain((await SearchService.SearchAsync(db, "nebula")).Results, r => r.Kind == "storyVersion");
        Assert.Equal(2, Assert.Single((await SearchService.SearchAsync(db, "comet")).Results).VersionNumber);
        await Sql("""
            INSERT INTO Categories (Id, Name, CreatedAt) VALUES (2, 'New home', '2026-01-01');
            UPDATE Stories SET Title = 'Beacon', CategoryId = 2, SummaryText = 'Fresh synopsis' WHERE Id = 1;
            UPDATE Categories SET Name = 'Renamed home' WHERE Id = 2;
            UPDATE DraftMessages SET Content = 'New draft words' WHERE Id = 1;
            UPDATE PromptMessages SET Content = 'New prompt words' WHERE Id = 1;
            UPDATE OneLiners SET Text = 'New miniature' WHERE Id = 1;
            UPDATE Tropes SET Text = 'New trope words' WHERE Id = 1;
            UPDATE Storytellers SET Prompt = 'New instructions' WHERE Id = 1;
            UPDATE AppSettings SET Value = 'New summary instructions' WHERE Key = 'SummaryPrompt';
            """);
        var moved = Assert.Single((await SearchService.SearchAsync(db, "beacon comet")).Results);
        Assert.Equal(2, moved.CategoryId);
        Assert.Equal("Renamed home", moved.CategoryName);
        Assert.Empty((await SearchService.SearchAsync(db, "lighthouse")).Results);
        Assert.Equal("category", Assert.Single((await SearchService.SearchAsync(db, "nebula")).Results).Kind);
        await Sql("DELETE FROM Categories; DELETE FROM Drafts; DELETE FROM AppSettings; DELETE FROM Storytellers;");
        Assert.Empty((await SearchService.SearchAsync(db, "new beacon nebula")).Results);
        Assert.Equal(0, await db.Database.SqlQueryRaw<long>("SELECT count(*) AS Value FROM SearchDocuments").SingleAsync());
        await Sql("INSERT INTO LibrarySearch(LibrarySearch, rank) VALUES ('integrity-check', 1);");
    }

    [Fact]
    public async Task BackfillsExistingDataOnceAndKeepsRollbackAtomic()
    {
        await Seed();
        // Simulate a pre-search vault; source content survives index removal.
        var triggers = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM sqlite_master WHERE type = 'trigger' AND (name LIKE 'Search_%' OR name LIKE 'SearchDocuments_%')").ToListAsync();
        foreach (var trigger in triggers) await Sql($"DROP TRIGGER \"{trigger}\";");
        await Sql("DROP TABLE LibrarySearch; DROP TABLE SearchDocuments;");
        await db.EnsureSchemaUpdatedAsync();
        var first = await SearchService.SearchAsync(db, "nebula");
        await db.EnsureSchemaUpdatedAsync();
        Assert.Equal(first.Results, (await SearchService.SearchAsync(db, "nebula")).Results);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await Sql("UPDATE OneLiners SET Text = 'Rollback marker';");
            await transaction.RollbackAsync();
        }
        Assert.Empty((await SearchService.SearchAsync(db, "rollback")).Results);
        Assert.Contains((await SearchService.SearchAsync(db, "nebula")).Results, r => r.Kind == "oneLiner");
    }

    [Fact]
    public async Task TitleMatchesRankFirstAndSummaryMatchesAreIdentified()
    {
        await Seed();
        await Sql("UPDATE Drafts SET Title = 'Quasar'; UPDATE OneLiners SET Text = 'A quasar';");
        var ranked = await SearchService.SearchAsync(db, "quasar");
        Assert.Equal("draft", ranked.Results[0].Kind);
        Assert.Equal("oneLiner", ranked.Results[1].Kind);
        Assert.True(Assert.Single((await SearchService.SearchAsync(db, "summary")).Results,
            r => r.Kind == "story").MatchInSummary);
        Assert.False(Assert.Single((await SearchService.SearchAsync(db, "lighthouse")).Results,
            r => r.Kind == "story").MatchInSummary);
    }

    [Fact]
    public async Task HttpSearchRequiresAnUnlockedSessionAndReturnsNavigableResults()
    {
        await Seed();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<SessionTokenStore>();
        builder.Services.AddSingleton<VaultService>();
        builder.Services.AddDbContext<FabulisDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddScoped<OneLinerService>();
        builder.Services.AddScoped<TropeService>();
        await using var app = builder.Build();
        var api = app.MapGroup("/api/v1");
        api.MapSearchEndpoints();
        api.MapOneLinerEndpoints();
        api.MapTropeEndpoints();
        api.MapStorytellerEndpoints();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/search?q=nebula")).StatusCode);
        var vault = app.Services.GetRequiredService<VaultService>();
        vault.Unlock("synthetic-search-test");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/search?q=nebula")).StatusCode);
        var token = app.Services.GetRequiredService<SessionTokenStore>().Issue();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await client.GetAsync("/api/v1/search?q=nebula&limit=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var page = (await response.Content.ReadFromJsonAsync<SearchResponse>())!;
        Assert.Equal(2, page.Results.Count);
        Assert.True(page.HasMore);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/search?q=" + new string('x', 501))).StatusCode);
        Assert.Equal("A nebula in a bottle", (await client.GetFromJsonAsync<OneLinerDto>("/api/v1/one-liners/1"))!.Text);
        Assert.Equal("Lost in a nebula", (await client.GetFromJsonAsync<TropeDto>("/api/v1/tropes/1"))!.Text);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/tropes/999")).StatusCode);
        await Sql("INSERT INTO Storytellers (Id, Name, Prompt, TitlingPrompt, ModelName, Temperature, CreatedAt) VALUES (2, 'Other storyteller', 'Other instructions', 'Other titles', 'synthetic/model', 0.7, '2026-01-01');");
        Assert.Equal(1, (await client.GetFromJsonAsync<StorytellerDto>("/api/v1/storyteller"))!.Id);
        Assert.Equal(2, (await client.GetFromJsonAsync<StorytellerDto>("/api/v1/storyteller?id=2"))!.Id);
        vault.Lock();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/search?q=nebula")).StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task PagesAreStableAndDoNotHideMatchesBeyondFirstPage()
    {
        await Seed();
        var all = await SearchService.SearchAsync(db, "nebula");
        var first = await SearchService.SearchAsync(db, "nebula", 4);
        var next = await SearchService.SearchAsync(db, "nebula", 4, 4);
        var last = await SearchService.SearchAsync(db, "nebula", 4, 8);
        Assert.True(first.HasMore);
        Assert.True(next.HasMore);
        Assert.False(last.HasMore);
        Assert.Equal(all.Results, first.Results.Concat(next.Results).Concat(last.Results));
    }
}
