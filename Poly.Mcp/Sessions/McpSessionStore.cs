using System.Collections.Concurrent;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;
using Poly.Syntax.Analysis;

namespace Poly.Mcp.Sessions;

/// <summary>
/// V3 session state: holds the current V3 <see cref="Domain"/> root,
/// the latest analysis result, and a monotonically increasing revision number.
/// No V2 types, no revision snapshot history.
/// Workspace/session management lives here in MCP — not in DomainModeling.
/// </summary>
internal sealed record McpSessionState(
    Domain Domain,
    AnalysisResult? LatestAnalysis,
    long Revision
);

/// <summary>
/// Thread-safe in-memory store for V3 domain sessions.
/// Sessions are identified by a string sessionId (typically a GUID).
/// Bootstrap uses <see cref="DomainFactory.Create"/> from the DomainModeling API.
/// </summary>
internal static class McpSessionStore {
    private static readonly ConcurrentDictionary<string, McpSessionState> Sessions = new(StringComparer.Ordinal);
    private static readonly Lock StoreLock = new();

    /// <summary>
    /// Creates a new session with a bootstrapped domain.
    /// </summary>
    public static (string SessionId, McpSessionState State) Create(string domainName, string? preferredSessionId = null) {
        lock (StoreLock) {
            var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
                ? Guid.NewGuid().ToString("N")
                : preferredSessionId;

            var domain = DomainFactory.Create(domainName);
            var analysis = DomainModelAnalyzer.Analyze(domain);
            var state = new McpSessionState(domain, analysis, Revision: 0);
            Sessions[sessionId] = state;
            return (sessionId, state);
        }
    }

    /// <summary>
    /// Gets an existing session by ID. Returns false if not found.
    /// </summary>
    public static bool TryGet(string sessionId, out McpSessionState session) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            session = null!;
            return false;
        }
        return Sessions.TryGetValue(sessionId, out session!);
    }

    /// <summary>
    /// Atomically reads, mutates, and writes a session. The entire read-modify-write
    /// cycle holds <see cref="StoreLock"/>, preventing the lost-update race that occurs
    /// when callers do an unprotected <see cref="TryGet"/> → evolve → <see cref="Update"/>.
    /// </summary>
    /// <param name="sessionId">Session to mutate.</param>
    /// <param name="mutate">Receives the current <see cref="Domain"/>; returns a
    /// <see cref="EvolutionResult"/> from <c>DomainEvolution.Apply()</c>.</param>
    /// <returns>The evolution result, or <c>null</c> if the session was not found.</returns>
    public static EvolutionResult? Evolve(
        string sessionId,
        Func<Domain, EvolutionResult> mutate) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        lock (StoreLock) {
            if (!Sessions.TryGetValue(sessionId, out var current))
                return null;

            var outcome = mutate(current.Domain);
            if (!outcome.Succeeded) {
                // On failure, keep the current state unchanged (revision unchanged).
                // Overwrite the analysis result though — the failed analysis diagnostics
                // are useful for debugging.
                Sessions[sessionId] = current with { LatestAnalysis = outcome.Analysis };
                return outcome;
            }

            var next = new McpSessionState(outcome.Root, outcome.Analysis, current.Revision + 1);
            Sessions[sessionId] = next;
            return outcome;
        }
    }

    /// <summary>
    /// Returns a list of all active session IDs.
    /// </summary>
    public static IReadOnlyList<string> ListSessions() {
        return Sessions.Keys.ToList();
    }

    /// <summary>
    /// Removes a session by ID. Returns true if it existed.
    /// </summary>
    public static bool Remove(string sessionId) {
        return Sessions.TryRemove(sessionId, out _);
    }
}