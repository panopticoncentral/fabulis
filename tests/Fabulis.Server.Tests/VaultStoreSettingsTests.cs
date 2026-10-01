using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class VaultStoreSettingsTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly FabulisDbContext db;
    private readonly IVaultStore store;

    public VaultStoreSettingsTests()
    {
        connection.Open();
        db = new FabulisDbContext(new DbContextOptionsBuilder<FabulisDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        store = new SqliteVaultStore(db);
    }

    public void Dispose() { db.Dispose(); connection.Dispose(); }

    [Fact]
    public async Task SettingsPreserveExactStringsUnknownKeysAndOmittedValues()
    {
        await store.UpdateSettingsAsync(new Dictionary<string, string>
        {
            ["futureSetting"] = "\r\n  café e\u0301 🦊\n\t",
            ["Empty"] = "",
            ["Case"] = "uppercase",
            ["case"] = "lowercase",
            ["OpenRouterApiKey"] = "synthetic-key",
        });
        await store.UpdateSettingsAsync(new Dictionary<string, string> { ["Case"] = "changed" });

        var settings = await store.GetSettingsAsync();
        Assert.Equal(5, settings.Count);
        Assert.Equal("\r\n  café e\u0301 🦊\n\t", settings["futureSetting"]);
        Assert.Equal("", await store.GetSettingAsync("Empty"));
        Assert.Null(await store.GetSettingAsync("missing"));
        Assert.Equal("changed", settings["Case"]);
        Assert.Equal("lowercase", settings["case"]);
        Assert.Equal("synthetic-key", settings["OpenRouterApiKey"]);
        await store.UpdateSettingsAsync(new Dictionary<string, string>());
        Assert.Equal(5, (await store.GetSettingsAsync()).Count);
    }

    [Fact]
    public async Task ReadSnapshotDoesNotChangeWhenSettingsAreUpdated()
    {
        await store.UpdateSettingsAsync(new Dictionary<string, string> { ["key"] = "before" });
        var snapshot = await store.GetSettingsAsync();
        await store.UpdateSettingsAsync(new Dictionary<string, string> { ["key"] = "after" });
        Assert.Equal("before", snapshot["key"]);
        Assert.Equal("after", await store.GetSettingAsync("key"));
    }

    [Fact]
    public async Task FailedBatchRollsBackAllSettingsAndSearchIndexChanges()
    {
        await db.EnsureSchemaUpdatedAsync();
        await store.UpdateSettingsAsync(new Dictionary<string, string> { ["SummaryPrompt"] = "oldnebula" });
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER RejectSyntheticSetting BEFORE INSERT ON AppSettings
            WHEN new.Key = 'Rejected'
            BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.UpdateSettingsAsync(new Dictionary<string, string>
        {
            ["SummaryPrompt"] = "newquasar",
            ["Rejected"] = "value",
        }));
        db.ChangeTracker.Clear();
        Assert.Equal("oldnebula", await store.GetSettingAsync("SummaryPrompt"));
        Assert.Null(await store.GetSettingAsync("Rejected"));
        Assert.Empty((await SearchService.SearchAsync(db, "newquasar")).Results);
        Assert.Single((await SearchService.SearchAsync(db, "oldnebula")).Results);
    }

    [Fact]
    public async Task CancelledWriteDoesNotApplyAnySettings()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.UpdateSettingsAsync(new Dictionary<string, string> { ["key"] = "value" }, cancellation.Token));
        Assert.Empty(await store.GetSettingsAsync());
    }
}
