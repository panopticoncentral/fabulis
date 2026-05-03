using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class DraftEndpoints
{
    private static readonly JsonSerializerOptions EnvelopeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static IEndpointRouteBuilder MapDraftEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/drafts").RequireSession();

        group.MapGet("", async (DraftService drafts) =>
        {
            var all = await drafts.GetDraftsAsync();
            var dto = all.Select(d => new DraftSummaryDto(
                d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.Messages.Count)).ToList();
            return Results.Ok(dto);
        });

        group.MapPost("", async (DraftService drafts) =>
        {
            var draft = await drafts.CreateDraftAsync();
            var loaded = await drafts.GetDraftAsync(draft.Id);
            return Results.Ok(ToDto(loaded!));
        });

        group.MapGet("/{id:int}", async (int id, DraftService drafts) =>
        {
            var draft = await drafts.GetDraftAsync(id);
            return draft is null ? Results.NotFound() : Results.Ok(ToDto(draft));
        });

        group.MapDelete("/{id:int}", async (int id, DraftService drafts) =>
        {
            await drafts.DeleteDraftAsync(id);
            return Results.NoContent();
        });

        group.MapPost("/{id:int}/messages", async (
            int id,
            StreamPromptRequest body,
            DraftService drafts,
            OpenRouterService openRouter,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Prompt))
                return Results.BadRequest(new { error = "prompt is required" });

            var draft = await drafts.GetDraftAsync(id);
            if (draft is null) return Results.NotFound();

            await drafts.AddMessageAsync(id, MessageRole.Prompt, body.Prompt.Trim());
            draft = await drafts.GetDraftAsync(id);
            if (draft is null) return Results.NotFound();

            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var content = new StringBuilder();
            var storyteller = draft.Storyteller;

            try
            {
                await foreach (var chunk in openRouter.ChatStreamAsync(
                    storyteller.ModelName,
                    storyteller.Prompt,
                    draft.Messages.ToList(),
                    storyteller.Temperature,
                    storyteller.TopP,
                    storyteller.MaxTokens,
                    storyteller.MinP,
                    storyteller.TopK,
                    storyteller.TopA,
                    ct))
                {
                    var isReasoning = chunk.Kind == StreamChunkKind.Reasoning;
                    if (!isReasoning) content.Append(chunk.Text);
                    await WriteEnvelope(http, new StreamEnvelope("chunk", chunk.Text, isReasoning, null), ct);
                }

                int? savedId = null;
                if (content.Length > 0)
                {
                    var saved = await drafts.AddMessageAsync(id, MessageRole.Response, content.ToString());
                    savedId = saved.Id;
                }
                await WriteEnvelope(http, new StreamEnvelope("done", null, null, savedId), ct);
            }
            catch (OperationCanceledException)
            {
                if (content.Length > 0)
                {
                    var saved = await drafts.AddMessageAsync(id, MessageRole.Response, content.ToString());
                    await WriteEnvelope(http, new StreamEnvelope("done", null, null, saved.Id), CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                await WriteEnvelope(http, new StreamEnvelope("error", ex.Message, null, null), CancellationToken.None);
            }

            return Results.Empty;
        });

        group.MapPost("/{id:int}/save", async (
            int id,
            SaveDraftRequest body,
            DraftService drafts,
            FabulisDbContext db) =>
        {
            var draft = await drafts.GetDraftAsync(id);
            if (draft is null) return Results.NotFound();

            int categoryId;
            if (body.CategoryId is int existingCategoryId)
            {
                categoryId = existingCategoryId;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.NewCategoryName))
                    return Results.BadRequest(new { error = "categoryId or newCategoryName is required" });
                var newCat = new Category { Name = body.NewCategoryName.Trim(), CreatedAt = DateTime.UtcNow };
                db.Categories.Add(newCat);
                await db.SaveChangesAsync();
                categoryId = newCat.Id;
            }

            int? storyId = body.StoryId;
            string? newStoryTitle = body.NewStoryTitle?.Trim();

            if (storyId is null && string.IsNullOrWhiteSpace(newStoryTitle))
                return Results.BadRequest(new { error = "storyId or newStoryTitle is required" });

            var version = await drafts.SaveToLibraryAsync(id, categoryId, storyId, newStoryTitle);
            return Results.Ok(new SaveDraftResponse(version.StoryId, version.Id, version.VersionNumber));
        });

        group.MapDelete("/{draftId:int}/messages/{messageId:int}", async (
            int draftId,
            int messageId,
            DraftService drafts) =>
        {
            await drafts.DeleteMessageAndSubsequentAsync(messageId);
            return Results.NoContent();
        });

        group.MapPost("/{id:int}/regenerate", async (
            int id,
            DraftService drafts,
            OpenRouterService openRouter,
            HttpContext http,
            CancellationToken ct) =>
        {
            var initial = await drafts.GetDraftAsync(id);
            if (initial is null) return Results.NotFound();

            await drafts.DeleteLastResponseAsync(id);
            var draft = await drafts.GetDraftAsync(id);
            if (draft is null || draft.Messages.Count == 0)
                return Results.BadRequest(new { error = "no messages to regenerate from" });

            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var content = new StringBuilder();
            var storyteller = draft.Storyteller;

            try
            {
                await foreach (var chunk in openRouter.ChatStreamAsync(
                    storyteller.ModelName, storyteller.Prompt, draft.Messages.ToList(),
                    storyteller.Temperature, storyteller.TopP, storyteller.MaxTokens,
                    storyteller.MinP, storyteller.TopK, storyteller.TopA, ct))
                {
                    var isReasoning = chunk.Kind == StreamChunkKind.Reasoning;
                    if (!isReasoning) content.Append(chunk.Text);
                    await WriteEnvelope(http, new StreamEnvelope("chunk", chunk.Text, isReasoning, null), ct);
                }

                int? savedId = null;
                if (content.Length > 0)
                {
                    var saved = await drafts.AddMessageAsync(id, MessageRole.Response, content.ToString());
                    savedId = saved.Id;
                }
                await WriteEnvelope(http, new StreamEnvelope("done", null, null, savedId), ct);
            }
            catch (OperationCanceledException)
            {
                if (content.Length > 0)
                {
                    var saved = await drafts.AddMessageAsync(id, MessageRole.Response, content.ToString());
                    await WriteEnvelope(http, new StreamEnvelope("done", null, null, saved.Id), CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                await WriteEnvelope(http, new StreamEnvelope("error", ex.Message, null, null), CancellationToken.None);
            }

            return Results.Empty;
        });

        return routes;
    }

    internal static DraftDto ToDto(Draft d) => new(
        d.Id, d.Title, d.CreatedAt, d.UpdatedAt,
        d.Storyteller.Name, d.Storyteller.ModelName,
        d.Messages.OrderBy(m => m.SortOrder)
            .Select(m => new DraftMessageDto(m.Id, m.Role, m.Content, m.SortOrder))
            .ToList());

    private static async Task WriteEnvelope(HttpContext http, StreamEnvelope env, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(env, EnvelopeJson);
        var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
        await http.Response.Body.WriteAsync(bytes, ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
