namespace Poly.DomainModeling.Analysis;

internal static class DomainModelDiagnosticCodes {
    public const string StructuralDuplicate = "DMSTR001";
    public const string StructuralCycle = "DMSTR002";
    public const string StructuralOwnership = "DMSTR003";
    /// <summary>
    /// A user-declared type reuses a name lowering emits at module scope
    /// (<c>{Entity}Stage</c> for an entity that has stages, or <c>DomainResult</c>).
    /// </summary>
    public const string ReservedGeneratedName = "DMSTR004";
    /// <summary>
    /// A collection navigation's generated C# member name (PascalCase of the nav) equals
    /// the name of the entity type it points at, and lowering emits a create-in factory
    /// for that nav. The collection member's type (<c>IReadOnlyList&lt;T&gt;</c>) differs
    /// from its name, so C#'s Color Color rule does not apply, and the unqualified
    /// <c>{Type}.Create(...)</c> factory call binds to the member — the generated host
    /// does not compile. Singular navs are exempt (their member type is the target type).
    /// </summary>
    public const string NavigationShadowsEntityType = "DMSTR005";

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
    public const string EntityLevelAction = "DMAS002";

    /// Path-prefix on 'many' cardinality relationship (use collection quantifiers instead).
    public const string RelationshipNavigationCardinality = "DMREL001";

    /// A collection relationship that a subscription, a <c>for</c> loop, or an expression
    /// reader (a <c>count</c>/<c>any</c>/<c>all</c>/<c>none</c>/<c>sum</c> quantifier or a
    /// <c>Rel exists</c> test) reads while nothing in the domain writes it — no <c>create in</c>
    /// for it and it is not the target's aggregate parent relationship — so the published
    /// dispatch plan cannot fire, the loop matches nothing, and the expression reads an empty
    /// set. Only an explicit link (or a host) can populate it.
    public const string UnwrittenRelationship = "DMREL002";

    // Retired: nested invoke/create/transition now lower (session.Lower). Kept so
    // historical messages remain identifiable; analysis no longer reports it.

    /// Retired. Nested effects in Composite/Conditional lower to Syntax; they are not dropped.
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

    /// M1: a mutation targets another entity's state. Only the owning entity's named
    /// behavior (actions; entry, exit and when blocks per V4) may mutate it; change
    /// another entity by invoking one of its actions.
    public const string EffectCrossEntityMutation = "DMEFF012";

    /// <summary>
    /// An entity's automatic transition graph (entry + exit, including nested in <c>if</c>)
    /// has a cycle where every edge is unconditional — no real guard, or a guard Analyze
    /// can prove is always true. Guarded cycles are allowed; the runtime loop guard is the
    /// backstop when a guard never becomes false.
    /// </summary>
    public const string UnconditionalAutomaticTransitionCycle = "DMEFF013";

    // ── Aggregate / ownership diagnostics (APM Phase B) ────────

    // DMAGG001 removed — the aggregate now promotes a parentless entity to a root, so
    // "non-root with no aggregate parent" cannot exist. It was a false-positive machine
    // (fixed for grandchildren, then for singular-only chains: research/2026-10-10-analysis-review.md D2).

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