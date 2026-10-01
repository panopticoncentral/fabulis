using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fabulis.Server.Api;
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Fabulis.Server.Tests;

public class SettingsStoreEndpointTests
{
    [Fact]
    public async Task SettingsApiWorksWithoutDbContextAndAppliesRuntimeEffectsOnlyAfterCommit()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var store = new TestStore();
        builder.Services.AddSingleton<IVaultStore>(store);
        builder.Services.AddSingleton<SessionTokenStore>();
        builder.Services.AddSingleton<VaultService>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton(sp => new KokoroService(
            sp.GetRequiredService<IHttpClientFactory>(), _ => Task.FromResult<string?>(null)));
        await using var app = builder.Build();
        app.MapGroup("/api/v1").MapSettingsEndpoints();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/settings")).StatusCode);
        var vault = app.Services.GetRequiredService<VaultService>();
        vault.Unlock("synthetic-password");
        vault.ConfigureAutoLock(15);
        var token = app.Services.GetRequiredService<SessionTokenStore>().Issue();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var settings = await client.GetFromJsonAsync<SettingsDto>("/api/v1/settings");
        Assert.False(settings!.ApiKeyIsSet);
        Assert.Equal(StorySummary.DefaultPrompt, settings.SummaryPrompt);

        using var invalid = await client.PutAsJsonAsync("/api/v1/settings", new
        {
            apiKey = "new-key", autoLockSelection = "1", narrationSpeed = 999,
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, store.Writes);
        Assert.Equal(TimeSpan.FromMinutes(15), vault.AutoLockTimeout);

        store.FailWrites = true;
        using var failed = await client.PutAsJsonAsync("/api/v1/settings", new { autoLockSelection = "1" });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(15), vault.AutoLockTimeout);

        store.FailWrites = false;
        using var saved = await client.PutAsJsonAsync("/api/v1/settings", new
        {
            apiKey = "  synthetic-key  ", autoLockSelection = "never", summaryPrompt = "  new instructions  ",
        });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Null(vault.AutoLockTimeout);
        Assert.Equal("synthetic-key", await store.GetSettingAsync("OpenRouterApiKey"));
        Assert.Equal("new instructions", await store.GetSettingAsync("SummaryPrompt"));
        var response = await client.GetStringAsync("/api/v1/settings");
        Assert.DoesNotContain("synthetic-key", response);
        settings = await client.GetFromJsonAsync<SettingsDto>("/api/v1/settings");
        Assert.True(settings!.ApiKeyIsSet);
        Assert.Equal("never", settings.AutoLockSelection);
        await app.StopAsync();
    }

    private sealed class TestStore : IVaultStore
    {
        private readonly Dictionary<string, string> settings = new(StringComparer.Ordinal);
        public bool FailWrites { get; set; }
        public int Writes { get; private set; }
        public Task<string?> GetSettingAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(settings.GetValueOrDefault(key));
        public Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(settings));
        public Task UpdateSettingsAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct = default)
        {
            if (FailWrites) throw new IOException("Synthetic save failure.");
            foreach (var (key, value) in changes) settings[key] = value;
            Writes++;
            return Task.CompletedTask;
        }
    }
}
