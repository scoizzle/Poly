using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Poly.Mcp.Sessions;

/// <summary>
/// Sweeps idle sessions so a long-lived stdio server does not grow without bound.
/// </summary>
internal sealed class SessionEvictionService(
    ISessionStore store,
    IOptions<McpSessionOptions> options,
    ILogger<SessionEvictionService> logger) : BackgroundService {

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var interval = options.Value.SweepInterval;
        if (interval <= TimeSpan.Zero)
            return;
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) {
            try {
                var evicted = store.EvictIdle();
                if (evicted > 0)
                    logger.LogInformation("evicted {Count} idle MCP session(s)", evicted);
            }
            catch (Exception ex) {
                logger.LogError(ex, "MCP session eviction sweep failed");
            }
        }
    }
}