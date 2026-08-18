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
    /// Subscription effect binding: subscriber props, peer binder path-prefix, legacy event ban.
    public const string SubscriptionEffectBinding = "DMSS004";

    // General system diagnostics
    public const string RuleCoverage = "DMSYS001";

    // Authoring suggestions (advisory hints)
    public const string AuthoringSuggestion = "DMAS001";

    /// Path-prefix on 'many' cardinality relationship (use collection quantifiers instead).
    public const string RelationshipNavigationCardinality = "DMREL001";

    // Unsupported / silently-dropped effect diagnostics

    /// Composite/Conditional effect contains direct-execution children that are silently dropped.
    public const string NestedDirectEffectDropped = "DMEFF006";

    /// Invoke quantifier/filter/relationship shape is invalid
    /// (e.g. any/all without a collection relationship, where on singular/self).
    public const string EffectInvokeShape = "DMEFF007";

    /// Assigned value violates property constraints (range, length, pattern, enum, required).
    public const string EffectConstraintViolation = "DMEFF008";

    /// Action declares <c>-&gt; T</c> but no create/create-in produces entity type T.
    public const string EffectReturnWithoutProducer = "DMEFF009";

    /// Action declares <c>-&gt; T</c> but its final statement does not produce T —
    /// the create/create-in yielding the return value must be the last statement
    /// (or every branch of a final conditional must produce it).
    public const string EffectReturnNotProducedByFinalStatement = "DMEFF010";

    /// A create / create-in does not provide a value for every <c>required</c>
    /// property of the created entity (auto-wired back-reference nav excluded).
    public const string CreateMissingRequiredProperty = "DMEFF011";

    // ── Aggregate / ownership diagnostics (APM Phase B) ────────

    /// Non-root entity has no aggregate parent — potentially orphaned.
    public const string AggregateOrphan = "DMAGG001";

    // DMAGG002 removed — AggregateAnalyzer reads IsRoot from EntityStructureMetadata,
    // so the two can never conflict in the pipeline. Add back if a real conflict signal
    // emerges (e.g. structural heuristic vs aggregate topology with create-in override).

    // ── Cross-reference / cycle diagnostics (APM Phase B) ──────

    /// Cross-entity dependency cycle detected (relationships + subscriptions).
    public const string DependencyCycle = "DMDEP001";

    // ── Behavior / action diagnostics (APM Phase B) ────────────

    /// Action has no require gates and no parameters — unconditionally invocable.
    public const string UnconditionalAction = "DMBEH001";
}