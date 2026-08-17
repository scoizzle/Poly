using System.Collections.Concurrent;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Mcp.Sessions;

/// <summary>
/// MCP session state: holds the current <see cref="Domain"/> root,
/// the latest analysis result, a monotonically increasing revision number,
/// the loaded <see cref="DomainSession"/>, and a runtime instance store.
/// Workspace/session management lives here in MCP — not in DomainModeling.
/// </summary>
internal sealed record McpSessionState(
    Domain Domain,
    AnalysisResult? LatestAnalysis,
    long Revision,
    DomainSession Modeling
) {
    /// <summary>
    /// Runtime instance store for executing action/lifecycle behavior.
    /// Created fresh per session; null until first <c>create_instance</c>.
    /// </summary>
    public DomainInstanceStore? InstanceStore { get; set; }

    /// <summary>
    /// Maps instance IDs (GUID strings) to <see cref="DomainEntityInstance"/>
    /// objects for MCP tool access.
    /// </summary>
    public ConcurrentDictionary<string, DomainEntityInstance> InstanceMap { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Thread-safe in-memory store for domain sessions.
/// Sessions are identified by a string sessionId (typically a GUID).
/// Bootstrap uses <see cref="DomainFactory.Create"/> from the DomainModeling API.
/// </summary>
internal static class McpSessionStore {
    private static readonly ConcurrentDictionary<string, McpSessionState> Sessions = new(StringComparer.Ordinal);
    private static readonly Lock StoreLock = new();

    /// <summary>
    /// Creates a new session with a bootstrapped domain and the product authoring
    /// session (Temporal language + storage annotations).
    /// </summary>
    public static (string SessionId, McpSessionState State) Create(string domainName, string? preferredSessionId = null) {
        lock (StoreLock) {
            var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
                ? Guid.NewGuid().ToString("N")
                : preferredSessionId;

            var domain = DomainFactory.Create(domainName) with {
                Extensions = [.. ExtensionCatalog.ProductAuthoring]
            };
            var modeling = DomainSession.Open(domain);
            var analysis = modeling.Analyze(domain);
            var state = new McpSessionState(domain, analysis, Revision: 0, modeling);
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
    public static EvolutionResult? Evolve(
        string sessionId,
        Func<Domain, DomainSession, EvolutionResult> mutate) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        lock (StoreLock) {
            if (!Sessions.TryGetValue(sessionId, out var current))
                return null;

            var outcome = mutate(current.Domain, current.Modeling);
            if (!outcome.Succeeded) {
                Sessions[sessionId] = current with { LatestAnalysis = outcome.Analysis };
                return outcome;
            }

            var modeling = current.Modeling.WithDomain(outcome.Root);
            Sessions[sessionId] = new McpSessionState(
                outcome.Root,
                outcome.Analysis,
                current.Revision + 1,
                modeling);
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

    /// <summary>
    /// Atomically replaces a session's domain and analysis. The revision counter
    /// is set to the current revision + 1. Runtime instances are cleared.
    /// Used by <c>apply_dsl</c> to replace the session with a freshly-parsed domain.
    /// </summary>
    public static bool Replace(string sessionId, Domain domain, AnalysisResult? analysis) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        lock (StoreLock) {
            if (!Sessions.TryGetValue(sessionId, out var current))
                return false;
            var modeling = current.Modeling.WithDomain(domain);
            Sessions[sessionId] = new McpSessionState(
                domain,
                analysis,
                current.Revision + 1,
                modeling);
            return true;
        }
    }

    /// <summary>
    /// Provides locked access to a session's state for runtime instance operations.
    /// </summary>
    public static bool TryModifyInstances(string sessionId, Action<McpSessionState> action) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        lock (StoreLock) {
            if (!Sessions.TryGetValue(sessionId, out var current))
                return false;
            action(current);
            return true;
        }
    }
}