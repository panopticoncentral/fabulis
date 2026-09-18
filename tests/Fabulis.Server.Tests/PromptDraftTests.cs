using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class PromptDraftTests
{
    [Fact]
    public async Task StartingDraftCopiesOrderedMessagesWithoutChangingTemplate()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FabulisDbContext>().UseSqlite(connection).Options;
        await using var db = new FabulisDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var category = new Category { Name = "Ideas", CreatedAt = DateTime.UtcNow };
        db.Categories.Add(category);
        db.Storytellers.Add(new Storyteller { Name = "Writer", Prompt = "Write", TitlingPrompt = "Title", ModelName = "test", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var templates = new PromptService(db);
        var prompt = await templates.CreatePromptAsync(category.Id, "A beginning");
        await templates.UpdatePromptAsync(prompt.Id, prompt.Title, category.Id, ["First", "Second"]);
        var service = new DraftService(db);
        var draft = await service.CreateDraftFromPromptAsync(prompt.Id);
        Assert.NotNull(draft);
        Assert.Equal("A beginning", draft.Title);
        Assert.Equal(new[] { "First", "Second" }, draft.Messages.OrderBy(m => m.SortOrder).Select(m => m.Content));
        Assert.All(draft.Messages, m => Assert.Equal(MessageRole.Prompt, m.Role));
        await service.UpdateMessageContentAsync(draft.Messages.First().Id, "Changed draft");
        Assert.Equal("First", (await templates.GetPromptAsync(prompt.Id))!.Messages.OrderBy(m => m.SortOrder).First().Content);
        Assert.Null(await service.CreateDraftFromPromptAsync(-1));
    }
}
