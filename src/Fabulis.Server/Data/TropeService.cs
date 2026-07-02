using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class TropeService(FabulisDbContext db)
{
    public async Task<Trope> CreateTropeAsync(int categoryId, string text)
    {
        var now = DateTime.UtcNow;
        var trope = new Trope
        {
            CategoryId = categoryId,
            Text = text.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Tropes.Add(trope);
        await db.SaveChangesAsync();
        return trope;
    }

    public async Task<Category?> GetCategoryWithTropesAsync(int categoryId)
    {
        return await db.Categories
            .Include(c => c.Tropes)
            .FirstOrDefaultAsync(c => c.Id == categoryId);
    }

    public async Task<Trope?> GetTropeAsync(int id)
    {
        return await db.Tropes
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Trope?> UpdateTropeAsync(int id, string text, int categoryId)
    {
        var trope = await db.Tropes.FirstOrDefaultAsync(t => t.Id == id);
        if (trope is null) return null;

        trope.Text = text.Trim();
        trope.CategoryId = categoryId;
        trope.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return await GetTropeAsync(id);
    }

    public async Task<bool> CategoryExistsAsync(int categoryId)
    {
        return await db.Categories.AnyAsync(c => c.Id == categoryId);
    }

    public async Task<bool> DeleteTropeAsync(int id)
    {
        var trope = await db.Tropes.FirstOrDefaultAsync(t => t.Id == id);
        if (trope is null) return false;
        db.Tropes.Remove(trope);
        await db.SaveChangesAsync();
        return true;
    }
}
