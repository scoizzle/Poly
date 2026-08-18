using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Extra output files from the analyzed domain. Libraries register these on the
/// session builder. The compiler asks them only after analysis succeeds.
/// </summary>
public interface IArtifactContributor {
    /// <summary>Produces additional files for <paramref name="domain"/>, or an empty
    /// list when this contributor has nothing to emit for the analyzed domain.</summary>
    IReadOnlyList<(string FileName, string Source)> Contribute(Domain domain, AnalysisResult analysis);
}