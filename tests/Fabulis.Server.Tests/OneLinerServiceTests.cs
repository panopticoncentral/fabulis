using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class OneLinerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FabulisDbContext _db;

    public OneLinerServiceTests()
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

    private async Task<Category> SeedCategoryAsync(string name = "Openers")
    {
        var cat = new Category { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return cat;
    }

    [Fact]
    public async Task CreateOneLinerTrimsAndStoresText()
    {
        var cat = await SeedCategoryAsync();
        var svc = new OneLinerService(_db);

        var line = await svc.CreateOneLinerAsync(cat.Id, "  She set fire to the document.  ");

        Assert.Equal("She set fire to the document.", line.Text);
        Assert.Equal(cat.Id, line.CategoryId);
        Assert.NotEqual(default, line.CreatedAt);
        Assert.Equal(line.CreatedAt, line.UpdatedAt);
    }

    [Fact]
    public async Task UpdateOneLinerChangesTextAndCategory()
    {
        var from = await SeedCategoryAsync("From");
        var to = await SeedCategoryAsync("To");
        var svc = new OneLinerService(_db);
        var line = await svc.CreateOneLinerAsync(from.Id, "Original line.");

        var updated = await svc.UpdateOneLinerAsync(line.Id, "  Edited line.  ", to.Id);

        Assert.NotNull(updated);
        Assert.Equal("Edited line.", updated!.Text);
        Assert.Equal(to.Id, updated.CategoryId);
        Assert.Equal("To", updated.Category.Name);
    }

    [Fact]
    public async Task UpdateOneLinerReturnsNullForUnknownId()
    {
        var svc = new OneLinerService(_db);

        var updated = await svc.UpdateOneLinerAsync(9999, "x", 1);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteOneLinerRemovesIt()
    {
        var cat = await SeedCategoryAsync();
        var svc = new OneLinerService(_db);
        var line = await svc.CreateOneLinerAsync(cat.Id, "Doomed.");

        var deleted = await svc.DeleteOneLinerAsync(line.Id);

        Assert.True(deleted);
        Assert.Empty(await _db.OneLiners.ToListAsync());
    }

    [Fact]
    public async Task CategoryExistsReflectsSeededState()
    {
        var cat = await SeedCategoryAsync();
        var svc = new OneLinerService(_db);

        Assert.True(await svc.CategoryExistsAsync(cat.Id));
        Assert.False(await svc.CategoryExistsAsync(9999));
    }

    [Fact]
    public async Task DeletingCategoryCascadesToOneLiners()
    {
        var cat = await SeedCategoryAsync();
        var svc = new OneLinerService(_db);
        await svc.CreateOneLinerAsync(cat.Id, "Child line.");

        var loaded = await _db.Categories
            .Include(c => c.OneLiners)
            .FirstAsync(c => c.Id == cat.Id);
        _db.Categories.Remove(loaded);
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.OneLiners.ToListAsync());
    }
}
