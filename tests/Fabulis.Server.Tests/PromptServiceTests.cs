using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class PromptServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FabulisDbContext _db;

    public PromptServiceTests()
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

    private async Task<Category> SeedCategoryAsync(string name = "Fairy Tales")
    {
        var cat = new Category { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return cat;
    }

    [Fact]
    public async Task CreatePromptUsesDefaultTitleWhenNull()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);

        var prompt = await svc.CreatePromptAsync(cat.Id, null);

        Assert.Equal("Untitled Prompt", prompt.Title);
        Assert.Equal(cat.Id, prompt.CategoryId);
        Assert.Empty(prompt.Messages);
    }

    [Fact]
    public async Task UpdatePromptReplacesMessagesAndReindexesSortOrder()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);
        var prompt = await svc.CreatePromptAsync(cat.Id, "Original");

        await svc.UpdatePromptAsync(prompt.Id, "Original", cat.Id, ["A", "B"]);
        var updated = await svc.UpdatePromptAsync(prompt.Id, "Renamed", cat.Id, ["C", "D", "E"]);

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated!.Title);
        Assert.Equal(
            new[] { "C", "D", "E" },
            updated.Messages.OrderBy(m => m.SortOrder).Select(m => m.Content).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, updated.Messages.OrderBy(m => m.SortOrder).Select(m => m.SortOrder).ToArray());
    }

    [Fact]
    public async Task DeletePromptRemovesItAndMessages()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);
        var prompt = await svc.CreatePromptAsync(cat.Id, "Doomed");
        await svc.UpdatePromptAsync(prompt.Id, "Doomed", cat.Id, ["X"]);

        var deleted = await svc.DeletePromptAsync(prompt.Id);

        Assert.True(deleted);
        Assert.Empty(await _db.Prompts.ToListAsync());
        Assert.Empty(await _db.PromptMessages.ToListAsync());
    }

    [Fact]
    public async Task CategoryExistsReturnsTrueForSeededCategory()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);

        Assert.True(await svc.CategoryExistsAsync(cat.Id));
    }

    [Fact]
    public async Task CategoryExistsReturnsFalseForUnknownId()
    {
        var svc = new PromptService(_db);

        Assert.False(await svc.CategoryExistsAsync(9999));
    }

    [Fact]
    public async Task DeletingCategoryCascadesToPrompts()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);
        var prompt = await svc.CreatePromptAsync(cat.Id, "Child");
        await svc.UpdatePromptAsync(prompt.Id, "Child", cat.Id, ["msg"]);

        var loaded = await _db.Categories
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .FirstAsync(c => c.Id == cat.Id);
        _db.Categories.Remove(loaded);
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.Prompts.ToListAsync());
        Assert.Empty(await _db.PromptMessages.ToListAsync());
    }
}
