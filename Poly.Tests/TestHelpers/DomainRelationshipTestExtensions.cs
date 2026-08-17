// Deliberately in the Poly.DomainModeling namespace (test assembly) so every test
// file that constructs a Domain (via `using Poly.DomainModeling;`) sees this helper.
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.DomainModeling;

/// <summary>
/// Test-only relationship accessor that reads the analysis result's catalog (via
/// <see cref="DomainSemanticLookupExtensions.GetAllRelationships"/>) — no ontology
/// flatten. Relationship semantics are analysis-only; tests route through the
/// analysis result they already hold on <see cref="EvolutionResult"/>.
/// </summary>
public static class DomainRelationshipTestExtensions {
    public static IReadOnlyList<Relationship> Relationships(this EvolutionResult result) =>
        result.Analysis.GetAllRelationships(result.Root);
}