using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Runtime;
using Poly.Packs.Product;

namespace Poly.Mcp.Sessions;

/// <summary>
/// MCP session state: holds the current <see cref="Domain"/> root,
/// the analysis of that root, a monotonically increasing revision number,
/// the loaded <see cref="DomainSession"/>, and a runtime instance store.
/// Workspace/session management lives here in MCP — not in DomainModeling.
/// <para>
/// Do not copy this record with <c>with</c>: a copy would carry the lock, the cached
/// <c>_execution</c> pair and any live executor from the original, which belong to the domain
/// they were built for. Every state change constructs a new instance.
/// </para>
/// </summary>
internal sealed record McpSessionState(
    Domain Domain,
    AnalysisResult? LatestAnalysis,
    long Revision,
    DomainSession Modeling,
    string? SourceText = null
) {
    private readonly Lock _executorGate = new();
    private ModuleInstanceExecutor? _executor;
    private (DomainSession Session, AnalysisResult Analysis)? _execution;

    /// <summary>
    /// The one compilation this state runs: the session that lowers the module and the analysis
    /// that produced its constraint anchors. Both are created together, lazily, and never
    /// replaced.
    /// <para>
    /// <see cref="RuntimeAnalysisCache"/> keeps one session, analysis and module per
    /// <see cref="Domain"/> reference. A second session over one domain used to evict the module a
    /// live executor was running and re-lower it without the persistence overlay's
    /// <c>UniqueConstraintCompiler</c>; <c>Bind</c> now refuses that. One pair per state means the
    /// refusal is never reached, so
    /// binding it again is a no-op.
    /// </para>
    /// </summary>
    public (DomainSession Session, AnalysisResult Analysis) Execution {
        get {
            lock (_executorGate)
                return EnsureExecution();
        }
    }

    /// <summary>
    /// The session that lowers and runs this domain: the authored session, or the authored one
    /// plus the persistence library so a <c>unique</c> property is the storage-port check in the
    /// module's own bodies whether or not the source says <c>uses persistence</c>.
    /// </summary>
    public DomainSession ExecutionSession => Execution.Session;

    /// <summary>The analysis of <see cref="ExecutionSession"/>; carries the constraint anchors.</summary>
    public AnalysisResult ExecutionAnalysis => Execution.Analysis;

    /// <summary>
    /// The execution analysis when one has been built, without building it. Used by a caller that
    /// wants to report why <see cref="Execution"/> threw: lowering refuses a domain whose execution
    /// analysis has errors, and that analysis is the only place the reasons are recorded.
    /// </summary>
    public AnalysisResult? ExecutionAnalysisIfCreated {
        get { lock (_executorGate) return _execution?.Analysis; }
    }

    /// <summary>
    /// The session's module executor: runs the lowered operation module on dictionary
    /// instances. Created on first use, so a session that never creates an instance never
    /// lowers. A state replaced by <c>apply_dsl</c> or evolve starts with a fresh one.
    /// </summary>
    public ModuleInstanceExecutor Executor {
        get {
            lock (_executorGate) {
                if (_executor is not null)
                    return _executor;
                _ = EnsureExecution();
                return _executor = new ModuleInstanceExecutor(Domain);
            }
        }
    }

    /// <summary>Caller holds <see cref="_executorGate"/>; <see cref="System.Threading.Lock"/> is not reentrant.</summary>
    private (DomainSession Session, AnalysisResult Analysis) EnsureExecution() {
        if (_execution.HasValue)
            return _execution.Value;
        var ids = Domain.Extensions.ToList();
        var hasOverlay = ids.Any(id => id is "persistence" or "sqlite" or "sqlserver");
        var session = hasOverlay
            ? Modeling
            : DomainSession.ForExtensions([.. ids, "persistence"], ProductCatalog.Model);
        var analysis = session.Analyze(Domain);
        // Publish the pair before lowering: lowering refuses a domain whose execution analysis has
        // errors, and a caller that catches the throw still needs this analysis to report why.
        _execution = (session, analysis);
        RuntimeAnalysisCache.GetOrLower(Domain, session, analysis);
        return _execution.Value;
    }

    /// <summary>The executor if one exists; read-only paths use it so they never lower.</summary>
    public ModuleInstanceExecutor? ExecutorIfCreated {
        get { lock (_executorGate) return _executor; }
    }

    /// <summary>
    /// Drops every runtime instance while keeping the lowered module and the session that
    /// lowered it. Replacing the state instead would bind the same domain to a second
    /// session and evict the module (see <see cref="RuntimeAnalysisCache"/>), which broke
    /// the next create after a simulation reset.
    /// </summary>
    public void ResetInstances() {
        lock (_executorGate) {
            _executor = null;
        }
    }

    /// <summary>
    /// Instance ids (GUID strings) to the dictionary that is each instance, in creation
    /// order. A view over the executor, not a second store.
    /// </summary>
    public IReadOnlyDictionary<string, IDictionary<string, object?>> InstanceMap =>
        ExecutorIfCreated is { } executor
            ? executor.Instances.ToDictionary(i => i.Id, i => i.Bag, StringComparer.Ordinal)
            : new Dictionary<string, IDictionary<string, object?>>(StringComparer.Ordinal);
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
            Extensions = [.. ProductCatalog.ImplicitIds]
        };
        var modeling = DomainSession.Open(domain, ProductCatalog.Model);
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
        Func<Domain, DomainSession, AnalysisResult?, EvolutionResult> mutate) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));

        if (!_sessions.TryGetValue(sessionId, out var entry))
            return null;

        lock (entry.Sync) {
            Interlocked.Exchange(ref entry.LastUsedTicks, NowTicks);
            var current = entry.State;
            // A rejected outcome leaves the session as it was: its analysis is of the
            // proposal, not of the domain the session keeps.
            var outcome = mutate(current.Domain, current.Modeling, current.LatestAnalysis);
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
    /// <paramref name="sourceText"/> is the authored document the domain came from (null when it
    /// did not come from one). A non-null <paramref name="expectedRevision"/> that no longer matches
    /// writes nothing and returns false.
    /// Used by <c>apply_dsl</c> to replace the session with a freshly-parsed domain.
    /// <paramref name="modeling"/> is the ForSource session; required so a <c>uses</c>
    /// change does not re-open against Core.
    /// </summary>
    public bool Replace(
        string sessionId, Domain domain, AnalysisResult? analysis, DomainSession modeling,
        string? sourceText = null, long? expectedRevision = null) {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        ArgumentNullException.ThrowIfNull(modeling);

        if (!_sessions.TryGetValue(sessionId, out var entry))
            return false;

        lock (entry.Sync) {
            Interlocked.Exchange(ref entry.LastUsedTicks, NowTicks);
            var current = entry.State;
            if (expectedRevision is { } expected && current.Revision != expected)
                return false;
            entry.State = new McpSessionState(
                domain,
                analysis,
                current.Revision + 1,
                modeling.WithDomain(domain),
                sourceText);
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