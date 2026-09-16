using Cerberus.Domain.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cerberus.Infrastructure.Security;

/// <summary>
/// Periodically creates the next keys ahead of rotation. The token server loads its key set at startup:
/// a newly activated key is used after the next restart, while the previous key stays valid and published
/// during its retention period, so a late restart never breaks token validation.
/// </summary>
public sealed class KeyRotationHostedService(IServiceScopeFactory scopeFactory, ILogger<KeyRotationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public IReadOnlyList<string> LoadedSigningKeyIds { get; set; } = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var manager = scope.ServiceProvider.GetRequiredService<SigningKeyManager>();
                await manager.EnsureKeysAsync(stoppingToken);
                var current = (await manager.LoadKeysAsync(KeyUsage.Signing, stoppingToken)).Select(k => k.KeyId).FirstOrDefault();
                if (current is not null && LoadedSigningKeyIds.FirstOrDefault() != current)
                {
                    logger.LogWarning("Signing key {KeyId} is now active but not loaded yet: restart the service to start using it.", current);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Key rotation check failed.");
            }
        }
    }
}
