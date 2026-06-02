using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class StorytellerEndpoints
{
    public static IEndpointRouteBuilder MapStorytellerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/storyteller").RequireSession();

        group.MapGet("", async (FabulisDbContext db) =>
        {
            var s = await db.Storytellers.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (s is null)
                return Results.NotFound();

            return Results.Ok(new StorytellerDto(
                s.Id, s.Name, s.Prompt, s.TitlingPrompt, s.ModelName,
                s.Temperature, s.TopP, s.MaxTokens, s.MinP, s.TopK, s.TopA));
        });

        group.MapPut("", async (StorytellerUpdateRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) ||
                string.IsNullOrWhiteSpace(body.Prompt) ||
                string.IsNullOrWhiteSpace(body.ModelName))
            {
                return Results.BadRequest(new { error = "name, prompt, and modelName are required" });
            }

            var s = await db.Storytellers.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (s is null)
                return Results.NotFound();

            s.Name = body.Name.Trim();
            s.Prompt = body.Prompt;
            s.TitlingPrompt = body.TitlingPrompt;
            s.ModelName = body.ModelName.Trim();
            s.Temperature = body.Temperature;
            s.TopP = body.TopP;
            s.MaxTokens = body.MaxTokens;
            s.MinP = body.MinP;
            s.TopK = body.TopK;
            s.TopA = body.TopA;

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }
}
