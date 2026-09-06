using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class LibraryEndpoints
{
    /// <summary>
    /// The library summary needs only a count and the newest title/text per collection,
    /// so this projects those directly instead of loading every story, prompt, one-liner,
    /// and trope. Ties on CreatedAt break by ascending Id, matching the order the rows
    /// came back in when this was an in-memory sort over included collections.
    /// </summary>
    internal static IQueryable<CategorySummaryDto> CategorySummaries(FabulisDbContext db) =>
        db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategorySummaryDto(
                c.Id,
                c.Name,
                c.CreatedAt,
                c.Stories.Count,
                c.Stories
                    .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
                    .Select(s => s.Title)
                    .FirstOrDefault(),
                c.Prompts.Count,
                c.Prompts
                    .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
                    .Select(p => p.Title)
                    .FirstOrDefault(),
                c.OneLiners.Count,
                c.OneLiners
                    .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
                    .Select(o => o.Text)
                    .FirstOrDefault(),
                c.Tropes.Count,
                c.Tropes
                    .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
                    .Select(t => t.Text)
                    .FirstOrDefault()));

    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("").RequireSession();

        group.MapGet("/library", async (FabulisDbContext db) =>
        {
            var categories = await CategorySummaries(db).ToListAsync();
            return Results.Ok(new LibraryResponse(categories));
        });

        group.MapGet("/categories/{id:int}", async (int id, FabulisDbContext db) =>
        {
            var category = await db.Categories
                .Include(c => c.Stories)
                    .ThenInclude(s => s.Versions)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category is null)
                return Results.NotFound();

            var dto = new CategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.Stories
                    .OrderBy(s => s.Title)
                    .Select(s => new StorySummaryDto(s.Id, s.Title, s.CreatedAt, s.Versions.Count))
                    .ToList());

            return Results.Ok(dto);
        });

        group.MapGet("/categories/{id:int}/prompts", async (int id, PromptService prompts) =>
        {
            var category = await prompts.GetCategoryWithPromptsAsync(id);
            if (category is null)
                return Results.NotFound();

            var dto = new PromptCategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.Prompts
                    .OrderBy(p => p.Title)
                    .Select(p => new PromptSummaryDto(p.Id, p.Title, p.CreatedAt, p.Messages.Count))
                    .ToList());

            return Results.Ok(dto);
        });

        group.MapGet("/categories/{id:int}/one-liners", async (int id, OneLinerService oneLiners) =>
        {
            var category = await oneLiners.GetCategoryWithOneLinersAsync(id);
            if (category is null)
                return Results.NotFound();

            var dto = new OneLinerCategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.OneLiners
                    .OrderByDescending(o => o.CreatedAt)
                    .Select(o => new OneLinerSummaryDto(o.Id, o.Text, o.CreatedAt))
                    .ToList());

            return Results.Ok(dto);
        });

        group.MapGet("/categories/{id:int}/tropes", async (int id, TropeService tropes) =>
        {
            var category = await tropes.GetCategoryWithTropesAsync(id);
            if (category is null)
                return Results.NotFound();

            var dto = new TropeCategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.Tropes
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => new TropeSummaryDto(t.Id, t.Text, t.CreatedAt))
                    .ToList());

            return Results.Ok(dto);
        });

        group.MapPost("/categories", async (CreateCategoryRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest(new { error = "name is required" });
            // Validate what will actually be stored. The validator rejects
            // surrounding whitespace (Sanitize would strip it, desyncing the
            // row from its directory), so checking the raw value would turn a
            // trailing space — long accepted and quietly trimmed — into a 400.
            var name = body.Name.Trim();
            if (!NameValidation.IsValidPathSegment(name))
                return Results.BadRequest(new { error = NameValidation.Error("name") });
            var cat = new Category { Name = name, CreatedAt = DateTime.UtcNow };
            db.Categories.Add(cat);
            await db.SaveChangesAsync();
            return Results.Ok(new CategorySummaryDto(cat.Id, cat.Name, cat.CreatedAt, 0, null, 0, null, 0, null, 0, null));
        });

        group.MapPut("/categories/{id:int}", async (int id, RenameCategoryRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest(new { error = "name is required" });
            var name = body.Name.Trim();
            if (!NameValidation.IsValidPathSegment(name))
                return Results.BadRequest(new { error = NameValidation.Error("name") });
            var cat = await db.Categories.FindAsync(id);
            if (cat is null) return Results.NotFound();
            cat.Name = name;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/categories/{id:int}", async (int id, FabulisDbContext db) =>
        {
            var cat = await db.Categories.Include(c => c.Stories).FirstOrDefaultAsync(c => c.Id == id);
            if (cat is null) return Results.NotFound();
            db.Categories.Remove(cat);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }
}
