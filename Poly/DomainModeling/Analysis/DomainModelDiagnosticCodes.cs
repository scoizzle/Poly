namespace Poly.DomainModeling.Analysis;

internal static class DomainModelDiagnosticCodes {
    public const string StructuralDuplicate = "DMSTR001";
    public const string StructuralCycle = "DMSTR002";
    public const string StructuralOwnership = "DMSTR003";

    public const string SemanticTypeCompatibility = "DMSEM003";
    public const string SemanticConstraintMismatch = "DMSEM004";
    public const string SemanticReferenceResolution = "DMSEM005";

    public const string EffectBinding = "DMEFF001";
    public const string EffectUnsatisfiedRequirement = "DMEFF002";
    public const string EffectPrePostCondition = "DMEFF003";
    public const string EffectUnusedParameter = "DMEFF004";

    public const string ConstraintSatisfiability = "DMCS001";
    public const string ConstraintFixedPoint = "DMCS002";

    public const string ContractIntegration = "DMCON001";

    // Stage-subscription diagnostics (replaced retired DMEV* event codes)
    public const string SubscriptionCausalityCycle = "DMSS001";
    public const string SubscriptionIdempotencyReplay = "DMSS002";
    public const string SubscriptionContractMismatch = "DMSS003";
    /// Unknown this.* / event.* property reference in a subscription effect expression.
    public const string SubscriptionEffectBinding = "DMSS004";

    // General system diagnostics
    public const string RuleCoverage = "DMSYS001";

    // Authoring suggestions (advisory hints)
    public const string AuthoringSuggestion = "DMAS001";

    /// Path-prefix on 'many' cardinality relationship (use Q3′ quantifiers instead).
    public const string RelationshipNavigationCardinality = "DMREL001";

    // Unsupported / silently-dropped effect diagnostics

    /// TransitionRelationshipEffect is parsed and stored but NOT executed at runtime.
    public const string EffectNotExecutable = "DMEFF005";

    /// Composite/Conditional effect contains direct-execution children that are silently dropped.
    public const string NestedDirectEffectDropped = "DMEFF006";

    /// Invoke quantifier/filter/relationship shape is invalid
    /// (e.g. any/all without a collection relationship, where on singular/self).
    public const string EffectInvokeShape = "DMEFF007";
}