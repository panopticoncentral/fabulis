using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fabulis.Server.Tests;

/// <summary>
/// The storyteller's reasoning effort has to survive the trip into the
/// OpenRouter request body, so these assert on the JSON actually sent.
/// </summary>
public class OpenRouterReasoningTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly StubHttpMessageHandler _stub = new();
    private readonly OpenRouterService _openRouter;

    public OpenRouterReasoningTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var collection = new ServiceCollection();
        collection.AddDbContext<FabulisDbContext>(o => o.UseSqlite(_connection));
        collection.AddScoped<IVaultStore, SqliteVaultStore>();
        _services = collection.BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
            db.Database.EnsureCreated();
            db.AppSettings.Add(new AppSetting { Key = "OpenRouterApiKey", Value = "test-key" });
            db.SaveChanges();
        }

        var factory = new FixedHttpClientFactory(new HttpClient(_stub));
        _openRouter = new OpenRouterService(factory, _services, new VaultService(new SessionTokenStore()),
            NullLogger<OpenRouterService>.Instance);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private async Task<JsonElement> CaptureRequestBodyAsync(ReasoningEffort? reasoning)
    {
        _stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[] { new { message = new { content = "A Title" } } }
            })
        });

        await _openRouter.ChatAsync("some/model", "system", "user", reasoning: reasoning);

        Assert.NotNull(_stub.LastRequestBody);
        return JsonDocument.Parse(_stub.LastRequestBody!).RootElement.Clone();
    }

    private async Task<JsonElement> CaptureStreamRequestBodyAsync(ReasoningEffort? reasoning)
    {
        _stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: [DONE]\n")
        });

        await foreach (var _ in _openRouter.ChatStreamAsync(
            "some/model", "system", [], reasoning: reasoning))
        {
        }

        Assert.NotNull(_stub.LastRequestBody);
        return JsonDocument.Parse(_stub.LastRequestBody!).RootElement.Clone();
    }

    [Fact]
    public async Task ChatStreamOmitsReasoningWhenEffortIsNull()
    {
        var body = await CaptureStreamRequestBodyAsync(null);

        Assert.False(body.TryGetProperty("reasoning", out _));
    }

    [Fact]
    public async Task ChatStreamSendsLowercaseEffortWhenEffortIsSet()
    {
        var body = await CaptureStreamRequestBodyAsync(ReasoningEffort.Medium);

        Assert.Equal("medium", body.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task StorytellerReasoningEffortRoundTripsAsText()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
        db.Storytellers.Add(new Storyteller
        {
            Name = "Teller",
            Prompt = "p",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = "some/model",
            ReasoningEffort = ReasoningEffort.Low,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var stored = await db.Database
            .SqlQueryRaw<string?>("SELECT ReasoningEffort AS Value FROM Storytellers")
            .SingleAsync();

        Assert.Equal("Low", stored);
    }

    [Fact]
    public async Task ChatOmitsReasoningWhenEffortIsNull()
    {
        var body = await CaptureRequestBodyAsync(null);

        Assert.False(body.TryGetProperty("reasoning", out _));
    }

    [Fact]
    public async Task ChatDisablesReasoningWhenEffortIsOff()
    {
        var body = await CaptureRequestBodyAsync(ReasoningEffort.Off);

        var reasoning = body.GetProperty("reasoning");
        Assert.False(reasoning.GetProperty("enabled").GetBoolean());
        Assert.False(reasoning.TryGetProperty("effort", out _));
    }

    [Fact]
    public async Task ChatSendsLowercaseEffortWhenEffortIsSet()
    {
        var body = await CaptureRequestBodyAsync(ReasoningEffort.High);

        var reasoning = body.GetProperty("reasoning");
        Assert.Equal("high", reasoning.GetProperty("effort").GetString());
        Assert.False(reasoning.TryGetProperty("enabled", out _));
    }
}
