using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Entry point for analysis-gated evolution over an immutable Domain.
/// 
/// This is the thin layer that preserves the model evolution pattern (batch changes,
/// analysis gate, rich trace + original root on failure) while the underlying model
/// is immutable records. There is no explicit transaction/commit model — atomicity
/// is a natural consequence of immutable values.
/// </summary>
public sealed class DomainEvolution {
    private readonly Domain _current;
    private readonly DomainModelAnalyzer _analyzer;

    public DomainEvolution(Domain current, DomainModelAnalyzer? analyzer = null) {
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _analyzer = analyzer ?? new DomainModelAnalyzer();
    }

    public Domain Current => _current;

    /// <summary>
    /// Applies a batch of changes against the current snapshot.
    /// Produces a proposed new root, runs analysis, and returns either a successful
    /// result with the new root or a rolled-back result containing the original root + diagnostics.
    /// </summary>
    public EvolutionResult Apply(IReadOnlyList<DomainChange> changes, AnalysisResult? priorAnalysis = null) {
        // The real implementation will interpret changes to produce a new Domain
        // (using V3 builders or pure construction helpers) and then run analysis.
        // Current skeleton is identity + full analysis for early testing.
        Domain proposed = ApplyChanges(_current, changes);

        var analysis = priorAnalysis is null
            ? Analyze(proposed)
            : Analyze(proposed, priorAnalysis, GetAffectedNodes(changes));

        var hasErrors = analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        var trace = BuildTrace(changes, hasErrors, analysis);

        return hasErrors
            ? EvolutionResult.RolledBack(_current, analysis, trace)
            : EvolutionResult.Success(proposed, analysis, trace);
    }

    /// <summary>
    /// Starts a fluent evolution builder for ergonomic batch construction.
    /// All changes collected through the builder still go through the single
    /// analysis gate when the final Apply() is called.
    /// </summary>
    public EvolutionBuilder Evolve() => new(this, _current);

    // --- Internal analysis hooks (will be refined for incremental support) ---

    internal AnalysisResult Analyze(Domain domain)
        => _analyzer.Analyze(domain);

    internal AnalysisResult Analyze(Domain domain, AnalysisResult prior, IEnumerable<Node> affected)
        => _analyzer.Analyze(domain, prior, affected);

    // --- Placeholder applicator / trace logic (to be replaced with real implementation) ---

    private Domain ApplyChanges(Domain current, IReadOnlyList<DomainChange> changes) {
        // Temporary identity so the layer can be exercised end-to-end while the
        // real change interpreter + builder integration is built.
        return current;
    }

    private IReadOnlyList<Node> GetAffectedNodes(IReadOnlyList<DomainChange> changes)
        => Array.Empty<Node>();

    private EvolutionTrace BuildTrace(
        IReadOnlyList<DomainChange> changes,
        bool rolledBack,
        AnalysisResult analysis) {
        var steps = changes
            .Select(c => new EvolutionStep(c.GetType().Name, Array.Empty<string>()))
            .ToList();

        return new EvolutionTrace(
            steps,
            AffectedNodeIds: Array.Empty<string>(),
            RolledBack: rolledBack,
            Duration: TimeSpan.Zero,
            ErrorCount: analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
            WarningCount: analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning));
    }
}

/// <summary>
/// Lightweight fluent builder for accumulating changes before a single analysis-gated Apply.
/// This is the primary ergonomic surface for agents and future UI-driven evolution.
/// </summary>
public sealed class EvolutionBuilder {
    private readonly DomainEvolution _evolution;
    private readonly Domain _startingRoot;
    private readonly List<DomainChange> _changes = new();

    internal EvolutionBuilder(DomainEvolution evolution, Domain startingRoot) {
        _evolution = evolution;
        _startingRoot = startingRoot;
    }

    public EvolutionBuilder Apply(DomainChange change) {
        ArgumentNullException.ThrowIfNull(change);
        _changes.Add(change);
        return this;
    }

    /// <summary>
    /// Executes the accumulated changes through the analysis gate.
    /// Returns either a successful EvolutionResult with the new root,
    /// or a rolled-back result with the original root + diagnostics + trace.
    /// </summary>
    public EvolutionResult Apply(AnalysisResult? priorAnalysis = null)
        => _evolution.Apply(_changes, priorAnalysis);
}