using Poly.Data.Modeling.TypeSystem;
using Poly.Syntax.Analysis;

namespace Poly.Data.Modeling;

/// <summary>
/// Domain-specific adapter over <see cref="SyntaxInvalidityExplainer"/> that maps domain
/// node names and diagnostic codes to actionable remediation hints.
/// </summary>
public static class DomainInvalidityExplainer {
    public static NodeInvalidityReport Explain(AnalysisResult analysis) =>
        SyntaxInvalidityExplainer.Explain(analysis, BuildHint, GetNodeName);

    private static string GetNodeName(Node node) => node switch {
        DomainMember member => member.Name,
        _ => node.ToString() ?? node.Id.Value
    };

    private static string BuildHint(string? code, string message) => code switch {
        DomainModelDiagnosticCodes.StructuralDuplicate => "Rename one of the duplicate members so names are unique within the same owner.",
        DomainModelDiagnosticCodes.StructuralCycle => "Break inheritance cycles by removing or re-pointing at least one parent link.",
        DomainModelDiagnosticCodes.StructuralOwnership => "Use entity-to-entity ownership with one-to-one or one-to-many cardinality.",
        DomainModelDiagnosticCodes.MutationInvariant => "Ensure all referenced nodes belong to the same domain and are registered before linking.",
        DomainModelDiagnosticCodes.SemanticStageInheritance => "Map each child stage to a valid parent stage when inheriting stage models.",
        DomainModelDiagnosticCodes.SemanticActionVisibility => "Ensure stage actions reference actions owned by the same entity.",
        DomainModelDiagnosticCodes.SemanticTypeCompatibility => "Use types declared in the same domain as the entity or action parameter.",
        DomainModelDiagnosticCodes.PolicyMissingProperty => "Verify every policy property reference exists on the target node.",
        DomainModelDiagnosticCodes.PolicyAstGeneration => "Inspect rule/constraint composition and simplify unsupported expressions.",
        DomainModelDiagnosticCodes.PolicyActorReference => "Ensure actor references and mappings point to existing actor definitions.",
        DomainModelDiagnosticCodes.EffectBinding => "Bind every required effect input to a compatible source property.",
        DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement => "Add or map the required property before applying this effect.",
        _ when message.Contains("Duplicate", StringComparison.Ordinal) => "Rename duplicates so each sibling member name is unique.",
        _ => "Review this diagnostic and adjust the referenced node configuration to satisfy analyzer invariants."
    };
}