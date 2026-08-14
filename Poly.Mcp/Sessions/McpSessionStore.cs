using System.Collections.Concurrent;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Packs;

namespace Poly.Mcp.Sessions;

/// <summary>
/// Shared default parser/analyzer inputs for MCP sessions.
/// New sessions snapshot these defaults into session state.
/// </summary>
internal static class McpDefaults {
    public static DomainParserInputs ParserInputs { get; } = ExtensionCatalog.Core.Authoring.Parser;
}

/// <summary>
/// MCP session state: holds the current <see cref="Domain"/> root,
/// the latest analysis result, a monotonically increasing revision number,
/// and a runtime instance store for exercising domain lifecycle.
/// No V2 types, no revision snapshot history.
/// Workspace/session management lives here in MCP — not in DomainModeling.
/// </summary>
internal sealed record McpSessionState(
    Domain Domain,
    AnalysisResult? LatestAnalysis,
    long Revision,
    DomainParserInputs ParserInputs
) {
    /// <summary>
    /// Runtime instance store for executing action/lifecycle behavior.
    /// Created fresh per session; null until first <c>create_instance</c>.
    /// Uses <see cref="DomainInstanceStore"/> for relationship-based
    /// subscription fan-out and instance-level links.
    /// </summary>
    public DomainInstanceStore? InstanceStore { get; set; }

    /// <summary>
    /// Maps instance IDs (GUID strings) to <see cref="DomainEntityInstance"/>
    /// objects for MCP tool access. Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// </summary>
    public ConcurrentDictionary<string, DomainEntityInstance> InstanceMap { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Thread-safe in-memory store for V3 domain sessions.
/// Sessions are identified by a string sessionId (typically a GUID).
/// Bootstrap uses <see cref="DomainFactory.Create"/> from the DomainModeling API.
/// </summary>
internal static class McpSessionStore {
    private static readonly ConcurrentDictionary<string, McpSessionState> Sessions = new(StringComparer.Ordinal);
    private static readonly Lock StoreLock = new();

    /// <summary>
    /// Creates a new session with a bootstrapped domain and the product authoring
    /// inputs (Temporal language + optional storage annotations).
    /// </summary>
    public static (string SessionId, McpSessionState State) Create(string domainName, string? preferredSessionId = null) =>
        Create(domainName, McpDefaults.ParserInputs, preferredSessionId);

    /// <summary>
    /// Creates a new session with explicit parser/printer inputs built once per session.
    /// <c>apply_dsl</c>, <c>export_dsl</c>, and policy-fragment parsing all consume
    /// these same session inputs so packs registered here shape both parse and print.
    /// </summary>
    public static (string SessionId, McpSessionState State) Create(
        string domainName,
        DomainParserInputs parserInputs,
        string? preferredSessionId = null) {
        ArgumentNullException.ThrowIfNull(parserInputs);
        lock (StoreLock) {
            var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
                ? Guid.NewGuid().ToString("N")
                : preferredSessionId;

            var domain = DomainFactory.Create(domainName) with {
                Extensions = [.. ExtensionCatalog.ProductAuthoring]
            };
            var analysis = DomainModelAnalyzer.Analyze(domain);
            var resolved = parserInputs;
            try {
                resolved = domain.ResolveHost().Parser;
            }
            catch (InvalidOperationException) {
                // Caller-supplied parserInputs (tests) when catalog cannot resolve.
            }
            if (!ReferenceEquals(parserInputs, McpDefaults.ParserInputs))
                resolved = parserInputs;
            var state = new McpSessionState(domain, analysis, Revision: 0, resolved);
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

            // Fresh state: new domain root clears InstanceMap/InstanceStore (entity refs stale).
            var next = new McpSessionState(
                outcome.Root,
                outcome.Analysis,
                current.Revision + 1,
                current.ParserInputs);
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

    /// <summary>
    /// Atomically replaces a session's domain and analysis. The revision counter
    /// is set to the current revision + 1 (not reset to zero), so agents see
    /// monotonically increasing revisions across both evolve and replace cycles.
    /// Runtime instances are cleared (new empty <see cref="McpSessionState.InstanceMap"/>)
    /// because they hold entity/identity references from the previous domain root.
    /// Used by <c>apply_dsl</c> to replace the session with a freshly-parsed domain.
    /// </summary>
    public static bool Replace(string sessionId, Domain domain, AnalysisResult? analysis) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        lock (StoreLock) {
            if (!Sessions.TryGetValue(sessionId, out var current))
                return false;
            DomainParserInputs parserInputs;
            try {
                parserInputs = domain.ResolveHost().Parser;
            }
            catch (InvalidOperationException) {
                parserInputs = current.ParserInputs;
            }
            Sessions[sessionId] = new McpSessionState(
                domain,
                analysis,
                current.Revision + 1,
                parserInputs);
            return true;
        }
    }

    /// <summary>
    /// Provides locked access to a session's state for runtime instance operations.
    /// The callback receives the current <see cref="McpSessionState"/> and can
    /// modify <c>InstanceMap</c> and <c>InstanceStore</c> in place (they are mutable).
    /// Returns <c>true</c> if the session was found, <c>false</c> otherwise.
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