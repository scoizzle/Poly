using Poly.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Mcp.Sessions;

/// <summary>
/// Session state store for the MCP harness. Sessions are keyed by the
/// <c>sessionId</c> the tools pass around; identity is the id, never a
/// transport header (this process is stdio).
/// </summary>
internal interface ISessionStore {
    /// <summary>Creates a session with a bootstrapped domain. Overwrites an existing id.</summary>
    (string SessionId, McpSessionState State) Create(string domainName, string? preferredSessionId = null);

    /// <summary>Reads a session and marks it used. False when the id is unknown or blank.</summary>
    bool TryGet(string sessionId, out McpSessionState session);

    /// <summary>
    /// Atomically reads, mutates, and writes a session under that session's lock.
    /// A rejected outcome leaves the session as it was. Null when the id is unknown.
    /// </summary>
    EvolutionResult? Evolve(string sessionId, Func<Domain, DomainSession, EvolutionResult> mutate);

    /// <summary>All active session ids.</summary>
    IReadOnlyList<string> ListSessions();

    /// <summary>Removes a session. True when it existed.</summary>
    bool Remove(string sessionId);

    /// <summary>
    /// Atomically replaces a session's domain and analysis (revision + 1) and clears
    /// runtime instances. <paramref name="modeling"/> is the ForSource session, so a
    /// <c>uses</c> change does not re-open against Core.
    /// </summary>
    bool Replace(string sessionId, Domain domain, AnalysisResult? analysis, DomainSession modeling);

    /// <summary>Locked access to a session's state for runtime instance operations.</summary>
    bool TryModifyInstances(string sessionId, Action<McpSessionState> action);

    /// <summary>Removes sessions idle longer than the configured timeout. Returns how many were removed.</summary>
    int EvictIdle();
}