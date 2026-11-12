namespace Poly.DomainModeling.Analysis;

internal static class DomainModelDiagnosticCodes {
    public const string StructuralDuplicate = "DMSTR001";
    public const string StructuralCycle = "DMSTR002";
    public const string StructuralOwnership = "DMSTR003";
    /// <summary>
    /// A user-declared type reuses a name lowering emits at module scope:
    /// <c>{Entity}Stage</c> for an entity whose stage enum is generated (not an implicit
    /// single stage), <c>DomainResult</c> / <c>DomainResult&lt;T&gt;</c>,
    /// <c>ConstraintFailureException</c>, or the <c>System</c> namespace the generated C#
    /// imports.
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

    /// <summary>
    /// An entity property is typed as a domain <c>value</c> type. A value type is a
    /// contract payload / action parameter with no storage representation, so a stored
    /// column for it cannot be mapped (the host throws on first use).
    /// </summary>
    public const string StoredValueTypeProperty = "DMSTR006";

    /// <summary>
    /// An entity's key designation (<c>Entity.KeyProperties</c>, #405) is malformed: it names no
    /// member, or a member that is optional, assigned by an effect (not immutable), a collection
    /// navigation, or a navigation whose target entity has no key. A duplicate member is
    /// <see cref="StructuralDuplicate"/> and a navigation-key cycle is <see cref="StructuralCycle"/>.
    /// </summary>
    public const string InvalidKeyDesignation = "DMSTR007";

    /// <summary>
    /// An entity's <c>initial(Stage)</c> designation (#675) names no stage of that entity, or the
    /// entity declares none. <see cref="Ontology.EntityStages.InitialStage"/> falls back to the first
    /// declared stage so create still works; this is the error that says the designation is wrong.
    /// </summary>
    public const string InvalidInitialStageDesignation = "DMINI001";

    /// <summary>
    /// Two members of one entity share a generated identifier: the C# member name (a
    /// property keeps its name, a navigation is PascalCased, an action and a policy keep
    /// theirs) or the constructor-parameter name (<c>ToCamelCase</c>). The two names differ
    /// but the emitter derives one identifier for both (CS0102/CS0100).
    /// </summary>
    public const string GeneratedIdentifierCollision = "DMSTR008";

    /// <summary>
    /// A member's generated identity is a name the exporter reserves on the entity (the
    /// <c>Create</c> factory, <c>CurrentStage</c>, <c>EnsureUnique</c>, a navigation's
    /// <c>Create{Name}</c>/<c>Attach{Name}</c>/member/field, the automatic-transition guard,
    /// the <c>{Entity}Stage</c> enum) or the entity's own name (CS0542). On an enum it is a
    /// name C# reserves on every enum member list: <c>value__</c>, the backing field, which is
    /// CS0076 (S-02, #530). The enum's own name is not reserved — an enum member is a value,
    /// not a nested member, so <c>E: enum { E }</c> compiles. An action parameter named like a
    /// generated reference (<c>Poly</c>, <c>System</c>) shadows it inside the method body and is
    /// reported under this code too.
    /// </summary>
    public const string ReservedMemberName = "DMSTR009";

    /// <summary>
    /// An entity type is the target of two or more owner navigations (S-12, #585): an owned
    /// target has exactly one owner navigation. Two <c>owned</c> navigations to one type —
    /// on different entities or on one — leave the single-owner rule unlowerable. Keep one
    /// <c>owned</c> and make the other a plain reference. A self-owning navigation is one
    /// edge and is legal.
    /// </summary>
    public const string OwnedTargetMultipleOwners = "DMSTR010";

    public const string SemanticTypeCompatibility = "DMSEM003";
    public const string SemanticConstraintMismatch = "DMSEM004";
    public const string SemanticReferenceResolution = "DMSEM005";

    // #406 function calls (DMFUN): a declared function is callable from a domain expression.

    /// <summary>A call names a function the domain does not declare.</summary>
    public const string FunctionUnknown = "DMFUN001";
    /// <summary>A call passes a different number of arguments than the function declares.</summary>
    public const string FunctionArity = "DMFUN002";
    /// <summary>A call argument's type is not assignable to the declared parameter type.</summary>
    public const string FunctionArgumentType = "DMFUN003";
    /// <summary>
    /// A function reaches itself through calls (directly or through other functions). v1 rejects
    /// recursion (#406): a body is one expression with no base-case control flow, so every
    /// recursive function would diverge, and lowering would inline without end.
    /// </summary>
    public const string FunctionRecursion = "DMFUN004";
    /// <summary>
    /// A function body names something other than its parameters (an entity property, a
    /// relationship, a quantifier, a clock). Functions are pure: they have no subject entity.
    /// </summary>
    public const string FunctionImpureBody = "DMFUN005";
    /// <summary>A function body's type is not assignable to the declared return type.</summary>
    public const string FunctionReturnType = "DMFUN006";
    /// <summary>Two functions declare the same name.</summary>
    public const string FunctionDuplicate = "DMFUN007";
    /// <summary>A call sits where lowering cannot place it (a property default).</summary>
    public const string FunctionCallPosition = "DMFUN008";

    public const string EffectBinding = "DMEFF001";
    public const string EffectUnsatisfiedRequirement = "DMEFF002";
    public const string EffectPrePostCondition = "DMEFF003";
    public const string EffectUnusedParameter = "DMEFF004";

    public const string ConstraintSatisfiability = "DMCS001";
    public const string ConstraintFixedPoint = "DMCS002";
    public const string ConstraintUndecided = "DMCS003";

    public const string ContractIntegration = "DMCON001";

    /// <summary>
    /// A <see cref="DiagnosticAllowance"/> that acknowledged no analysis diagnostic this analysis
    /// emitted (wrong code or path, the finding stopped firing, or no non-blank provenance ids).
    /// An error, and not itself acknowledgeable.
    /// </summary>
    public const string UnusedDiagnosticAllowance = "DMALW001";

    /// <summary>
    /// A public member's exported signature names a type that is internal to the declaring
    /// domain. The exported contract must be closed (decision 12, #417): mark the named type
    /// <c>public</c> rather than adding an exception.
    /// </summary>
    public const string PublicSurfaceNotClosed = "DMCON002";

    /// <summary>
    /// A public action sits on an internal entity. A public entity exports its name and
    /// its public actions (#418, decision 13), so a public action must be reachable
    /// through public; the export never projects an action through an internal owner.
    /// </summary>
    public const string PublicMemberOnInternalOwner = "DMCON003";

    /// <summary>
    /// A <c>uses</c> id loaded a package that declares host targets, and the host target is
    /// absent or not among them (spike #413 decisions 3 and 7, #424). The target is a host fact:
    /// <c>uses posix</c> imports the package and never chooses the OS. The fix is to pass the
    /// host target (or load the package for it), not to change the <c>uses</c> line.
    /// </summary>
    public const string PlatformTargetMismatch = "DMCON004";

    /// <summary>
    /// A bind names an imported contract that is not registered in the domain (no <c>uses</c> or
    /// contract block seeded it). Split from <see cref="ContractIntegration"/> by #422 so the
    /// unresolved-reference case is acknowledgeable as a tolerance fact without covering the
    /// package-metadata, payload-type, and signature errors that share the old code.
    /// </summary>
    public const string ContractReferenceUnresolved = "DMCON005";

    // #420 takes 010-013 after #424's 004; #422 takes 005; 006-009 stay free.

    /// <summary>
    /// A <c>uses</c> pin does not match the loaded surface: the author pinned a version the
    /// package does not publish, or a fingerprint the current export does not compute.
    /// </summary>
    public const string ContractPinMismatch = "DMCON010";

    /// <summary>
    /// A bind named the original action and more than one exported endpoint accepted the
    /// parameter type; the unique most-constrained payload was chosen. Advisory: names the
    /// choice so a reviewer sees it.
    /// </summary>
    public const string ContractOverloadSelected = "DMCON011";

    /// <summary>
    /// A bind named the original action and several exported endpoints accepted the parameter
    /// type, but none was more constrained than the others. Naming one exported endpoint is
    /// the only sound fix.
    /// </summary>
    public const string ContractOverloadAmbiguous = "DMCON012";

    /// <summary>
    /// A bind names an endpoint the loaded contract does not have (or an original action no
    /// exported endpoint carries).
    /// </summary>
    public const string ContractEndpointNotOnSurface = "DMCON013";

    /// <summary>
    /// #425: the caller's argument is provably disjoint from a public payload constraint or a
    /// readable public precondition, or the contributed body provably writes a payload member
    /// outside a declared constraint or nulls a required one. An error: Lower refuses.
    /// </summary>
    public const string ContractBoundaryViolation = "DMCON014";

    /// <summary>
    /// #425: a boundary obligation the merge cannot decide, or one not readable from the
    /// argument, or a contributed-body write or payload escape the walk cannot decide (a surface-only
    /// contribution has no body and is silent). A warning: Lower is not
    /// blocked, and an allowance is not a way to clear it (#422 owns tolerance).
    /// </summary>
    public const string ContractBoundaryUnknown = "DMCON015";

    /// <summary>
    /// #426: an expression call that names an imported contract endpoint. Slice 1 only resolves
    /// and records it; nothing lowers it yet, so analysis rejects it in every position (shipped
    /// is a subset of lowerable). An error, reported once per call node.
    /// </summary>
    public const string ContractCallNotLowerable = "DMCON016";

    /// <summary>
    /// #602: two bind declarations reuse one name. Diagnostics and binding facts are keyed by
    /// bind name, so the duplicate makes them ambiguous and the printer emits both. An error,
    /// positioned on the second declaration, naming the first.
    /// </summary>
    public const string ContractBindDuplicateName = "DMCON017";

    /// <summary>
    /// #603: two binds target the same action. Lower splices one endpoint body and silently
    /// drops the other, so the contributed body that was bound never runs. An error,
    /// positioned on the second declaration, naming both binds.
    /// </summary>
    public const string ContractBindActionAlreadyBound = "DMCON018";

    /// <summary>
    /// #604 L-22: a public entity has a property typed as an entity that is internal to the declaring
    /// domain (reachable only through the model API; the DSL reads such a member as a navigation). Persistence
    /// ignores the property, so demoting it would lose data: mark the target <c>public</c>. A navigation to an
    /// internal entity is legal since L-22b (it prints <c>internal</c> and the pack maps it explicitly), except a
    /// many-to-many one (model API only), which fails closed under this code until row L-22d maps it.
    /// An error positioned on the property or the navigation, not acknowledgeable.
    /// </summary>
    public const string PublicEntityPropertyTypedInternalEntity = "DMCON019";

    /// <summary>
    /// #684 C-05: a constraint no loaded library compiles. <c>unique</c> is the one
    /// shipped record: its guard is the storage port, so a document that lists
    /// <c>uses</c> without <c>persistence</c> or a vendor library would have it
    /// silently dropped. Positioned on the constrained <see cref="Property"/> or
    /// <see cref="DomainType"/>; an error, and Lower refuses it.
    /// </summary>
    public const string ConstraintNoCompiler = "DMLIB001";

    // Stage-subscription diagnostics (replaced retired DMEV* event codes)
    public const string SubscriptionCausalityCycle = "DMSS001";
    public const string SubscriptionIdempotencyReplay = "DMSS002";
    public const string SubscriptionContractMismatch = "DMSS003";
    /// Subscription effect binding: subscriber props, peer binder path-prefix, legacy event ban.
    public const string SubscriptionEffectBinding = "DMSS004";
    /// <summary>
    /// #615: two <c>when</c>s in one scope (entity level, or one stage) share a semantic key
    /// (relationship, quantifier, peer binder, stage set). Nothing is removed: both handlers
    /// run, in declaration order. A warning, not an error (Q11).
    /// </summary>
    public const string SubscriptionDuplicateHandler = "DMSS006";
    /// <summary>
    /// #616: a <c>when</c> names a stage on the target entity that nothing transitions into and
    /// that is not the initial stage (Q21), so the handler can never fire. Syntactic, not
    /// reachability: no transition into the stage at all. Positioned on the <c>when</c>, not the
    /// stage declaration, so a stage nobody watches stays silent.
    /// </summary>
    public const string SubscriptionToNeverEnteredStage = "DMSS007";

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

    /// <summary>
    /// A navigation's <c>via</c> clause names something the target entity does not
    /// declare as a navigation. A property of that name does not pair: the clause
    /// names the navigation that points back.
    /// </summary>
    public const string InverseUnknown = "DMREL003";

    /// <summary>
    /// Two sides of a <c>via</c> pairing contradict each other: the named navigation
    /// points elsewhere, declares a different inverse, is the navigation itself, is
    /// owned on both ends, pairs an owned navigation with a collection inverse (S-12), or
    /// two navigations on one entity claim the same undeclared inverse.
    /// </summary>
    public const string InverseContradictory = "DMREL004";

    /// <summary>
    /// Two or more navigations from one entity to the same target (same cardinality
    /// class) with a navigation on the target pointing back, and no side declaring
    /// <c>via</c>. The pairing is undeclared and ambiguous — declare <c>via</c>.
    /// </summary>
    public const string InverseAmbiguous = "DMREL005";

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

    /// <summary>
    /// A copy whose source may carry null (an optional property or an optional parameter) writes a
    /// required target, and no provable guard clears it (#444). In the tree it sits on the assign;
    /// an <see cref="ObservedFlow"/> has no guard site, so it sits on the target property. An
    /// error that no <see cref="DiagnosticAllowance"/> acknowledges.
    /// </summary>
    public const string ObservedFlowNullability = "DMEFF014";

    /// <summary>
    /// An action assigns a value that may be null to a property a stage <c>require</c> proves present
    /// (#444). The stage invariant holds only if no action that can run in the stage can null it.
    /// </summary>
    public const string StagePresenceInvalidated = "DMEFF015";

    /// <summary>
    /// The general <c>for x in Rel [where cond] { … }</c> effect (#409) parses and prints but its
    /// effect legality (slice 5) and lowering (slice 6) are not shipped; Analyze rejects it so
    /// nothing unlowerable ships. An error.
    /// </summary>
    public const string EffectForNotLowerable = "DMEFF016";

    /// <summary>
    /// A transition in a stage's exit block targets a stage that can differ from the target of a hop
    /// that leaves the same stage. The hop assigns <c>CurrentStage</c> after the inlined exit block,
    /// so the exit-block target is overridden. The shipped shape is the same target on both. An error
    /// that no <see cref="DiagnosticAllowance"/> acknowledges.
    /// </summary>
    public const string ExitBlockTransitionTargetOverridden = "DMEFF017";

    /// <summary>
    /// A stage's exit block holds <c>invoke</c>, <c>create</c>, <c>create in</c> or
    /// <c>for … invoke</c>, which the guide's effect table makes action-only. The exit block inlines
    /// into every hop that leaves the stage, so a callee or a create could transition the instance
    /// behind <see cref="ExitBlockTransitionTargetOverridden"/>. An error that no
    /// <see cref="DiagnosticAllowance"/> acknowledges.
    /// </summary>
    public const string ExitBlockEffectNotSupported = "DMEFF018";

    /// <summary>
    /// A <c>create</c> or <c>create in</c> initializer supplies a property the new instance's entry
    /// chain may assign (conditionally, in a loop, through a hop or an invoked action). The entry
    /// chain overwrites the supplied value, so it can never reach the object. Omit the property
    /// (give it <c>default(...)</c> or mark it <c>optional</c> if it must be omittable). An error
    /// that no <see cref="DiagnosticAllowance"/> acknowledges.
    /// </summary>
    public const string CreateSuppliesEntryAssignedProperty = "DMEFF019";

    /// <summary>
    /// A <c>match</c> arm condition (#409) is not Boolean. An error.
    /// </summary>
    public const string MatchConditionNotBoolean = "DMMAT001";

    /// <summary>
    /// The value types of a <c>match</c> do not unify: an arm (or the <c>else</c>) has a type
    /// incompatible with the other arms. An error.
    /// </summary>
    public const string MatchArmTypeMismatch = "DMMAT002";

    /// <summary>
    /// Two arms of a <c>match</c> have the same condition, so the later arm is unreachable. An error.
    /// </summary>
    public const string MatchDuplicateCondition = "DMMAT003";

    /// <summary>
    /// A bare name in a <c>match</c> condition or value resolves to no property, parameter, enum
    /// member or library identifier. An error.
    /// </summary>
    public const string MatchUnresolvedName = "DMMAT004";

    /// <summary>
    /// A <see cref="DiagnosticAllowance"/> names a <see cref="ObservedFlowNullability"/> finding. That
    /// finding cannot be acknowledged; only a provable guard clears it. An error.
    /// </summary>
    public const string UnacknowledgeableAllowance = "DMALW002";

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