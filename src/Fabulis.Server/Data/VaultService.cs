using Fabulis.Server.Auth;

namespace Fabulis.Server.Data;

public class VaultService(SessionTokenStore tokens)
{
    private long _lastActivityTicks;
    private int _autoLockMinutes;
    private int _isUnlocked;
    private string? _password;

    public event Action? Locked;

    public bool IsUnlocked => Volatile.Read(ref _isUnlocked) != 0;

    public string? Password => Volatile.Read(ref _password);

    public DateTime LastActivityAt =>
        new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);

    public TimeSpan? AutoLockTimeout
    {
        get
        {
            var minutes = Volatile.Read(ref _autoLockMinutes);
            return minutes > 0 ? TimeSpan.FromMinutes(minutes) : null;
        }
    }

    public void Unlock(string password)
    {
        Volatile.Write(ref _password, password);
        Volatile.Write(ref _isUnlocked, 1);
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    public void Lock()
    {
        Volatile.Write(ref _password, null);
        Volatile.Write(ref _isUnlocked, 0);
        Volatile.Write(ref _autoLockMinutes, 0);
        tokens.RevokeAll();
        Locked?.Invoke();
    }

    public void RecordActivity()
    {
        if (IsUnlocked)
            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    public void ConfigureAutoLock(int? minutes)
    {
        var value = minutes is int m && m > 0 ? m : 0;
        Volatile.Write(ref _autoLockMinutes, value);
    }
}
