using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Fabulis.Server.Tests;

/// <summary>
/// The server log is the only place that records which model produced a
/// generation, so these assert on the lines it writes: the model asked for,
/// and the one OpenRouter reports having served.
/// </summary>
public class OpenRouterLoggingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly StubHttpMessageHandler _stub = new();
    private readonly RecordingLogger _log = new();
    private readonly OpenRouterService _openRouter;

    public OpenRouterLoggingTests()
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
        _openRouter = new OpenRouterService(factory, _services, new VaultService(new SessionTokenStore()), _log);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private void RespondWith(object body) =>
        _stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(body)
        });

    private void RespondWithStream(string sse) =>
        _stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse)
        });

    private static object Completion(string? servedModel) => servedModel is null
        ? new { choices = new[] { new { message = new { content = "Text" } } } }
        : new { model = servedModel, choices = new[] { new { message = new { content = "Text" } } } };

    private async Task DrainStreamAsync(string requestedModel)
    {
        await foreach (var _ in _openRouter.ChatStreamAsync(requestedModel, "system", []))
        {
        }
    }

    [Fact]
    public async Task ChatLogsTheRequestedModelAndSettings()
    {
        RespondWith(Completion(null));

        await _openRouter.ChatAsync("vendor/asked", "system", "user",
            temperature: 0.4, topP: 0.9, reasoning: ReasoningEffort.High);

        var entry = Assert.Single(_log.Entries, e => e.Message.StartsWith("OpenRouter completion:"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("model=vendor/asked", entry.Message);
        Assert.Contains("temperature=0.4", entry.Message);
        Assert.Contains("top_p=0.9", entry.Message);
        Assert.Contains("reasoning=high", entry.Message);
        Assert.DoesNotContain("top_k", entry.Message);
    }

    [Fact]
    public async Task ChatLogsSettingsInvariantlyUnderACommaDecimalCulture()
    {
        RespondWith(Completion(null));

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            await _openRouter.ChatAsync("vendor/asked", "system", "user", temperature: 0.4);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var entry = Assert.Single(_log.Entries, e => e.Message.StartsWith("OpenRouter completion:"));
        Assert.Contains("temperature=0.4", entry.Message);
    }

    [Fact]
    public async Task ChatLogsRoutingWhenTheServedModelDiffers()
    {
        RespondWith(Completion("vendor/served"));

        await _openRouter.ChatAsync("vendor/asked", "system", "user");

        var entry = Assert.Single(_log.Entries, e => e.Message.StartsWith("OpenRouter routed"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("vendor/asked", entry.Message);
        Assert.Contains("model=vendor/served", entry.Message);
    }

    [Fact]
    public async Task ChatLogsAMatchingServedModelAtDebug()
    {
        RespondWith(Completion("vendor/asked"));

        await _openRouter.ChatAsync("vendor/asked", "system", "user");

        Assert.DoesNotContain(_log.Entries, e => e.Message.StartsWith("OpenRouter routed"));
        var entry = Assert.Single(_log.Entries, e => e.Message.StartsWith("OpenRouter served"));
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains("model=vendor/asked", entry.Message);
    }

    [Fact]
    public async Task ChatSurvivesAResponseWithNoModelField()
    {
        RespondWith(Completion(null));

        await _openRouter.ChatAsync("vendor/asked", "system", "user");

        Assert.DoesNotContain(_log.Entries, e => e.Message.StartsWith("OpenRouter routed"));
        Assert.DoesNotContain(_log.Entries, e => e.Message.StartsWith("OpenRouter served"));
    }

    [Fact]
    public async Task ChatStreamLogsTheRequestedModelAndMessageCount()
    {
        RespondWithStream("data: [DONE]\n");

        await DrainStreamAsync("vendor/asked");

        var entry = Assert.Single(_log.Entries, e => e.Message.StartsWith("OpenRouter stream:"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("model=vendor/asked", entry.Message);
        Assert.Contains("messages=0", entry.Message);
    }

    [Fact]
    public async Task ChatStreamLogsRoutingFromTheFirstChunkThatNamesAModel()
    {
        RespondWithStream(
            "data: {\"choices\":[{\"delta\":{}}]}\n" +
            "data: {\"model\":\"vendor/served\",\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n" +
            "data: {\"model\":\"vendor/served\",\"choices\":[{\"delta\":{\"content\":\" there\"}}]}\n" +
            "data: [DONE]\n");

        await DrainStreamAsync("vendor/asked");

        var entry = Assert.Single(_log.Entries, e => e.Message.StartsWith("OpenRouter routed"));
        Assert.Contains("model=vendor/served", entry.Message);
    }
}

/// <summary>
/// Captures formatted log lines so tests can assert on what the server writes.
/// </summary>
public sealed class RecordingLogger : ILogger<OpenRouterService>
{
    public record Entry(LogLevel Level, string Message);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception)));
}
