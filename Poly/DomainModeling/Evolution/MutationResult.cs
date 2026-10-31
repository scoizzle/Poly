using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// The result of <see cref="DomainEvolution.Apply"/>. Mutation only: no analysis ran,
/// and this value does not carry an <see cref="Poly.Analysis.AnalysisResult"/>.
/// On a mutation error the original root is returned and the proposal is discarded.
/// </summary>
public sealed record MutationResult(
    Domain Root,
    EvolutionTrace Trace,
    bool Succeeded,
    bool WasRolledBack,
    IReadOnlyList<string> MutationErrors
) {
    public string? FailureSummary =>
        Succeeded ? null : string.Join("; ", MutationErrors.Take(3));

    public static MutationResult Applied(Domain root, EvolutionTrace trace) =>
        new(root, trace, Succeeded: true, WasRolledBack: false, MutationErrors: []);

    public static MutationResult Rejected(
        Domain originalRoot,
        EvolutionTrace trace,
        IReadOnlyList<string> mutationErrors) =>
        new(originalRoot, trace, Succeeded: false, WasRolledBack: true, mutationErrors);
}