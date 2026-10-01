namespace Fabulis.Server.Data;

/// <summary>
/// Server persistence boundary, introduced incrementally while SQLite remains
/// the active backend. Settings are the first migrated operations. Callers see
/// values and complete mutations, never EF queries or tracked entities.
/// </summary>
public interface IVaultStore
{
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically upserts the supplied keys. Absent keys are unchanged, empty
    /// strings are values, and all strings (including unknown settings) survive
    /// verbatim. Validation/normalization belongs to the application caller.
    /// </summary>
    Task UpdateSettingsAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct = default);
}
