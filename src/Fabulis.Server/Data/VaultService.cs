namespace Fabulis.Server.Data;

public class VaultService
{
    public bool IsUnlocked { get; private set; }
    public string? Password { get; private set; }

    public void Unlock(string password)
    {
        Password = password;
        IsUnlocked = true;
    }

    public void Lock()
    {
        Password = null;
        IsUnlocked = false;
    }
}
