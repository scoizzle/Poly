using AnalysisResult = Poly.Syntax.Analysis.AnalysisResult;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// The result of an evolution operation (via Apply or the fluent Evolve() builder).
/// Contains either a new successful root or the original root on analysis failure,
/// plus the analysis diagnostics and a rich trace for agents / UI / debugging.
/// 
/// This shape preserves the "ApplyWithTrace + clear rollback on error" experience
/// from V2 while taking advantage of immutable records (no compensating rollback logic needed).
/// </summary>
public sealed record EvolutionResult(
    Domain Root,
    AnalysisResult Analysis,
    EvolutionTrace Trace,
    bool Succeeded,
    bool WasRolledBack
) {
    public static EvolutionResult Success(
        Domain root,
        AnalysisResult analysis,
        EvolutionTrace trace) =>
        new(root, analysis, trace, Succeeded: true, WasRolledBack: false);

    public static EvolutionResult RolledBack(
        Domain originalRoot,
        AnalysisResult analysis,
        EvolutionTrace trace) =>
        new(originalRoot, analysis, trace, Succeeded: false, WasRolledBack: true);
}