using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class TropeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FabulisDbContext _db;

    public TropeServiceTests()
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

    private async Task<Category> SeedCategoryAsync(string name = "Themes")
    {
        var cat = new Category { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return cat;
    }

    [Fact]
    public async Task CreateTropeTrimsAndStoresText()
    {
        var cat = await SeedCategoryAsync();
        var svc = new TropeService(_db);

        var trope = await svc.CreateTropeAsync(cat.Id, "  a haunted lighthouse  ");

        Assert.Equal("a haunted lighthouse", trope.Text);
        Assert.Equal(cat.Id, trope.CategoryId);
        Assert.NotEqual(default, trope.CreatedAt);
        Assert.Equal(trope.CreatedAt, trope.UpdatedAt);
    }

    [Fact]
    public async Task UpdateTropeChangesTextAndCategory()
    {
        var from = await SeedCategoryAsync("From");
        var to = await SeedCategoryAsync("To");
        var svc = new TropeService(_db);
        var trope = await svc.CreateTropeAsync(from.Id, "enemies to allies");

        var updated = await svc.UpdateTropeAsync(trope.Id, "  enemies to lovers  ", to.Id);

        Assert.NotNull(updated);
        Assert.Equal("enemies to lovers", updated!.Text);
        Assert.Equal(to.Id, updated.CategoryId);
        Assert.Equal("To", updated.Category.Name);
    }

    [Fact]
    public async Task UpdateTropeReturnsNullForUnknownId()
    {
        var svc = new TropeService(_db);

        var updated = await svc.UpdateTropeAsync(9999, "x", 1);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteTropeRemovesIt()
    {
        var cat = await SeedCategoryAsync();
        var svc = new TropeService(_db);
        var trope = await svc.CreateTropeAsync(cat.Id, "a locked room");

        var deleted = await svc.DeleteTropeAsync(trope.Id);

        Assert.True(deleted);
        Assert.Empty(await _db.Tropes.ToListAsync());
    }

    [Fact]
    public async Task CategoryExistsReflectsSeededState()
    {
        var cat = await SeedCategoryAsync();
        var svc = new TropeService(_db);

        Assert.True(await svc.CategoryExistsAsync(cat.Id));
        Assert.False(await svc.CategoryExistsAsync(9999));
    }

    [Fact]
    public async Task DeletingCategoryCascadesToTropes()
    {
        var cat = await SeedCategoryAsync();
        var svc = new TropeService(_db);
        await svc.CreateTropeAsync(cat.Id, "a haunted lighthouse");

        var loaded = await _db.Categories
            .Include(c => c.Tropes)
            .FirstAsync(c => c.Id == cat.Id);
        _db.Categories.Remove(loaded);
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.Tropes.ToListAsync());
    }
}
