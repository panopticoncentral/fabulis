using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class PromptEndpoints
{
    public static IEndpointRouteBuilder MapPromptEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/prompts").RequireSession();

        group.MapGet("/{id:int}", async (int id, PromptService prompts) =>
        {
            var prompt = await prompts.GetPromptAsync(id);
            return prompt is null ? Results.NotFound() : Results.Ok(ToDto(prompt));
        });

        group.MapPost("", async (CreatePromptRequest body, PromptService prompts) =>
        {
            if (!await prompts.CategoryExistsAsync(body.CategoryId))
                return Results.BadRequest(new { error = "category does not exist" });
            var prompt = await prompts.CreatePromptAsync(body.CategoryId, body.Title);
            var full = await prompts.GetPromptAsync(prompt.Id);
            return Results.Ok(ToDto(full!));
        });

        group.MapPut("/{id:int}", async (int id, UpdatePromptRequest body, PromptService prompts) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new { error = "title is required" });
            if (!await prompts.CategoryExistsAsync(body.CategoryId))
                return Results.BadRequest(new { error = "category does not exist" });
            var updated = await prompts.UpdatePromptAsync(id, body.Title, body.CategoryId, body.Messages);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapDelete("/{id:int}", async (int id, PromptService prompts) =>
        {
            return await prompts.DeletePromptAsync(id) ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    private static PromptDto ToDto(Prompt p) => new(
        p.Id,
        p.CategoryId,
        p.Category?.Name ?? "",
        p.Title,
        p.CreatedAt,
        p.UpdatedAt,
        p.Messages
            .OrderBy(m => m.SortOrder)
            .Select(m => new PromptMessageDto(m.Id, m.Content, m.SortOrder))
            .ToList());
}
