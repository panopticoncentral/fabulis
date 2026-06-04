using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class PromptService(FabulisDbContext db)
{
    public async Task<Prompt> CreatePromptAsync(int categoryId, string? title)
    {
        var now = DateTime.UtcNow;
        var prompt = new Prompt
        {
            CategoryId = categoryId,
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled Prompt" : title.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Prompts.Add(prompt);
        await db.SaveChangesAsync();
        return prompt;
    }

    public async Task<Category?> GetCategoryWithPromptsAsync(int categoryId)
    {
        return await db.Categories
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .FirstOrDefaultAsync(c => c.Id == categoryId);
    }

    public async Task<Prompt?> GetPromptAsync(int id)
    {
        return await db.Prompts
            .Include(p => p.Category)
            .Include(p => p.Messages)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Prompt?> UpdatePromptAsync(
        int id, string title, int categoryId, IReadOnlyList<string> messages)
    {
        var prompt = await db.Prompts
            .Include(p => p.Messages)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (prompt is null) return null;

        prompt.Title = string.IsNullOrWhiteSpace(title) ? "Untitled Prompt" : title.Trim();
        prompt.CategoryId = categoryId;
        prompt.UpdatedAt = DateTime.UtcNow;

        db.PromptMessages.RemoveRange(prompt.Messages);
        prompt.Messages = messages
            .Select((content, index) => new PromptMessage
            {
                Content = content,
                SortOrder = index,
            })
            .ToList();

        await db.SaveChangesAsync();
        return await GetPromptAsync(id);
    }

    public async Task<bool> CategoryExistsAsync(int categoryId)
    {
        return await db.Categories.AnyAsync(c => c.Id == categoryId);
    }

    public async Task<bool> DeletePromptAsync(int id)
    {
        var prompt = await db.Prompts
            .Include(p => p.Messages)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (prompt is null) return false;
        db.Prompts.Remove(prompt);
        await db.SaveChangesAsync();
        return true;
    }
}
