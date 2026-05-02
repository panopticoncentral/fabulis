using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class StoryEndpoints
{
    public static IEndpointRouteBuilder MapStoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/stories").RequireSession();

        group.MapGet("/{id:int}", async (int id, FabulisDbContext db) =>
        {
            var story = await db.Stories
                .Include(s => s.Category)
                .Include(s => s.Versions)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (story is null)
                return Results.NotFound();

            var dto = new StoryDto(
                story.Id,
                story.CategoryId,
                story.Category.Name,
                story.Title,
                story.CreatedAt,
                story.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new StoryVersionSummaryDto(v.Id, v.VersionNumber, v.ModelName, v.CreatedAt))
                    .ToList());

            return Results.Ok(dto);
        });

        group.MapGet("/{storyId:int}/versions/{version:int}", async (
            int storyId,
            int version,
            FabulisDbContext db) =>
        {
            var v = await db.StoryVersions
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.StoryId == storyId && x.VersionNumber == version);

            if (v is null)
                return Results.NotFound();

            var dto = new StoryVersionDto(
                v.Id,
                v.StoryId,
                v.VersionNumber,
                v.ModelName,
                v.CreatedAt,
                v.Messages
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new StoryMessageDto(m.Id, m.Role, m.Content, m.SortOrder))
                    .ToList());

            return Results.Ok(dto);
        });

        return routes;
    }
}
