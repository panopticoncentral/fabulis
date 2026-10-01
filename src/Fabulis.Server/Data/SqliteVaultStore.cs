using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public sealed class SqliteVaultStore(FabulisDbContext db) : IVaultStore
{
    public Task<string?> GetSettingAsync(string key, CancellationToken ct = default) =>
        db.AppSettings.AsNoTracking().Where(s => s.Key == key)
            .Select(s => (string?)s.Value).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(CancellationToken ct = default) =>
        await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, ct);

    public async Task UpdateSettingsAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (changes.Count == 0) return;

        var keys = changes.Keys.ToArray();
        var existing = await db.AppSettings.Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, ct);
        foreach (var (key, value) in changes)
        {
            if (existing.TryGetValue(key, out var setting)) setting.Value = value;
            else db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        // EF wraps this batch in a transaction, including settings search-index
        // triggers, so callers never commit just part of an update.
        await db.SaveChangesAsync(ct);
    }
}
