using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class OneLinerService(FabulisDbContext db)
{
    public async Task<OneLiner> CreateOneLinerAsync(int categoryId, string text)
    {
        var now = DateTime.UtcNow;
        var oneLiner = new OneLiner
        {
            CategoryId = categoryId,
            Text = text.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.OneLiners.Add(oneLiner);
        await db.SaveChangesAsync();
        return oneLiner;
    }

    public async Task<Category?> GetCategoryWithOneLinersAsync(int categoryId)
    {
        return await db.Categories
            .Include(c => c.OneLiners)
            .FirstOrDefaultAsync(c => c.Id == categoryId);
    }

    public async Task<OneLiner?> GetOneLinerAsync(int id)
    {
        return await db.OneLiners
            .Include(o => o.Category)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<OneLiner?> UpdateOneLinerAsync(int id, string text, int categoryId)
    {
        var oneLiner = await db.OneLiners.FirstOrDefaultAsync(o => o.Id == id);
        if (oneLiner is null) return null;

        oneLiner.Text = text.Trim();
        oneLiner.CategoryId = categoryId;
        oneLiner.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return await GetOneLinerAsync(id);
    }

    public async Task<bool> CategoryExistsAsync(int categoryId)
    {
        return await db.Categories.AnyAsync(c => c.Id == categoryId);
    }

    public async Task<bool> DeleteOneLinerAsync(int id)
    {
        var oneLiner = await db.OneLiners.FirstOrDefaultAsync(o => o.Id == id);
        if (oneLiner is null) return false;
        db.OneLiners.Remove(oneLiner);
        await db.SaveChangesAsync();
        return true;
    }
}
