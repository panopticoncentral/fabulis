using Microsoft.Extensions.Hosting;

namespace Fabulis.Server.Data;

public class AutoLockService(VaultService vault, ILogger<AutoLockService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (vault.IsUnlocked && vault.AutoLockTimeout is { } timeout)
                {
                    var idle = DateTime.UtcNow - vault.LastActivityAt;
                    if (idle > timeout)
                    {
                        logger.LogInformation("Auto-locking vault after {Idle} of inactivity (timeout {Timeout}).", idle, timeout);
                        vault.Lock();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-lock poll failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
