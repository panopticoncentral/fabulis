using System.Diagnostics;
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class NarrationEndpoints
{
    public static IEndpointRouteBuilder MapNarrationEndpoints(this IEndpointRouteBuilder routes)
    {
        // Session-authed group: voices listing and prepare.
        var authed = routes.MapGroup("/narration").RequireSession();

        authed.MapGet("voices", async (
            KokoroService kokoro,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Narration");
            try
            {
                var voices = await kokoro.ListVoicesAsync(ct);
                var dto = new VoicesResponse(
                    voices.Select(v => new NarrationVoiceDto(v.Id, v.DisplayName, v.Language))
                          .ToList());
                return Results.Ok(dto);
            }
            catch (KokoroUnavailableException ex)
            {
                logger.LogWarning(ex, "Kokoro unavailable while listing voices");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });

        // POST /prepare validates the synthesis params and stashes them
        // under a one-shot token. AVPlayer on iOS can only fetch via GET
        // without bodies, so the actual audio request is the separate
        // /play/{token} GET below.
        authed.MapPost("prepare", async (
            SynthesizeRequest body,
            FabulisDbContext db,
            NarrationTokenStore tokens,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Narration");
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });
            if (!NarrationValidation.IsTextLengthOk(body.Text))
                return Results.BadRequest(new { error = $"text exceeds {NarrationValidation.MaxTextLength} characters" });

            var stripped = MarkdownStripper.ToPlainText(body.Text);
            if (string.IsNullOrWhiteSpace(stripped))
                return Results.BadRequest(new { error = "text has no readable content after markdown stripping" });

            var voiceSetting = await db.AppSettings.FindAsync(["NarrationVoice"]);
            var speedSetting = await db.AppSettings.FindAsync(["NarrationSpeed"]);

            var voice = NarrationValidation.NormalizeVoice(body.Voice, voiceSetting?.Value);
            if (voice is null)
                return Results.BadRequest(new { error = "no voice configured" });

            var speed = NarrationValidation.NormalizeSpeed(body.Speed, speedSetting?.Value);
            if (!NarrationValidation.IsSpeedValid(speed))
                return Results.BadRequest(new { error = $"speed must be between {NarrationValidation.MinSpeed} and {NarrationValidation.MaxSpeed}" });

            var token = tokens.Create(new NarrationTokenStore.Params(stripped, voice, speed));
            logger.LogInformation("Narration prepare: issued token (length={Len}, voice={Voice}, speed={Speed}, textLen={TextLen})",
                token.Length, voice, speed, stripped.Length);
            return Results.Ok(new PrepareResponse(token));
        });

        // GET /play/{token} is NOT session-authed — the token itself is
        // the credential. Tokens are 22 chars of URL-safe base64 (128 bits
        // of entropy), one-shot, and expire after 5 minutes. This is what
        // AVPlayer fetches natively; auth via Authorization header on the
        // asset's HTTP request would force us to use AVURLAssetHTTPHeaderFieldsKey
        // (which is undocumented and brittle).
        routes.MapMethods("/narration/play/{token}", new[] { "GET", "HEAD" }, async (
            string token,
            HttpContext httpContext,
            NarrationTokenStore tokens,
            KokoroService kokoro,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Narration");
            var method = httpContext.Request.Method;
            logger.LogInformation("Narration play: {Method} token length={Len}", method, token.Length);

            var parameters = tokens.Lookup(token);
            if (parameters is null)
            {
                logger.LogWarning("Narration play: token not found / expired / already consumed");
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // HEAD: just tell the client the type and that we'll serve audio.
            // AVPlayer sometimes probes with HEAD before starting the GET.
            if (string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "audio/mpeg";
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            HttpResponseMessage? upstream = null;
            try
            {
                upstream = await kokoro.SynthesizeStreamAsync(
                    parameters.Text, parameters.Voice, parameters.Speed, ct);

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "audio/mpeg";
                // No Content-Length; framework will use chunked transfer encoding.

                await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(ct);
                long totalBytes = 0;
                var buffer = new byte[8192];
                int read;
                while ((read = await upstreamStream.ReadAsync(buffer, ct)) > 0)
                {
                    await httpContext.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                    totalBytes += read;
                }

                logger.LogInformation(
                    "Streamed {Bytes} bytes in {ElapsedMs}ms (voice={Voice}, speed={Speed}, textLength={TextLength})",
                    totalBytes, stopwatch.ElapsedMilliseconds, parameters.Voice, parameters.Speed, parameters.Text.Length);
            }
            catch (KokoroUnavailableException ex)
            {
                logger.LogWarning(ex,
                    "Kokoro unavailable during play after {ElapsedMs}ms (textLength={TextLength})",
                    stopwatch.ElapsedMilliseconds, parameters.Text.Length);
                if (!httpContext.Response.HasStarted)
                    httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
            catch (KokoroUpstreamException ex)
            {
                logger.LogWarning(ex,
                    "Kokoro returned upstream error after {ElapsedMs}ms (status={Status})",
                    stopwatch.ElapsedMilliseconds, ex.UpstreamStatus);
                if (!httpContext.Response.HasStarted)
                    httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client cancelled (paused / closed view) — normal.
                logger.LogDebug(
                    "Play stream cancelled by client after {ElapsedMs}ms",
                    stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                upstream?.Dispose();
            }
        });

        return routes;
    }
}
