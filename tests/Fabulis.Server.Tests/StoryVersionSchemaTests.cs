using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class StoryVersionSchemaTests
{
    [Fact]
    public async Task LegacyStoryVersionsGainOriginAndNullableModelWithoutLosingMessages()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var setup = connection.CreateCommand();
        setup.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE Stories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CategoryId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
            );
            CREATE TABLE StoryVersions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StoryId INTEGER NOT NULL,
                VersionNumber INTEGER NOT NULL,
                ModelName TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (StoryId) REFERENCES Stories(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_StoryVersions_StoryId ON StoryVersions (StoryId);
            CREATE TABLE StoryMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StoryVersionId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                Content TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                FOREIGN KEY (StoryVersionId) REFERENCES StoryVersions(Id) ON DELETE CASCADE
            );
            INSERT INTO Categories (Name, CreatedAt) VALUES ('Archive', '2020-01-01T00:00:00Z');
            INSERT INTO Stories (CategoryId, Title, CreatedAt)
                VALUES (1, 'Existing story', '2020-01-01T00:00:00Z');
            INSERT INTO StoryVersions (StoryId, VersionNumber, ModelName, CreatedAt)
                VALUES (1, 1, 'old-model', '2020-01-01T00:00:00Z');
            INSERT INTO StoryMessages (StoryVersionId, Role, Content, SortOrder)
                VALUES (1, 'Response', 'Once upon a time', 0);
            """;
        await setup.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<FabulisDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new FabulisDbContext(options);

        await db.EnsureSchemaUpdatedAsync();

        var existing = await db.StoryVersions.Include(v => v.Messages).SingleAsync();
        Assert.Equal(StoryOrigin.Generated, existing.Origin);
        Assert.Equal("old-model", existing.ModelName);
        Assert.Equal("Once upon a time", existing.Messages.Single().Content);

        db.StoryVersions.Add(new StoryVersion
        {
            StoryId = 1,
            VersionNumber = 2,
            Origin = StoryOrigin.Imported,
            ModelName = null,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.Equal(StoryOrigin.Imported,
            (await db.StoryVersions.SingleAsync(v => v.VersionNumber == 2)).Origin);

        var integrity = await db.Database
            .SqlQueryRaw<string>("SELECT 'broken' AS Value FROM pragma_foreign_key_check")
            .ToListAsync();
        Assert.Empty(integrity);
    }
}
