using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Poly.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.Packs.Product;

namespace Poly.Mcp.Sessions;

/// <summary>
/// MCP session state: holds the current <see cref="Domain"/> root,
/// the analysis of that root, a monotonically increasing revision number,
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
/// Thread-safe in-memory store for domain sessions, registered as a singleton.
/// One lock per session: a slow mutation in one session never blocks another.
/// Sessions are identified by a string sessionId (typically a GUID); identity is
/// that id, never a transport header.
/// </summary>
internal sealed class McpSessionStore(
    TimeProvider time,
    IOptions<McpSessionOptions> options,
    ILogger<McpSessionStore> logger) : ISessionStore {

    private sealed class Entry(McpSessionState state, long now) {
        /// <summary>Per-session lock; never replaced, so concurrent mutators serialize.</summary>
        public readonly Lock Sync = new();
        public McpSessionState State = state;
        public long LastUsedTicks = now;
    }

    private readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.Ordinal);
    private readonly TimeSpan _idleTimeout = options.Value.IdleTimeout;

    private long NowTicks => time.GetUtcNow().UtcTicks;

    /// <summary>
    /// Creates a new session with a bootstrapped domain and the product authoring
    /// session (Temporal language + storage annotations). Overwrites an existing id.
    /// </summary>
    public (string SessionId, McpSessionState State) Create(string domainName, string? preferredSessionId = null) {
        var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
            ? Guid.NewGuid().ToString("N")
            : preferredSessionId;

        var domain = DomainFactory.Create(domainName) with {
            Extensions = [.. ExtensionCatalog.ProductAuthoring]
        };
        var modeling = DomainSession.Open(domain, ProductCatalog.Catalog);
        var analysis = modeling.Analyze(domain);
        var state = new McpSessionState(domain, analysis, Revision: 0, modeling);
        _sessions[sessionId] = new Entry(state, NowTicks);
        logger.LogInformation("MCP session {SessionId} created ({Domain})", sessionId, domainName);
        return (sessionId, state);
    }

    /// <summary>
    /// Gets an existing session by ID and marks it used. Returns false if not found.
    /// </summary>
    public bool TryGet(string sessionId, out McpSessionState session) {
        session = null!;
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;
        if (!_sessions.TryGetValue(sessionId, out var entry))
            return false;
        Interlocked.Exchange(ref entry.LastUsedTicks, NowTicks);
        session = entry.State;
        return true;
    }

    /// <summary>
    /// Atomically reads, mutates, and writes a session under that session's lock,
    /// preventing the lost-update race that occurs when callers do an unprotected
    /// <see cref="TryGet"/> → evolve → write.
    /// </summary>
    public EvolutionResult? Evolve(
        string sessionId,
        Func<Domain, DomainSession, EvolutionResult> mutate) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        if (!_sessions.TryGetValue(sessionId, out var entry))
            return null;

        lock (entry.Sync) {
            Interlocked.Exchange(ref entry.LastUsedTicks, NowTicks);
            var current = entry.State;
            // A rejected outcome leaves the session as it was: its analysis is of the
            // proposal, not of the domain the session keeps.
            var outcome = mutate(current.Domain, current.Modeling);
            if (!outcome.Succeeded)
                return outcome;

            var modeling = current.Modeling.WithDomain(outcome.Root);
            entry.State = new McpSessionState(
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
    public IReadOnlyList<string> ListSessions() {
        return _sessions.Keys.ToList();
    }

    /// <summary>
    /// Removes a session by ID. Returns true if it existed.
    /// </summary>
    public bool Remove(string sessionId) {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;
        var removed = _sessions.TryRemove(sessionId, out _);
        if (removed)
            logger.LogInformation("MCP session {SessionId} removed", sessionId);
        return removed;
    }

    /// <summary>
    /// Atomically replaces a session's domain and analysis. The revision counter
    /// is set to the current revision + 1. Runtime instances are cleared.
    /// Used by <c>apply_dsl</c> to replace the session with a freshly-parsed domain.
    /// <paramref name="modeling"/> is the ForSource session; required so a <c>uses</c>
    /// change does not re-open against Core.
    /// </summary>
    public bool Replace(
        string sessionId, Domain domain, AnalysisResult? analysis, DomainSession modeling) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        ArgumentNullException.ThrowIfNull(modeling);

        if (!_sessions.TryGetValue(sessionId, out var entry))
            return false;

        lock (entry.Sync) {
            Interlocked.Exchange(ref entry.LastUsedTicks, NowTicks);
            var current = entry.State;
            entry.State = new McpSessionState(
                domain,
                analysis,
                current.Revision + 1,
                modeling.WithDomain(domain));
            return true;
        }
    }

    /// <summary>
    /// Provides locked access to a session's state for runtime instance operations.
    /// </summary>
    public bool TryModifyInstances(string sessionId, Action<McpSessionState> action) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        if (!_sessions.TryGetValue(sessionId, out var entry))
            return false;

        lock (entry.Sync) {
            Interlocked.Exchange(ref entry.LastUsedTicks, NowTicks);
            action(entry.State);
            return true;
        }
    }

    /// <summary>
    /// Removes sessions idle longer than the configured timeout. A session in use
    /// is skipped and picked up by the next sweep.
    /// </summary>
    public int EvictIdle() {
        if (_idleTimeout <= TimeSpan.Zero)
            return 0;
        var cutoff = NowTicks - _idleTimeout.Ticks;
        var evicted = 0;
        foreach (var (sessionId, entry) in _sessions) {
            if (Interlocked.Read(ref entry.LastUsedTicks) > cutoff)
                continue;
            if (!entry.Sync.TryEnter())
                continue;
            try {
                if (Interlocked.Read(ref entry.LastUsedTicks) > cutoff)
                    continue;
                if (_sessions.TryRemove(new KeyValuePair<string, Entry>(sessionId, entry))) {
                    logger.LogInformation(
                        "MCP session {SessionId} evicted after {IdleTimeout} idle", sessionId, _idleTimeout);
                    evicted++;
                }
            }
            finally {
                entry.Sync.Exit();
            }
        }
        return evicted;
    }
}