using Fabulis.Server.Api;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databasePath = VaultLocation.DatabasePath;
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

builder.Services.AddSingleton<Fabulis.Server.Auth.SessionTokenStore>();
builder.Services.AddSingleton<VaultService>();
builder.Services.AddHostedService<AutoLockService>();
builder.Services.AddDbContext<FabulisDbContext>((sp, options) =>
{
    var vault = sp.GetRequiredService<VaultService>();
    if (vault.IsUnlocked)
    {
        options.UseSqlite(
            $"Data Source={databasePath};Password={vault.Password}",
            sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }
});

builder.Services.AddScoped<IVaultStore, SqliteVaultStore>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("kokoro", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<OpenRouterService>();
builder.Services.AddSingleton<KokoroService>();
builder.Services.AddSingleton<NarrationTokenStore>();
builder.Services.AddScoped<DraftService>();
builder.Services.AddScoped<PromptService>();
builder.Services.AddScoped<OneLinerService>();
builder.Services.AddScoped<TropeService>();
builder.Services.AddSingleton<GenerationManager>();
builder.Services.AddSingleton<SummaryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SummaryService>());

var app = builder.Build();

var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Fabulis.Startup");
if (VaultLocation.MigrateLegacyDatabase(VaultLocation.LegacyDatabasePath, databasePath))
    startupLog.LogInformation(
        "Moved the vault out of the build output to {DatabasePath}.", databasePath);
else
    startupLog.LogInformation("Vault database: {DatabasePath}", databasePath);

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is null || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }
    var vault = context.RequestServices.GetRequiredService<VaultService>();
    vault.RecordActivity();
    await next();
});

var api = app.MapGroup("/api/v1").DisableAntiforgery();
api.MapAuthEndpoints();
api.MapLibraryEndpoints();
api.MapSearchEndpoints();
api.MapStoryEndpoints();
api.MapSettingsEndpoints();
api.MapStorytellerEndpoints();
api.MapDraftEndpoints();
api.MapPromptEndpoints();
api.MapOneLinerEndpoints();
api.MapTropeEndpoints();
api.MapModelEndpoints();
api.MapNarrationEndpoints();

app.Run();
