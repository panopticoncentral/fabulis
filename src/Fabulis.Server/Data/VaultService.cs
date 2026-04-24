namespace Fabulis.Server.Data;

public class VaultService
{
    public bool IsUnlocked { get; private set; }
    public string? Password { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public TimeSpan? AutoLockTimeout { get; private set; }

    public void Unlock(string password)
    {
        Password = password;
        IsUnlocked = true;
        LastActivityAt = DateTime.UtcNow;
    }

    public void Lock()
    {
        Password = null;
        IsUnlocked = false;
        AutoLockTimeout = null;
    }

    public void RecordActivity()
    {
        if (IsUnlocked)
            LastActivityAt = DateTime.UtcNow;
    }

    public void ConfigureAutoLock(int? minutes)
    {
        AutoLockTimeout = minutes is int m && m > 0
            ? TimeSpan.FromMinutes(m)
            : null;
    }
}
