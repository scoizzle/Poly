using System.Collections.Concurrent;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.V2;

/// <summary>
/// A live modeling session wrapping a <see cref="Domain"/> with revision tracking, cached analysis,
/// snapshot history, and a unified intent-based mutation API optimized for UI, API, and MCP consumers.
/// </summary>
public sealed class DomainSession {
    private const int MaxSnapshotRevisions = 64;

    private readonly Lock _lock = new();
    private readonly DomainModelAnalyzer _analyzer;
    private readonly Dictionary<long, DomainSnapshot> _snapshots = [];
    private readonly DomainMutationIntentEngine _engine = new();

    private AnalysisResult? _latestAnalysis;
    private long _revision;

    internal DomainSession(Domain domain, DomainModelAnalyzer analyzer, AnalysisResult? initialAnalysis, long initialRevision) {
        Domain = domain;
        _analyzer = analyzer;
        _latestAnalysis = initialAnalysis;
        _revision = initialRevision;
        _snapshots[initialRevision] = DomainDiffUtil.CaptureSnapshot(domain);
    }

    /// <summary>The underlying domain being managed by this session.</summary>
    public Domain Domain { get; }

    /// <summary>The current revision number, incremented on each successful transaction.</summary>
    public long Revision => Volatile.Read(ref _revision);

    /// <summary>The most recent analysis result; may be null before the first successful transaction.</summary>
    public AnalysisResult? LatestAnalysis => _latestAnalysis;

    /// <summary>
    /// Applies a single intent as an atomic transaction and returns the result.
    /// Rolls back all mutations if analysis produces errors.
    /// </summary>
    public DomainTransactionResult Apply(DomainMutationIntent intent) {
        ArgumentNullException.ThrowIfNull(intent);
        return Apply([intent]);
    }

    /// <summary>
    /// Applies a batch of intents as a single atomic transaction and returns the result.
    /// Rolls back all mutations if analysis produces errors or if any intent cannot be applied.
    /// </summary>
    public DomainTransactionResult Apply(IEnumerable<DomainMutationIntent> intents) {
        ArgumentNullException.ThrowIfNull(intents);

        lock (_lock) {
            DomainMutationExecutionResult execResult;

            try {
                execResult = _engine.ApplyWithTrace(Domain, intents, _analyzer, _latestAnalysis);
            }
            catch (Exception ex) {
                var errorMessage = $"[Error] IntentDispatch: {ex.Message}";
                var failedTrace = new DomainMutationTrace(
                    Steps: [],
                    AffectedNodeIds: [],
                    AppliedStepCount: 0,
                    RolledBack: true,
                    Succeeded: false,
                    Duration: TimeSpan.Zero,
                    ErrorCount: 1,
                    WarningCount: 0);
                return new DomainTransactionResult(
                    Succeeded: false,
                    Revision: _revision,
                    Trace: failedTrace,
                    Diagnostics: [errorMessage]);
            }

            var trace = execResult.Trace;
            var analysis = execResult.Analysis;

            if (trace.Succeeded) {
                _latestAnalysis = analysis;
                var nextRevision = _revision + 1;
                _snapshots[nextRevision] = DomainDiffUtil.CaptureSnapshot(Domain);
                TrimSnapshots();
                _revision = nextRevision;
            }

            var diagnostics = analysis.Diagnostics
                .Select(static d => $"[{d.Severity}] {d.Code}: {d.Message}")
                .ToArray();

            return new DomainTransactionResult(trace.Succeeded, _revision, trace, diagnostics);
        }
    }

    /// <summary>
    /// Returns an immutable snapshot of the domain model suitable for serialization and diffing.
    /// </summary>
    public DomainModelSnapshot ToSnapshot() => SnapshotBuilder.Build(Domain);

    /// <summary>
    /// Renders the domain as human-readable ASCII text.
    /// </summary>
    public string RenderAsText() => DomainRenderer.Render(Domain);

    /// <summary>
    /// Renders a single entity as human-readable ASCII text.
    /// </summary>
    public string RenderEntityAsText(string entityName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        var entity = Domain.RequireEntity(entityName);
        return DomainRenderer.RenderEntitySummary(entity);
    }

    /// <summary>
    /// Tries to retrieve the <see cref="DomainSnapshot"/> captured at the given revision.
    /// </summary>
    public bool TryGetRevisionSnapshot(long revision, out DomainSnapshot? snapshot) {
        lock (_lock) {
            return _snapshots.TryGetValue(revision, out snapshot);
        }
    }

    private void TrimSnapshots() {
        if (_snapshots.Count > MaxSnapshotRevisions) {
            foreach (var key in _snapshots.Keys.OrderBy(static k => k).Take(_snapshots.Count - MaxSnapshotRevisions).ToArray()) {
                _snapshots.Remove(key);
            }
        }
    }
}
