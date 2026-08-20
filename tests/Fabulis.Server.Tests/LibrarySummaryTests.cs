using Fabulis.Server.Api;
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class LibrarySummaryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FabulisDbContext _db;

    public LibrarySummaryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<FabulisDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new FabulisDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static DateTime At(int day) => new(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

    private async Task<Category> SeedAsync(string name)
    {
        var category = new Category { Name = name, CreatedAt = At(1) };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    [Fact]
    public async Task CountsAndNewestEntriesPerCollection()
    {
        var category = await SeedAsync("Fairy Tales");
        _db.Stories.AddRange(
            new Story { CategoryId = category.Id, Title = "Older story", CreatedAt = At(1) },
            new Story { CategoryId = category.Id, Title = "Newest story", CreatedAt = At(3) },
            new Story { CategoryId = category.Id, Title = "Middle story", CreatedAt = At(2) });
        _db.Prompts.AddRange(
            new Prompt { CategoryId = category.Id, Title = "Older prompt", CreatedAt = At(1) },
            new Prompt { CategoryId = category.Id, Title = "Newest prompt", CreatedAt = At(4) });
        _db.OneLiners.Add(new OneLiner { CategoryId = category.Id, Text = "Only one-liner", CreatedAt = At(1) });
        _db.Tropes.AddRange(
            new Trope { CategoryId = category.Id, Text = "Older trope", CreatedAt = At(1) },
            new Trope { CategoryId = category.Id, Text = "Newest trope", CreatedAt = At(5) });
        await _db.SaveChangesAsync();

        var summary = Assert.Single(await LibraryEndpoints.CategorySummaries(_db).ToListAsync());

        Assert.Equal(category.Id, summary.Id);
        Assert.Equal("Fairy Tales", summary.Name);
        Assert.Equal(3, summary.StoryCount);
        Assert.Equal("Newest story", summary.LatestStoryTitle);
        Assert.Equal(2, summary.PromptCount);
        Assert.Equal("Newest prompt", summary.LatestPromptTitle);
        Assert.Equal(1, summary.OneLinerCount);
        Assert.Equal("Only one-liner", summary.LatestOneLinerText);
        Assert.Equal(2, summary.TropeCount);
        Assert.Equal("Newest trope", summary.LatestTropeText);
    }

    [Fact]
    public async Task EmptyCategoryReportsZeroCountsAndNullTitles()
    {
        await SeedAsync("Empty");

        var summary = Assert.Single(await LibraryEndpoints.CategorySummaries(_db).ToListAsync());

        Assert.Equal(0, summary.StoryCount);
        Assert.Null(summary.LatestStoryTitle);
        Assert.Null(summary.LatestPromptTitle);
        Assert.Null(summary.LatestOneLinerText);
        Assert.Null(summary.LatestTropeText);
    }

    [Fact]
    public async Task CategoriesAreOrderedByName()
    {
        await SeedAsync("Westerns");
        await SeedAsync("Adventures");
        await SeedAsync("Mysteries");

        var summaries = await LibraryEndpoints.CategorySummaries(_db).ToListAsync();

        Assert.Equal(["Adventures", "Mysteries", "Westerns"], summaries.Select(s => s.Name));
    }

    [Fact]
    public async Task TiesOnCreatedAtBreakByLowestId()
    {
        var category = await SeedAsync("Ties");
        _db.Stories.AddRange(
            new Story { CategoryId = category.Id, Title = "First inserted", CreatedAt = At(7) },
            new Story { CategoryId = category.Id, Title = "Second inserted", CreatedAt = At(7) });
        await _db.SaveChangesAsync();

        var summary = Assert.Single(await LibraryEndpoints.CategorySummaries(_db).ToListAsync());

        Assert.Equal("First inserted", summary.LatestStoryTitle);
    }

    [Fact]
    public async Task CountsAreScopedToTheirOwnCategory()
    {
        var fairyTales = await SeedAsync("Fairy Tales");
        var westerns = await SeedAsync("Westerns");
        _db.Stories.AddRange(
            new Story { CategoryId = fairyTales.Id, Title = "Cinderella", CreatedAt = At(1) },
            new Story { CategoryId = westerns.Id, Title = "High Noon", CreatedAt = At(2) },
            new Story { CategoryId = westerns.Id, Title = "Tombstone", CreatedAt = At(3) });
        await _db.SaveChangesAsync();

        var summaries = await LibraryEndpoints.CategorySummaries(_db).ToListAsync();

        Assert.Equal(1, summaries[0].StoryCount);
        Assert.Equal("Cinderella", summaries[0].LatestStoryTitle);
        Assert.Equal(2, summaries[1].StoryCount);
        Assert.Equal("Tombstone", summaries[1].LatestStoryTitle);
    }
}
