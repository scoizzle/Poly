using Poly.Analysis;

namespace Poly.DomainModeling.Packs;

/// <summary>
/// Emits extra output files from the analyzed domain. Contributors run only after
/// domain analysis succeeds; structural analysis failures fail closed first and the
/// compiler never asks a contributor over a failed analysis.
/// </summary>
public interface IArtifactContributor {
    /// <summary>Produces additional files for <paramref name="domain"/>, or an empty
    /// list when this contributor has nothing to emit for the analyzed domain.</summary>
    IReadOnlyList<(string FileName, string Source)> Contribute(Domain domain, AnalysisResult analysis);
}