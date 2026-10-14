namespace Poly.Mcp.Sessions;

/// <summary>
/// Session lifecycle options, bound from <c>Mcp:Sessions</c>.
/// </summary>
internal sealed class McpSessionOptions {
    /// <summary>Idle time after which a session is evicted. Zero or negative disables eviction.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(8);

    /// <summary>How often the eviction sweep runs. Zero or negative disables the sweep.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);
}