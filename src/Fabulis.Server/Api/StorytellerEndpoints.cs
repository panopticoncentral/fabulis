using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class StorytellerEndpoints
{
    public static IEndpointRouteBuilder MapStorytellerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/storyteller").RequireSession();

        group.MapGet("", async (int? id, FabulisDbContext db) =>
        {
            var s = await db.Storytellers.Where(x => id == null || x.Id == id).OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (s is null)
                return Results.NotFound();

            return Results.Ok(new StorytellerDto(
                s.Id, s.Name, s.Prompt, s.TitlingPrompt, s.ModelName,
                s.Temperature, s.TopP, s.MaxTokens, s.MinP, s.TopK, s.TopA,
                s.ReasoningEffort));
        });

        group.MapPut("", async (int? id, StorytellerUpdateRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) ||
                string.IsNullOrWhiteSpace(body.Prompt) ||
                string.IsNullOrWhiteSpace(body.ModelName))
            {
                return Results.BadRequest(new { error = "name, prompt, and modelName are required" });
            }
            // Validated as it will be stored, i.e. trimmed — see the note in
            // LibraryEndpoints' category create.
            var name = body.Name.Trim();
            if (!NameValidation.IsValidPathSegment(name))
                return Results.BadRequest(new { error = NameValidation.Error("name") });

            var s = await db.Storytellers.Where(x => id == null || x.Id == id).OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (s is null)
                return Results.NotFound();

            s.Name = name;
            s.Prompt = body.Prompt;
            s.TitlingPrompt = body.TitlingPrompt;
            s.ModelName = body.ModelName.Trim();
            s.Temperature = body.Temperature;
            s.TopP = body.TopP;
            s.MaxTokens = body.MaxTokens;
            s.MinP = body.MinP;
            s.TopK = body.TopK;
            s.TopA = body.TopA;
            s.ReasoningEffort = body.ReasoningEffort;

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }
}
