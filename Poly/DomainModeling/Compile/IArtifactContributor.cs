using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Extra output files from the analyzed domain. Libraries register these on the
/// session builder. Contributors run only after domain analysis succeeds; an
/// analysis with Errors fails closed first (see <c>DomainSession.Lower</c>) and
/// the compiler never asks a contributor over a failed analysis.
/// </summary>
public interface IArtifactContributor {
    /// <summary>Produces additional files for <paramref name="domain"/>, or an empty
    /// list when this contributor has nothing to emit for the analyzed domain.</summary>
    IReadOnlyList<(string FileName, string Source)> Contribute(Domain domain, AnalysisResult analysis);
}