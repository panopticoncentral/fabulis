namespace Fabulis.Server.Data;

public class VaultService
{
    private long _lastActivityTicks;
    private int _autoLockMinutes;

    public bool IsUnlocked { get; private set; }
    public string? Password { get; private set; }

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
        Password = password;
        IsUnlocked = true;
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    public void Lock()
    {
        Password = null;
        IsUnlocked = false;
        Volatile.Write(ref _autoLockMinutes, 0);
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
