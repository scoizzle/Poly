using System.Collections.Concurrent;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;
using Poly.Syntax.Analysis;

namespace Poly.Mcp.Sessions;

/// <summary>
/// V3 session state: holds the current V3 <see cref="Domain"/> root,
/// the latest analysis result, and a monotonically increasing revision number.
/// No V2 types, no revision snapshot history.
/// Workspace/session management lives here in MCP — not in DomainModeling.
/// </summary>
internal sealed record V3SessionState(
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
    private static readonly ConcurrentDictionary<string, V3SessionState> Sessions = new(StringComparer.Ordinal);
    private static readonly Lock StoreLock = new();

    /// <summary>
    /// Creates a new session with a bootstrapped domain.
    /// </summary>
    public static (string SessionId, V3SessionState State) Create(string domainName, string? preferredSessionId = null) {
        lock (StoreLock) {
            var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
                ? Guid.NewGuid().ToString("N")
                : preferredSessionId;

            var domain = DomainFactory.Create(domainName);
            var analysis = DomainModelAnalyzer.Analyze(domain);
            var state = new V3SessionState(domain, analysis, Revision: 0);
            Sessions[sessionId] = state;
            return (sessionId, state);
        }
    }

    /// <summary>
    /// Gets an existing session by ID. Returns false if not found.
    /// </summary>
    public static bool TryGet(string sessionId, out V3SessionState session) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            session = null!;
            return false;
        }
        return Sessions.TryGetValue(sessionId, out session!);
    }

    /// <summary>
    /// Atomically updates a session with a new domain and analysis result.
    /// Bumps the revision. Throws if the session doesn't exist.
    /// </summary>
    public static V3SessionState Update(string sessionId, Domain newDomain, AnalysisResult analysis) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        lock (StoreLock) {
            if (!Sessions.TryGetValue(sessionId, out var current))
                throw new InvalidOperationException($"Session '{sessionId}' not found.");

            var next = new V3SessionState(newDomain, analysis, current.Revision + 1);
            Sessions[sessionId] = next;
            return next;
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