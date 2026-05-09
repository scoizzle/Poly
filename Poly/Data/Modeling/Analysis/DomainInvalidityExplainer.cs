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
        DomainModelDiagnosticCodes.SemanticConstraintMismatch => "Constrain child enum/property variants to remain a subset of inherited constraints.",
        DomainModelDiagnosticCodes.PolicyMissingProperty => "Verify every policy property reference exists on the target node.",
        DomainModelDiagnosticCodes.PolicyAstGeneration => "Inspect rule/constraint composition and simplify unsupported expressions.",
        DomainModelDiagnosticCodes.PolicyActorReference => "Ensure actor references and mappings point to existing actor definitions.",
        DomainModelDiagnosticCodes.EffectBinding => "Bind every required effect input to a compatible source property.",
        DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement => "Add or map the required property before applying this effect.",
        DomainModelDiagnosticCodes.EffectPrePostCondition => "Reorder or split effects so post-state mutations do not occur after destructive transitions.",
        DomainModelDiagnosticCodes.ActionTrigger => "Configure action triggers and subscriptions so command and event-handler semantics stay aligned.",
        DomainModelDiagnosticCodes.EventSubscription => "Ensure each event subscription points to valid handler, event type, and correlation bindings.",
        DomainModelDiagnosticCodes.ActionEventContract => "Align handler event parameter shape with the trigger event payload contract.",
        DomainModelDiagnosticCodes.EventFlowLiveness => "Ensure events have both producers and consumers where behavior depends on event flow.",
        DomainModelDiagnosticCodes.EventCorrelationSoundness => "Use unique, complete correlation bindings for stable event-to-entity routing.",
        DomainModelDiagnosticCodes.ActionOrderingCausality => "Break invoke/publish cycles or add explicit boundaries to avoid recursive action loops.",
        DomainModelDiagnosticCodes.ActionIdempotencyReplay => "Guard create/link/publish side effects for replay-safe event handling.",
        DomainModelDiagnosticCodes.ConstraintFixedPoint => "Preserve parent property constraints or intentionally tighten them in child overrides.",
        DomainModelDiagnosticCodes.ConstraintSatisfiability => "Adjust constraint bounds/combinations so at least one value can satisfy the property contract.",
        DomainModelDiagnosticCodes.RuleCoverage => "Ensure mutation paths explicitly satisfy required property constraints.",
        DomainModelDiagnosticCodes.DiagnosticDrift => "Keep diagnostic code catalog, analyzer output, and contract tests aligned.",
        DomainModelDiagnosticCodes.ContractIntegration => "Align imported contract endpoints and local action parameters so contract bindings are type-safe and resolvable.",
        _ when message.Contains("Duplicate", StringComparison.Ordinal) => "Rename duplicates so each sibling member name is unique.",
        _ => "Review this diagnostic and adjust the referenced node configuration to satisfy analyzer invariants."
    };
}