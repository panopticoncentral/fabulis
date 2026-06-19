using Fabulis.Server.Api;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Fabulis.Server.Auth.SessionTokenStore>();
builder.Services.AddSingleton<VaultService>();
builder.Services.AddHostedService<AutoLockService>();
builder.Services.AddDbContext<FabulisDbContext>((sp, options) =>
{
    var vault = sp.GetRequiredService<VaultService>();
    if (vault.IsUnlocked)
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "fabulis.db");
        options.UseSqlite($"Data Source={dbPath};Password={vault.Password}");
    }
});

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
builder.Services.AddSingleton<GenerationManager>();
builder.Services.AddSingleton<SummaryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SummaryService>());

var app = builder.Build();

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
api.MapStoryEndpoints();
api.MapSettingsEndpoints();
api.MapStorytellerEndpoints();
api.MapDraftEndpoints();
api.MapPromptEndpoints();
api.MapModelEndpoints();
api.MapNarrationEndpoints();

app.Run();
