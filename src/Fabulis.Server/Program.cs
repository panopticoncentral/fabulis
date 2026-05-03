using Fabulis.Server.Api;
using Fabulis.Server.Components;
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

builder.Services.AddScoped<CategoryImportService>();
builder.Services.AddScoped<CategoryExportService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<OpenRouterService>();
builder.Services.AddScoped<DraftService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    var isInfra = path is not null && (
        path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase));

    if (!isInfra)
    {
        var vault = context.RequestServices.GetRequiredService<VaultService>();
        vault.RecordActivity();
    }

    await next();
});

var api = app.MapGroup("/api/v1").DisableAntiforgery();
api.MapAuthEndpoints();
api.MapLibraryEndpoints();
api.MapStoryEndpoints();
api.MapSettingsEndpoints();
api.MapStorytellerEndpoints();
api.MapDraftEndpoints();

app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
