using Poly.DomainModeling.Ontology;

using AnalysisResult = Poly.Analysis.AnalysisResult;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// The result of an evolution operation (via Apply or the fluent Evolve() builder).
/// Contains either a new successful root or the original root on analysis failure,
/// plus the analysis diagnostics and a rich trace for agents / UI / debugging.
/// 
/// Because the model is immutable, there is no actual "rollback" operation.
/// On failure the proposed root is simply discarded. The <c>WasRolledBack</c> flag
/// and <c>RolledBack</c> factory are retained as an observable signal for agents and
/// future UI so they can clearly distinguish accepted vs. rejected proposals.
/// </summary>
public sealed record EvolutionResult(
    Domain Root,
    AnalysisResult Analysis,
    EvolutionTrace Trace,
    bool Succeeded,
    bool WasRolledBack,
    IReadOnlyList<string> MutationErrors
) {
    /// <summary>
    /// Short, human-readable summary of the primary errors when the proposal was rejected.
    /// Mutation-target failures and analysis errors, in that order.
    /// </summary>
    public string? FailureSummary {
        get {
            if (Succeeded) return null;

            var errors = MutationErrors
                .Concat(Analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message))
                .Take(3);

            return string.Join("; ", errors);
        }
    }

    public static EvolutionResult Success(
        Domain root,
        AnalysisResult analysis,
        EvolutionTrace trace) =>
        new(root, analysis, trace, Succeeded: true, WasRolledBack: false, MutationErrors: []);

    public static EvolutionResult RolledBack(
        Domain originalRoot,
        AnalysisResult analysis,
        EvolutionTrace trace,
        IReadOnlyList<string>? mutationErrors = null) =>
        new(originalRoot, analysis, trace, Succeeded: false, WasRolledBack: true,
            MutationErrors: mutationErrors ?? []);
}