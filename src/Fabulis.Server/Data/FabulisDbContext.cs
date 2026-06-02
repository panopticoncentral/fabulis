using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fabulis.Server.Data;

public class FabulisDbContext : DbContext
{
    public FabulisDbContext(DbContextOptions<FabulisDbContext> options) : base(options)
    {
    }

    // Every DateTime in this database is UTC by convention (writes always use
    // DateTime.UtcNow). SQLite stores DateTime as TEXT; without explicit kind
    // labeling, EF reads them back as DateTimeKind.Unspecified and
    // System.Text.Json then serializes them WITHOUT a trailing 'Z' or offset,
    // which Foundation's ISO8601DateFormatter rejects on the client. Rebrand
    // both directions as UTC so the wire format is always Z-terminated.
    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        { }
    }

    private sealed class UtcNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public UtcNullableDateTimeConverter() : base(
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null)
        { }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
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
        // Escaped for embedding in the raw-SQL DEFAULT clauses below. Keep
        // this escaping if DefaultTitlingPrompt is ever edited to contain an
        // apostrophe, or schema bootstrap will produce broken SQL.
        var titlingDefaultSql = Storyteller.DefaultTitlingPrompt.Replace("'", "''");

        // EF1002: the interpolated value is a compile-time constant that we
        // single-quote-escape above, and a column DEFAULT in DDL cannot be
        // parameterized — so the injection warning does not apply here.
#pragma warning disable EF1002
        await Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS Storytellers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Prompt TEXT NOT NULL,
                TitlingPrompt TEXT NOT NULL DEFAULT '{titlingDefaultSql}',
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

        // Storytellers gained TitlingPrompt after the initial release.
        // CREATE TABLE IF NOT EXISTS above never alters an existing table,
        // so add the column on vaults created before this field existed.
        var storytellerColumns = await Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Storytellers')")
            .ToListAsync();
        if (!storytellerColumns.Contains("TitlingPrompt"))
        {
            await Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Storytellers ADD COLUMN TitlingPrompt TEXT NOT NULL DEFAULT '{titlingDefaultSql}'");
        }
#pragma warning restore EF1002

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
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = string.IsNullOrWhiteSpace(assistantModel) ? "anthropic/claude-sonnet-4" : assistantModel,
            Temperature = 0.7,
            CreatedAt = DateTime.UtcNow,
        });
        await SaveChangesAsync();
    }
}
