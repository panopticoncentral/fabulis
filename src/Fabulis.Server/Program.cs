using Fabulis.Server.Components;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<VaultService>();
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
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
