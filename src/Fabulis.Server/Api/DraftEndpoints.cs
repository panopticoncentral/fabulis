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

        group.MapDelete("/{id:int}", async (int id, DraftService drafts, GenerationManager gens) =>
        {
            gens.Get(id)?.Cts.Cancel();
            gens.Remove(id);
            await drafts.DeleteDraftAsync(id);
            return Results.NoContent();
        });

        group.MapPost("/{id:int}/messages", async (
            int id,
            StreamPromptRequest body,
            DraftService drafts,
            GenerationManager gens,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Prompt))
                return Results.BadRequest(new { error = "prompt is required" });

            var draft = await drafts.GetDraftAsync(id);
            if (draft is null) return Results.NotFound();

            if (gens.IsRunning(id))
                return Results.Conflict(new { error = "a generation is already in progress" });

            await drafts.AddMessageAsync(id, MessageRole.Prompt, body.Prompt.Trim());
            var gen = gens.Start(id);

            return await StreamGeneration(http, gen, ct);
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

        group.MapPut("/{draftId:int}/messages/{messageId:int}", async (
            int draftId,
            int messageId,
            UpdateMessageRequest body,
            DraftService drafts) =>
        {
            if (body.Content is null)
                return Results.BadRequest(new { error = "content is required" });
            await drafts.UpdateMessageContentAsync(messageId, body.Content);
            return Results.NoContent();
        });

        group.MapPost("/{draftId:int}/messages/{messageId:int}/edit-and-resubmit", async (
            int draftId,
            int messageId,
            UpdateMessageRequest body,
            DraftService drafts,
            GenerationManager gens,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (body.Content is null)
                return Results.BadRequest(new { error = "content is required" });

            var initial = await drafts.GetDraftAsync(draftId);
            if (initial is null) return Results.NotFound();

            if (gens.IsRunning(draftId))
                return Results.Conflict(new { error = "a generation is already in progress" });

            await drafts.UpdateMessageAndDeleteSubsequentAsync(messageId, body.Content);
            var draft = await drafts.GetDraftAsync(draftId);
            if (draft is null || draft.Messages.Count == 0)
                return Results.BadRequest(new { error = "no messages to stream from" });

            var gen = gens.Start(draftId);
            return await StreamGeneration(http, gen, ct);
        });

        group.MapPost("/{id:int}/regenerate", async (
            int id,
            DraftService drafts,
            GenerationManager gens,
            HttpContext http,
            CancellationToken ct) =>
        {
            var initial = await drafts.GetDraftAsync(id);
            if (initial is null) return Results.NotFound();

            if (gens.IsRunning(id))
                return Results.Conflict(new { error = "a generation is already in progress" });

            await drafts.DeleteLastResponseAsync(id);
            var draft = await drafts.GetDraftAsync(id);
            if (draft is null || draft.Messages.Count == 0)
                return Results.BadRequest(new { error = "no messages to regenerate from" });

            var gen = gens.Start(id);
            return await StreamGeneration(http, gen, ct);
        });

        // Re-attach to an in-flight (or recently-completed) generation for
        // this draft. Used by the client to resume after a network drop —
        // e.g. the iPhone backgrounded the app mid-stream and URLSession
        // dropped the connection, but the generation kept running on the
        // server. The first envelope is `snapshot` (full content so far);
        // the stream then continues with deltas, or terminates immediately
        // if the generation has already finished. 404 means there's nothing
        // in flight — caller should refresh the draft.
        group.MapGet("/{id:int}/stream", async (
            int id,
            GenerationManager gens,
            HttpContext http,
            CancellationToken ct) =>
        {
            var gen = gens.Get(id);
            if (gen is null) return Results.NotFound();
            return await StreamGeneration(http, gen, ct);
        });

        // Explicit cancel: stop generating and save whatever was produced
        // so far. Returns 204 even if there's nothing in flight (idempotent).
        group.MapDelete("/{id:int}/stream", (int id, GenerationManager gens) =>
        {
            gens.Get(id)?.Cts.Cancel();
            return Results.NoContent();
        });

        return routes;
    }

    internal static DraftDto ToDto(Draft d) => new(
        d.Id, d.Title, d.CreatedAt, d.UpdatedAt,
        d.Storyteller.Name, d.Storyteller.ModelName,
        d.Messages.OrderBy(m => m.SortOrder)
            .Select(m => new DraftMessageDto(m.Id, m.Role, m.Content, m.SortOrder))
            .ToList());

    private static async Task<IResult> StreamGeneration(HttpContext http, Generation gen, CancellationToken ct)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var env in gen.SubscribeAsync(ct))
            {
                await WriteEnvelope(http, env, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected (e.g. backgrounded). The generation keeps
            // running; another request can re-attach via GET /stream.
        }

        return Results.Empty;
    }

    private static async Task WriteEnvelope(HttpContext http, StreamEnvelope env, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(env, EnvelopeJson);
        var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
        await http.Response.Body.WriteAsync(bytes, ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
