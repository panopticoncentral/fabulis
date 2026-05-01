using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class FabulisDbContext : DbContext
{
    public FabulisDbContext(DbContextOptions<FabulisDbContext> options) : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Story> Stories => Set<Story>();
    public DbSet<StoryVersion> StoryVersions => Set<StoryVersion>();
    public DbSet<StoryMessage> StoryMessages => Set<StoryMessage>();
    public DbSet<Storyteller> Storytellers => Set<Storyteller>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<DraftMessage> DraftMessages => Set<DraftMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoryMessage>()
            .Property(m => m.Role)
            .HasConversion<string>();

        modelBuilder.Entity<AppSetting>()
            .HasKey(s => s.Key);

        modelBuilder.Entity<DraftMessage>()
            .Property(m => m.Role)
            .HasConversion<string>();
    }

    public async Task EnsureSchemaUpdatedAsync()
    {
        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Storytellers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Prompt TEXT NOT NULL,
                ModelName TEXT NOT NULL,
                Temperature REAL NOT NULL DEFAULT 0.7,
                TopP REAL NULL,
                MaxTokens INTEGER NULL,
                MinP REAL NULL,
                TopK INTEGER NULL,
                TopA REAL NULL,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
            )
            """);

        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL
            )
            """);

        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Drafts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StorytellerID INTEGER NOT NULL,
                Title TEXT NULL,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                FOREIGN KEY (StorytellerID) REFERENCES Storytellers(Id)
            )
            """);

        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS DraftMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DraftId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                Content TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (DraftId) REFERENCES Drafts(Id) ON DELETE CASCADE
            )
            """);

        await SeedDefaultStorytellerIfMissingAsync();
    }

    private async Task SeedDefaultStorytellerIfMissingAsync()
    {
        if (await Storytellers.AnyAsync()) return;

        var assistantModel = await AppSettings
            .Where(s => s.Key == "AssistantModel")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();

        Storytellers.Add(new Storyteller
        {
            Name = "Storyteller",
            Prompt = "You are a helpful storyteller.",
            ModelName = string.IsNullOrWhiteSpace(assistantModel) ? "anthropic/claude-sonnet-4" : assistantModel,
            Temperature = 0.7,
            CreatedAt = DateTime.UtcNow,
        });
        await SaveChangesAsync();
    }
}
