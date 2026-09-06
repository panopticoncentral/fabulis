using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli.Tests;

/// <summary>
/// An in-memory vault plus scratch directories, torn down together. The seed
/// deliberately includes one of every entity kind, an empty story, a
/// story-free category, and content that exercises the escaping rules — so
/// the round-trip test in Task 6 has teeth.
/// </summary>
public sealed class VaultFixture : IDisposable
{
    public static readonly DateTime Epoch = new(2026, 4, 11, 9, 3, 12, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly List<string> _tempDirs = [];

    public FabulisDbContext Db { get; }

    public VaultFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<FabulisDbContext>()
            .UseSqlite(_connection)
            .Options;
        Db = new FabulisDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>A path that does not exist yet, deleted on dispose.</summary>
    public string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "fabulis-test-" + Guid.NewGuid().ToString("N"));
        _tempDirs.Add(path);
        return path;
    }

    public async Task SeedFullVaultAsync()
    {
        var storyteller = new Storyteller
        {
            Name = "Storyteller",
            Prompt = "You are a helpful storyteller.\n\nBe vivid.",
            // Deliberately distinct from Storyteller.DefaultTitlingPrompt: the
            // importer falls back to that same constant when the section is
            // missing, so seeding the default here would let a dropped field
            // silently pass as "round-tripped correctly".
            TitlingPrompt = "Name this story in exactly three words. No punctuation.",
            ModelName = "anthropic/claude-sonnet-4",
            // Deliberately distinct from the importer's `?? 0.7` fallback, for
            // the same reason as TitlingPrompt above.
            Temperature = 0.83,
            TopK = 40,
            ReasoningEffort = Fabulis.Server.Data.ReasoningEffort.Medium,
            CreatedAt = Epoch,
        };
        Db.Storytellers.Add(storyteller);

        var category = new Category { Name = "Fables", CreatedAt = Epoch };
        Db.Categories.Add(category);

        var story = new Story { Title = "The Fox", CreatedAt = Epoch, Category = category };
        var version = new StoryVersion
        {
            VersionNumber = 1,
            ModelName = "anthropic/claude-sonnet-4",
            CreatedAt = Epoch,
            Story = story,
        };
        // Content that an unescaped format would corrupt.
        version.Messages.Add(new StoryMessage
        {
            Role = MessageRole.Prompt,
            SortOrder = 0,
            Content = "Write about a fox.\n**Me:**\nstill the same message",
        });
        version.Messages.Add(new StoryMessage
        {
            Role = MessageRole.Response,
            SortOrder = 1,
            Content = "A fox went out.\n\n---\n\nIt returned.",
        });
        story.Versions.Add(version);
        category.Stories.Add(story);

        // A story with no versions, exercising the empty-directory case.
        category.Stories.Add(new Story { Title = "Empty Story", CreatedAt = Epoch, Category = category });

        // A category with no stories at all — dropped entirely by the old export.
        var listOnly = new Category { Name = "Openers", CreatedAt = Epoch };
        Db.Categories.Add(listOnly);

        var prompt = new Prompt
        {
            Title = "Cold open",
            CreatedAt = Epoch,
            UpdatedAt = Epoch,
            Category = category,
        };
        prompt.Messages.Add(new PromptMessage { Content = "Start in motion.", SortOrder = 0 });
        prompt.Messages.Add(new PromptMessage { Content = "Second block\n---\nwith a rule.", SortOrder = 1 });
        category.Prompts.Add(prompt);

        listOnly.OneLiners.Add(new OneLiner
        {
            Text = "She set fire to the document.", CreatedAt = Epoch, UpdatedAt = Epoch,
        });
        listOnly.OneLiners.Add(new OneLiner
        {
            Text = "Multi-line opener\n- with a bullet inside", CreatedAt = Epoch, UpdatedAt = Epoch,
        });
        listOnly.Tropes.Add(new Trope { Text = "Reluctant mentor", CreatedAt = Epoch, UpdatedAt = Epoch });

        var draft = new Draft
        {
            Storyteller = storyteller,
            Title = "A Night In The Fens",
            CreatedAt = Epoch,
            UpdatedAt = Epoch.AddHours(2),
        };
        draft.Messages.Add(new DraftMessage
        {
            Role = MessageRole.Prompt, Content = "Set the scene.", SortOrder = 0,
        });
        Db.Drafts.Add(draft);

        Db.AppSettings.Add(new AppSetting { Key = "AutoLockMinutes", Value = "15" });
        Db.AppSettings.Add(new AppSetting { Key = "KokoroBaseUrl", Value = "http://localhost:8880" });
        Db.AppSettings.Add(new AppSetting { Key = "SummaryPrompt", Value = "Summarize this.\n\nBe brief." });
        Db.AppSettings.Add(new AppSetting { Key = "OpenRouterApiKey", Value = "sk-secret-value" });

        await Db.SaveChangesAsync();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; a leaked temp dir must not fail a test run.
            }
        }
    }
}
