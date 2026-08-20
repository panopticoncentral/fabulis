using Microsoft.Data.Sqlite;
using Xunit;

namespace Fabulis.Server.Tests;

/// <summary>
/// Guards the SQLCipher provider wiring: the server references the
/// Microsoft.EntityFrameworkCore.Sqlite.Core / Microsoft.Data.Sqlite.Core packages
/// so that only the SQLCipher native library ships. If the plain e_sqlite3 bundle
/// ever wins provider registration again, the encrypted round-trip below fails.
/// </summary>
public class SqlCipherProviderTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fabulis-cipher-{Guid.NewGuid():N}.db");

    [Fact]
    public void EncryptedDatabase_RoundTripsWithCorrectPassword()
    {
        using (var write = new SqliteConnection($"Data Source={_dbPath};Password=correct-horse"))
        {
            write.Open();
            using var cmd = write.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (v TEXT); INSERT INTO t VALUES ('secret');";
            cmd.ExecuteNonQuery();
        }

        using var read = new SqliteConnection($"Data Source={_dbPath};Password=correct-horse");
        read.Open();
        using var select = read.CreateCommand();
        select.CommandText = "SELECT v FROM t";
        Assert.Equal("secret", select.ExecuteScalar());
    }

    [Fact]
    public void EncryptedDatabase_RejectsWrongPassword()
    {
        using (var write = new SqliteConnection($"Data Source={_dbPath};Password=correct-horse"))
        {
            write.Open();
            using var cmd = write.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (v TEXT)";
            cmd.ExecuteNonQuery();
        }

        using var read = new SqliteConnection($"Data Source={_dbPath};Password=wrong-horse");
        Assert.ThrowsAny<SqliteException>(() => read.Open());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
