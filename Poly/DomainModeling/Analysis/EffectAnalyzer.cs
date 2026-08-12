using Poly.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Validate pack: effect binding, ordering, parameter usage, and requirement coverage diagnostics.
/// Writes no analysis facts — create-in resolution is published by <see cref="EffectFactsPass"/>.
/// </summary>
internal sealed class EffectAnalyzer : INodeAnalyzer {
    public const string Id = "DomainEffectAnalyzer";
    public string PassName => Id;
    // Lint-only: reads DTLM/ResolvedType (Semantic), catalog (DomainCatalogPass),
    // RequiredProperties (facts), DownstreamConstraints (ConstraintPropagation).
    // No metadata publication. Catalog dependency is declared (not accidental
    // pipeline order) so a catalog-less pipeline cannot silently soft-skip
    // domain-bound effect validation (review F1).
    public string[] Dependencies => [
        SemanticDomainAnalyzer.Id,
        DomainCatalogPass.Id,
        RequiredPropertiesPass.Id,
        ConstraintPropagationAnalyzer.Id,
    ];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        if (node is Domain domain) {
            ValidateDomainEffects(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomainEffects(AnalysisContext context, Domain domain) {
        var lookup = ResolveTypeLookup(context, domain);
        if (lookup is null) {
            // Fail closed (review F1): domain-bound validation without any type
            // lookup bag would silently omit every effect check. Report loudly.
            context.ReportStructuralFailure(
                domain,
                "Domain type lookup bag is unavailable; effect binding cannot be validated. " +
                "Run a successful SemanticDomainAnalyzer before EffectAnalyzer.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return;
        }

        DomainAnalysis.ForEachEntity(domain, entity => {
            foreach (var action in entity.Actions) {
                ValidateEffects(context, action.Effects, action, entity, domain, lookup);
                ValidateUnsatisfiedRequirements(context, action, entity, lookup);
                ValidateActionParameterUsage(context, action);
                ValidateActionReturnProducer(context, action, entity, domain, lookup);
                ValidateActionReturnFinalStatement(context, action, entity, domain, lookup);
                ValidateCallChainPostconditions(context, action, entity);
            }
            foreach (var stage in entity.Stages) {
                ValidateEffects(context, stage.OnEntryEffects, null, entity, domain, lookup);
                ValidateEffects(context, stage.OnExitEffects, null, entity, domain, lookup);
                foreach (var action in stage.Actions) {
                    ValidateEffects(context, action.Effects, action, entity, domain, lookup);
                    ValidateUnsatisfiedRequirements(context, action, entity, lookup);
                    ValidateActionParameterUsage(context, action);
                    ValidateActionReturnProducer(context, action, entity, domain, lookup);
                    ValidateActionReturnFinalStatement(context, action, entity, domain, lookup);
                }
            }
        });
    }

    /// <summary>
    /// P3: non-void <c>-&gt; T</c> requires a create / create-in that produces entity type T
    /// (product vertical — not primitive assign-as-return), and the FINAL statement
    /// must actually produce that value (DMEFF010) so the runtime/export return is
    /// deterministic — the exporter lowers the last statement's created instance as
    /// the return value.
    /// </summary>
    private static void ValidateActionReturnProducer(
        AnalysisContext context,
        Action action,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup) {
        if (action.Result.Members.Count == 0) return;

        var expectedType = action.Result.Members[0].Type.TypeName;
        if (lookup.Types.TryGetValue(expectedType, out var dt) && dt is not Entity) {
            context.ReportError(
                action,
                $"Action '{action.Name}' declares return type '{expectedType}', but only entity " +
                "returns produced by create / create-in are supported on the product path.",
                DomainModelDiagnosticCodes.EffectReturnWithoutProducer);
            return;
        }

        if (ActionEffectsProduceEntityType(context, domain, entity, action.Effects, expectedType, lookup))
            return;

        context.ReportError(
            action,
            $"Action '{action.Name}' declares return type '{expectedType}' but no create or " +
            $"create-in effect produces an instance of '{expectedType}'.",
            DomainModelDiagnosticCodes.EffectReturnWithoutProducer);
    }

    /// <summary>
    /// DMEFF010: when an action declares <c>-&gt; T</c>, the FINAL statement must
    /// produce a T — a <c>create</c> / <c>create in</c> of T, or a conditional whose
    /// every branch ends in such a producer (a conditional without a final
    /// <c>else</c> can produce nothing and is rejected — fail closed). This pins the
    /// contract the exporter relies on (return value = last statement's created
    /// instance) and rejects bodies like <c>create X; transition to Done</c> where
    /// the create is not the terminal statement.
    /// </summary>
    internal static bool ValidateActionReturnFinalStatement(
        AnalysisContext context,
        Action action,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup) {
        if (action.Result.Members.Count == 0) return true;

        var expectedType = action.Result.Members[0].Type.TypeName;
        if (lookup.Types.TryGetValue(expectedType, out var dt) && dt is not Entity)
            return true; // non-entity return already reported (DMEFF009)
        if (action.Effects.Count == 0)
            return true; // no-producer case already reported (DMEFF009)
        if (!ActionEffectsProduceEntityType(context, domain, entity, action.Effects, expectedType, lookup))
            return true; // no producer anywhere — DMEFF009 already reported; final-statement adds nothing

        if (LastStatementProducesEntityType(context, domain, entity, action.Effects, expectedType, lookup))
            return true;

        context.ReportError(
            action,
            $"Action '{action.Name}' declares return type '{expectedType}', but its final statement " +
            $"does not produce a '{expectedType}'. The create/create-in yielding the return value must " +
            "be the last statement (or every branch of a final conditional must produce it).",
            DomainModelDiagnosticCodes.EffectReturnNotProducedByFinalStatement);
        return false;
    }

    /// <summary>True when the last effect in <paramref name="effects"/> produces
    /// <paramref name="expectedType"/> (create / create-in / conditional with all
    /// branches ending in a producer).</summary>
    private static bool LastStatementProducesEntityType(
        AnalysisContext context,
        Domain domain,
        Entity sourceEntity,
        IReadOnlyList<Effect> effects,
        string expectedType,
        DomainTypeLookupMetadata lookup) {
        var last = effects[^1];
        switch (last) {
            case CreateEntityInstance cei:
                return string.Equals(cei.Type.TypeName, expectedType, StringComparison.Ordinal);

            case CreateEntityInRelationshipEffect createIn:
                return CreateInProducesEntityType(context, domain, sourceEntity, createIn, expectedType, lookup);

            case ConditionalEffect cond:
                // Every branch must terminate in a producer, and there must be a
                // final else — otherwise a false condition produces no value.
                return cond.ElseEffects is not null
                    && LastStatementProducesEntityType(context, domain, sourceEntity, cond.ThenEffects, expectedType, lookup)
                    && LastStatementProducesEntityType(context, domain, sourceEntity, cond.ElseEffects, expectedType, lookup);

            default:
                // transition / assign / invoke / delete / composite: not a producer.
                return false;
        }
    }

    /// <summary>True when the create-in's resolved target entity type is
    /// <paramref name="expectedType"/>.</summary>
    private static bool CreateInProducesEntityType(
        AnalysisContext context,
        Domain domain,
        Entity sourceEntity,
        CreateEntityInRelationshipEffect createIn,
        string expectedType,
        DomainTypeLookupMetadata lookup) {
        if (createIn.ResolvedTargetType is not null
            && string.Equals(createIn.ResolvedTargetType.TypeName, expectedType, StringComparison.Ordinal))
            return true;
        if (TryResolveRelationship(context, domain, sourceEntity.Name, createIn.RelationshipName, createIn, out var rel)
            && rel is not null
            && string.Equals(rel.Target.TypeName, expectedType, StringComparison.Ordinal)
            && string.Equals(rel.Source.TypeName, sourceEntity.Name, StringComparison.Ordinal))
            return true;
        return false;
    }

    private static bool ActionEffectsProduceEntityType(
        AnalysisContext context,
        Domain domain,
        Entity sourceEntity,
        IReadOnlyList<Effect> effects,
        string expectedType,
        DomainTypeLookupMetadata lookup) {
        foreach (var effect in FlattenEffects(effects)) {
            switch (effect) {
                case CreateEntityInstance cei
                    when string.Equals(cei.Type.TypeName, expectedType, StringComparison.Ordinal):
                    return true;
                case CreateEntityInRelationshipEffect createIn:
                    if (CreateInProducesEntityType(context, domain, sourceEntity, createIn, expectedType, lookup))
                        return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>
    /// Reports hints for declared action parameters that are never referenced by any effect expression.
    /// (Folded from ActionParameterUsageAnalyzer — D2.3)
    /// </summary>
    private static void ValidateActionParameterUsage(AnalysisContext context, Action action) {
        if (action.Parameters.Count == 0) return;

        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name),
            StringComparer.Ordinal);
        var usedParams = CollectParameterReferences(action.Effects, paramNames);
        foreach (var param in action.Parameters) {
            if (!usedParams.Contains(param.Name)) {
                context.ReportHint(
                    param,
                    $"Action parameter '{param.Name}' on '{action.Name}' is declared but never referenced by any effect expression.",
                    DomainModelDiagnosticCodes.EffectUnusedParameter);
            }
        }
    }

    private static HashSet<string> CollectParameterReferences(
        IReadOnlyList<Effect> effects,
        HashSet<string> paramNames) {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in effects)
            CollectFromEffect(effect, referenced, paramNames);
        return referenced;
    }

    private static void CollectFromEffect(Effect effect, HashSet<string> referenced, HashSet<string> paramNames) {
        switch (effect) {
            case ConditionalEffect ce:
                CollectFromExpression(ce.Condition, referenced, paramNames);
                foreach (var e in ce.ThenEffects) CollectFromEffect(e, referenced, paramNames);
                if (ce.ElseEffects is not null)
                    foreach (var e in ce.ElseEffects) CollectFromEffect(e, referenced, paramNames);
                break;
            case CompositeEffect ce:
                foreach (var e in ce.Effects) CollectFromEffect(e, referenced, paramNames);
                break;
            case AssignEffect ae:
                CollectFromExpression(ae.Target, referenced, paramNames);
                CollectFromExpression(ae.Value, referenced, paramNames);
                break;
            case CreateEntityInstance cei:
                foreach (var init in cei.Initializers)
                    CollectFromExpression(init.Expression, referenced, paramNames);
                break;
            case InvokeActionEffect iae:
                foreach (var binding in iae.ParameterBindings)
                    CollectFromExpression(binding.Expression, referenced, paramNames);
                break;
            case ForEachInvokeEffect efe:
                foreach (var binding in efe.ParameterBindings)
                    CollectFromExpression(binding.Expression, referenced, paramNames);
                break;
            case StageTransitionEffect:
            case CreateEntityInRelationshipEffect:
                break;
        }
    }

    private static void CollectFromExpression(DomainExpression expr, HashSet<string> referenced, HashSet<string> paramNames) {
        if (expr is ParameterAccess pa) {
            if (paramNames.Contains(pa.Name))
                referenced.Add(pa.Name);
            return;
        }
        // Recursively walk Children to find ParameterAccess nodes at any depth.
        // Avoids a brittle switch over every DomainExpression subtype.
        foreach (var child in expr.Children) {
            if (child is DomainExpression de)
                CollectFromExpression(de, referenced, paramNames);
        }
    }

    private static void ValidateEffects(
        AnalysisContext context,
        IReadOnlyList<Effect> effects,
        Action? action,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup) {
        // ── Per-effect validation ─────────────────────────────
        foreach (var effect in effects) {
            ValidateEffect(context, effect, action, entity, domain, lookup);
        }
    }

    private static void ValidateEffect(
        AnalysisContext context,
        Effect effect,
        Action? action,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup) =>
        new EffectValidationDispatch(context, action, entity, domain, lookup).Route(effect);

    /// <summary>
    /// Per-effect validation routed through <see cref="EffectDispatch{TResult}"/>
    /// (coh-e1). Methods are named by the effect type they validate; composite and
    /// conditional effects recurse via <see cref="EffectDispatch{TResult}.Route"/>.
    /// New effect subtypes fail loud in the base Route switch instead of silently
    /// passing through an analyzer switch.
    /// </summary>
    private sealed class EffectValidationDispatch(
        AnalysisContext context,
        Action? action,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup)
        : EffectDispatch<object?> {
        protected override object? Default() => null;

        protected override object? CreateEntityInstance(CreateEntityInstance e) {
            ValidateCreateEntityInstance(context, e, entity, lookup, domain);
            return null;
        }

        protected override object? CreateEntityInRelationship(CreateEntityInRelationshipEffect e) {
            ValidateCreateEntityInRelationship(context, e, entity, domain, lookup);
            return null;
        }

        protected override object? StageTransition(StageTransitionEffect e) {
            ValidateStageTransition(context, e, entity);
            return null;
        }

        protected override object? InvokeAction(InvokeActionEffect e) {
            ValidateInvokeAction(context, e, entity, domain);
            return null;
        }

        protected override object? ForEachInvoke(ForEachInvokeEffect e) {
            ValidateForEachInvoke(context, e, entity, domain);
            return null;
        }

        protected override object? Assign(AssignEffect e) {
            ValidateAssign(context, e, action, entity);
            return null;
        }

        protected override object? Conditional(ConditionalEffect e) {
            ValidateEffects(context, e.ThenEffects, action, entity, domain, lookup);
            if (e.ElseEffects is not null) {
                ValidateEffects(context, e.ElseEffects, action, entity, domain, lookup);
            }
            // DMEFF006: warn if the conditional contains direct-execution effects
            // that would be silently dropped by EffectLoweringPass
            WarnNestedDirectEffects(context, "ConditionalEffect (then)", e.ThenEffects);
            if (e.ElseEffects is not null)
                WarnNestedDirectEffects(context, "ConditionalEffect (else)", e.ElseEffects);
            return null;
        }

        protected override object? Composite(CompositeEffect e) {
            ValidateEffects(context, e.Effects, action, entity, domain, lookup);
            // DMEFF006: warn if composite contains direct-execution effects
            // that would be silently dropped by EffectLoweringPass
            WarnNestedDirectEffects(context, "CompositeEffect", e.Effects);
            return null;
        }
    }

    /// <summary>
    /// Reports a warning when a CompositeEffect or ConditionalEffect contains
    /// direct-execution effects that <see cref="EffectLoweringPass"/> will silently
    /// drop. Only VM-lowerable effects (Assign, sub-Composite, sub-Conditional)
    /// survive lowering; direct effects (transition, create, delete, link, invoke,
    /// etc.) are skipped without error.
    /// </summary>
    private static void WarnNestedDirectEffects(
        AnalysisContext context,
        string containerLabel,
        IReadOnlyList<Effect> effects) {
        foreach (var sub in effects) {
            if (IsDirectExecutionEffect(sub)) {
                context.ReportWarning(
                    sub,
                    $"{containerLabel} contains a '{sub.GetType().Name}' that will be silently dropped " +
                    "at runtime because it is a direct-execution effect and only VM-lowerable effects " +
                    "(Assign, nested Composite/Conditional) execute inside composite/conditional blocks. " +
                    "Move the direct effect outside the composite/conditional or use sequential effects.",
                    DomainModelDiagnosticCodes.NestedDirectEffectDropped);
            }
            // Recurse for nested Composite/Conditional (they have the same limitation)
            if (sub is CompositeEffect nested)
                WarnNestedDirectEffects(context, $"{containerLabel} > CompositeEffect", nested.Effects);
            if (sub is ConditionalEffect nestedCond) {
                WarnNestedDirectEffects(context, $"{containerLabel} > ConditionalEffect (then)", nestedCond.ThenEffects);
                if (nestedCond.ElseEffects is not null)
                    WarnNestedDirectEffects(context, $"{containerLabel} > ConditionalEffect (else)", nestedCond.ElseEffects);
            }
        }
    }

    private static bool IsDirectExecutionEffect(Effect effect) =>
        EffectHelpers.IsDirectExecutionEffect(effect);

    private static void ValidateCreateEntityInstance(
        AnalysisContext context, CreateEntityInstance cei, Entity actionEntity, DomainTypeLookupMetadata lookup, Domain domain) {
        if (!TryResolveDomainType(context, cei.Type, lookup, cei, out var resolvedType)) {
            return;
        }

        if (resolvedType is Entity targetEntity) {
            foreach (var initializer in cei.Initializers) {
                var targetProp = targetEntity.Properties
                    .FirstOrDefault(p => string.Equals(p.Name, initializer.PropertyName, StringComparison.Ordinal));

                // Initializers may also bind a singular navigation property of the
                // target entity (e.g. `create in loans { book: book }`) — the runtime
                // evaluates the binding into the child's value bag and the exporter
                // wires it as a Create(...) nav parameter. Rejecting it here would
                // contradict both (and the shipped demo).
                if (targetProp is null && !IsSingularTargetNavigation(domain, targetEntity.Name, initializer.PropertyName)) {
                    context.ReportError(
                        initializer,
                        $"CreateEntityInstance initializer references unknown property '{initializer.PropertyName}' on entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.EffectBinding);
                    continue;
                }

                if (targetProp is null) continue; // nav binding — no property constraints to check

                // Validate literal initializer values against property constraints
                if (initializer.Expression is Literal lit && targetProp.Constraints.Count > 0) {
                    ValidateLiteralAgainstConstraints(context, initializer, lit.Value, targetProp);
                }

                // Propagate the target's range onto derived (arithmetic) initializer values
                if (initializer.Expression is Add or Subtract or Multiply && targetProp.Constraints.Count > 0) {
                    ValidateDerivedValueRange(context, initializer, initializer.Expression, actionEntity, action: null, targetProp);
                }
            }

            // P2′.2: Reject bare create of exclusively-owned entity types
            // An entity is exclusively-owned if it is only ever the target of SourceOwnsTarget relationships
            // and no relationship has it as source.
            if (cei.RelationshipName is null && IsExclusivelyOwned(targetEntity, domain)) {
                context.ReportError(
                    cei,
                    $"Cannot directly create '{targetEntity.Name}': it is exclusively owned by other entities. " +
                    $"Use 'create in' on a relationship that has '{targetEntity.Name}' as the target.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }

            // D: every required property must be provided (auto-wire only in create-in).
            ValidateRequiredInitializerCoverage(context, cei, targetEntity,
                autoWireSourceEntity: null, cei.Initializers);

            // P2′.3 / P2′′.2: When RelationshipName is set, validate relationship exists,
            // that the action entity is the relationship source, and that the created
            // type matches the relationship target.
            if (cei.RelationshipName is not null) {
                ValidateCreateWithRelationshipName(context, cei, actionEntity, domain, lookup, targetEntity);
            }
        }
    }

    private static void ValidateCreateWithRelationshipName(
        AnalysisContext context, CreateEntityInstance cei, Entity actionEntity, Domain domain, DomainTypeLookupMetadata lookup, Entity targetEntity) {
        // amu-w1-1 / F1: catalog+RLM name resolve (no domain.Relationships scan);
        // bag-missing reports a structural failure (fail-closed) before returning.
        // cei.RelationshipName is non-null here (caller guards before invoking).
        if (!TryResolveRelationship(context, domain, actionEntity.Name, cei.RelationshipName!, cei, out var relationship)) {
            return;
        }
        if (relationship is null) {
            context.ReportError(
                cei,
                $"CreateEntityInstance references unknown relationship '{cei.RelationshipName}' in domain '{domain.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // Check the action's entity is the relationship source
        if (!string.Equals(relationship.Source.TypeName, actionEntity.Name, StringComparison.Ordinal)) {
            context.ReportError(
                cei,
                $"CreateEntityInstance uses relationship '{cei.RelationshipName}' whose source is " +
                $"'{relationship.Source.TypeName}', but the effect is on entity '{actionEntity.Name}'. " +
                $"Create with RelationshipName must be on the source entity of the relationship.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // Check the created type matches the relationship target
        if (!string.Equals(targetEntity.Name, relationship.Target.TypeName, StringComparison.Ordinal)) {
            context.ReportError(
                cei,
                $"CreateEntityInstance creates type '{targetEntity.Name}' but relationship " +
                $"'{cei.RelationshipName}' targets '{relationship.Target.TypeName}'. " +
                $"The created type must match the relationship target.",
                DomainModelDiagnosticCodes.EffectBinding);
        }
    }

    private static bool IsExclusivelyOwned(Entity entity, Domain domain) {
        var entityName = entity.Name;
        bool isSource = false;
        bool isOwnedTarget = false;

        foreach (var rel in domain.Relationships) {
            if (string.Equals(rel.Source.TypeName, entityName, StringComparison.Ordinal)) {
                isSource = true;
            }
            if (string.Equals(rel.Target.TypeName, entityName, StringComparison.Ordinal) && rel.SourceOwnsTarget) {
                isOwnedTarget = true;
            }
        }

        // Exclusively owned: exists only as a target of owned relationships, never as a source
        return isOwnedTarget && !isSource;
    }

    /// <summary>
    /// Catalog/RLM relationship name resolve (amu-w1-1; review F1). Replaces
    /// linear <c>domain.Relationships.FirstOrDefault</c> scans on domain-bound
    /// paths. Prefers the catalog relationship bag, falls back to the
    /// intermediate RLM bag (parity with <see cref="PolicyConstraintAnalyzer"/>),
    /// and <b>fails closed</b> with a structural diagnostic on
    /// <paramref name="reportNode"/> when neither bag is available — domain-bound
    /// validation never silently omits checks. Returns <c>true</c> with
    /// <paramref name="relationship"/> set to null when the name is genuinely
    /// not a relationship in the available bags.
    /// </summary>
    private static bool TryResolveRelationship(
        AnalysisContext context, Domain domain, string sourceEntityName, string relationshipName, Node reportNode, out Relationship? relationship) {
        var relLookup = ResolveRelationshipLookup(context, domain);
        if (relLookup is null) {
            relationship = null;
            ReportCatalogUnavailable(context, reportNode);
            return false;
        }
        relationship = relLookup.TryGetRelationship(sourceEntityName, relationshipName, out var rel) ? rel : null;
        return true;
    }

    /// <summary>
    /// Relationship lookup for domain-bound name resolve (amu-w1-1; review F1).
    /// Catalog relationship bag first; intermediate RLM bag from
    /// <see cref="SemanticDomainAnalyzer"/> as fallback (same contract as
    /// <see cref="PolicyConstraintAnalyzer"/>). Null only when neither bag exists.
    /// </summary>
    private static RelationshipLookupMetadata? ResolveRelationshipLookup(AnalysisContext context, Domain domain) =>
        context.GetRelationshipLookup(domain) ?? context.GetMetadata<RelationshipLookupMetadata>(default);

    /// <summary>
    /// Catalog/RLM entity type resolve (amu-w1-1; review F1). Replaces linear
    /// <c>domain.Types.OfType&lt;Entity&gt;().FirstOrDefault</c> scans on
    /// domain-bound paths. Same bag-availability + fail-closed contract as
    /// <see cref="TryResolveRelationship"/>.
    /// </summary>
    private static bool TryResolveEntity(
        AnalysisContext context, Domain domain, string typeName, Node reportNode, out Entity? entity) {
        var typeLookup = ResolveTypeLookup(context, domain);
        if (typeLookup is null) {
            entity = null;
            ReportCatalogUnavailable(context, reportNode);
            return false;
        }
        if (typeLookup.Types.TryGetValue(typeName, out var domainType) && domainType is Entity e) {
            entity = e;
            return true;
        }
        entity = null;
        return true;
    }

    /// <summary>
    /// Type lookup for domain-bound name resolve (amu-w1-1; review F1).
    /// Catalog type lookup first; intermediate DTLM from
    /// <see cref="SemanticDomainAnalyzer"/> as fallback. Null only when neither
    /// bag exists.
    /// </summary>
    private static DomainTypeLookupMetadata? ResolveTypeLookup(AnalysisContext context, Domain domain) =>
        context.GetTypeLookup(domain) ?? context.GetMetadata<DomainTypeLookupMetadata>(default);

    /// <summary>
    /// Fail-closed: domain-bound validation cannot proceed without the semantic
    /// lookup bags. Reports a structural failure instead of silently skipping
    /// the check (review F1 — bag-skip is not fail-closed).
    /// </summary>
    private static void ReportCatalogUnavailable(AnalysisContext context, Node node) =>
        context.ReportStructuralFailure(
            node,
            "Domain relationship/type lookup bags are unavailable; effect binding cannot be validated. " +
            "Run DomainCatalogPass over a successful SemanticDomainAnalyzer result before EffectAnalyzer.",
            DomainModelDiagnosticCodes.SemanticReferenceResolution);

    private static void ValidateCreateEntityInRelationship(
        AnalysisContext context, CreateEntityInRelationshipEffect createIn, Entity entity, Domain domain, DomainTypeLookupMetadata lookup) {
        // amu-w1-1 / F1: catalog+RLM name resolve (no domain.Relationships scan);
        // bag-missing reports a structural failure (fail-closed) before returning.
        if (!TryResolveRelationship(context, domain, entity.Name, createIn.RelationshipName, createIn, out var relationship)) {
            return;
        }
        if (relationship is null) {
            context.ReportError(
                createIn,
                $"CreateIn effect references unknown relationship '{createIn.RelationshipName}' in domain '{domain.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // Validate source entity matches the entity owning the action
        if (!string.Equals(relationship.Source.TypeName, entity.Name, StringComparison.Ordinal)) {
            context.ReportError(
                createIn,
                $"CreateIn effect uses relationship '{createIn.RelationshipName}' whose source is " +
                $"'{relationship.Source.TypeName}', but the effect is on entity '{entity.Name}'. " +
                $"CreateIn must be on the source entity of the relationship.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // Validate target entity exists
        if (!lookup.Types.TryGetValue(relationship.Target.TypeName, out var targetType) || targetType is not Entity targetEntity) {
            context.ReportError(
                createIn,
                $"CreateIn effect targets entity '{relationship.Target.TypeName}' via relationship " +
                $"'{createIn.RelationshipName}', but that entity does not exist.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // ResolvedRelationshipTargetMetadata is published by EffectFactsPass (fact emitter).

        // Validate initializer property names against target entity
        foreach (var initializer in createIn.Initializers) {
            var targetProp = targetEntity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, initializer.PropertyName, StringComparison.Ordinal));

            // Singular nav bindings are legal (see ValidateCreateEntityInstance) —
            // the demo's `create in loans { book: book }` relies on this.
            if (targetProp is null && !IsSingularTargetNavigation(domain, targetEntity.Name, initializer.PropertyName)) {
                context.ReportError(
                    initializer,
                    $"CreateIn initializer references unknown property '{initializer.PropertyName}' on entity '{targetEntity.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
                continue;
            }

            if (targetProp is null) continue; // nav binding — no property constraints to check

            // Validate literal initializer values against property constraints
            if (initializer.Expression is Literal lit && targetProp.Constraints.Count > 0) {
                ValidateLiteralAgainstConstraints(context, initializer, lit.Value, targetProp);
            }

            // Propagate the target's range onto derived (arithmetic) initializer values
            if (initializer.Expression is Add or Subtract or Multiply && targetProp.Constraints.Count > 0) {
                ValidateDerivedValueRange(context, initializer, initializer.Expression, entity, action: null, targetProp);
            }
        }

        // D: every required property must be provided — the back-reference nav
        // (typed as the source entity) is auto-wired and may be omitted.
        ValidateRequiredInitializerCoverage(context, createIn, targetEntity,
            autoWireSourceEntity: entity, createIn.Initializers);
    }

    /// <summary>
    /// True when <paramref name="name"/> is a SINGULAR navigation property declared
    /// on <paramref name="targetEntityName"/> (a one-to-one relationship sourced from
    /// that entity). Collection navs are not bindable initializer targets — the
    /// exporter emits empty collections for those.
    /// </summary>
    private static bool IsSingularTargetNavigation(Domain domain, string targetEntityName, string name) =>
        domain.Relationships.Any(r =>
            string.Equals(r.Source.TypeName, targetEntityName, StringComparison.Ordinal)
            && string.Equals(r.Name, name, StringComparison.Ordinal)
            && r.Cardinality == RelationshipCardinality.OneToOne);

    /// <summary>
    /// DMEFF011: a <c>create</c> / <c>create in</c> must provide a value for every
    /// <c>required</c> property of the created entity, unless the property has a
    /// <c>default</c> or is the auto-wired back-reference navigation (the target's
    /// nav typed as the create-in source entity). Without this, the generated
    /// <c>Create</c> factory would fail at runtime on a missing required value —
    /// analysis catches the bad authoring shape before the export/runtime path.
    /// </summary>
    private static void ValidateRequiredInitializerCoverage(
        AnalysisContext context,
        Effect effect,
        Entity targetEntity,
        Entity? autoWireSourceEntity,
        IReadOnlyList<PropertyBinding> initializers) {
        var provided = new HashSet<string>(
            initializers.Select(i => i.PropertyName), StringComparer.Ordinal);
        foreach (var prop in targetEntity.Properties) {
            if (!prop.Constraints.Any(static c => c is RequiredConstraint)) continue;
            if (prop.Constraints.Any(static c => c is DefaultValueConstraint)) continue;
            if (autoWireSourceEntity is not null
                && string.Equals(prop.Type.TypeName, autoWireSourceEntity.Name, StringComparison.Ordinal))
                continue; // auto-wired back-reference
            if (provided.Contains(prop.Name)) continue;

            context.ReportError(
                effect,
                $"Create effect on '{targetEntity.Name}' does not provide required property '{prop.Name}'. " +
                $"Every 'required' property must be set in the create / create in initializers (or have a default).",
                DomainModelDiagnosticCodes.CreateMissingRequiredProperty);
        }
    }

    private static void ValidateStageTransition(
        AnalysisContext context, StageTransitionEffect ste, Entity entity) {
        if (!entity.Stages.Any(s => string.Equals(s.Name, ste.TargetStage.StageName, StringComparison.Ordinal))) {
            context.ReportError(
                ste,
                $"StageTransition effect targets stage '{ste.TargetStage.StageName}' which does not exist on entity '{entity.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
        }
    }

    private static void ValidateForEachInvoke(
        AnalysisContext context, ForEachInvokeEffect efe, Entity entity, Domain domain) {
        // `for` requires a relationship, source-side, OneToMany (iterating a known singular
        // makes no sense), non-self.
        if (!TryResolveRelationship(context, domain, entity.Name, efe.RelationshipName, efe, out var relationship))
            return;
        if (relationship is null) {
            context.ReportError(
                efe,
                $"ForEachInvoke references relationship '{efe.RelationshipName}' which does not exist on domain.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }
        if (!string.Equals(relationship.Source.TypeName, entity.Name, StringComparison.Ordinal)) {
            context.ReportError(
                efe,
                $"ForEachInvoke relationship '{efe.RelationshipName}' may only be used from source entity " +
                $"'{relationship.Source.TypeName}' (caller is '{entity.Name}').",
                DomainModelDiagnosticCodes.EffectInvokeShape);
            return;
        }
        if (relationship.Cardinality is not RelationshipCardinality.OneToMany) {
            context.ReportError(
                efe,
                $"ForEachInvoke requires OneToMany relationship '{efe.RelationshipName}', " +
                $"got {relationship.Cardinality}. Iterating a singular relationship is not supported.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
            return;
        }
        if (string.Equals(relationship.Source.TypeName, relationship.Target.TypeName, StringComparison.Ordinal)) {
            context.ReportError(
                efe,
                $"ForEachInvoke on self-relationship '{efe.RelationshipName}' is not supported.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
            return;
        }

        if (!TryResolveEntity(context, domain, relationship.Target.TypeName, efe, out var targetEntity))
            return;
        if (targetEntity is null) {
            context.ReportError(
                efe,
                $"Target entity type '{relationship.Target.TypeName}' for relationship '{efe.RelationshipName}' not found.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // Binder must not collide with the caller's own members (property/stage/policy/action).
        if (entity.Properties.Any(p => string.Equals(p.Name, efe.BinderName, StringComparison.Ordinal))
            || entity.Stages.Any(s => string.Equals(s.Name, efe.BinderName, StringComparison.Ordinal))
            || entity.Policies.Any(p => string.Equals(p.Name, efe.BinderName, StringComparison.Ordinal))
            || entity.Actions.Any(a => string.Equals(a.Name, efe.BinderName, StringComparison.Ordinal))) {
            context.ReportError(
                efe,
                $"ForEachInvoke binder '{efe.BinderName}' collides with a member on entity '{entity.Name}'.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
        }

        // Predicate: named policy or stage membership on the TARGET entity.
        switch (efe.Predicate) {
            case ForEachNamedPolicy { PolicyName: var policyName }:
                if (!targetEntity.Policies.Any(p => string.Equals(p.Name, policyName, StringComparison.Ordinal))) {
                    context.ReportError(
                        efe,
                        $"ForEachInvoke predicate policy '{policyName}' does not exist on entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.EffectBinding);
                }
                break;
            case ForEachStageMembership { StageName: var stageName }:
                if (!targetEntity.Stages.Any(s => string.Equals(s.Name, stageName, StringComparison.Ordinal))) {
                    context.ReportError(
                        efe,
                        $"ForEachInvoke predicate stage '{stageName}' does not exist on entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.EffectBinding);
                }
                break;
        }

        // Action must exist on the target (entity or stage actions).
        var targetAction = targetEntity.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, efe.ActionName, StringComparison.Ordinal))
            ?? targetEntity.Stages.SelectMany(s => s.Actions)
                .FirstOrDefault(a => string.Equals(a.Name, efe.ActionName, StringComparison.Ordinal));
        if (targetAction is null) {
            context.ReportError(
                efe,
                $"ForEachInvoke references action '{efe.ActionName}' which does not exist on entity '{targetEntity.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        foreach (var binding in efe.ParameterBindings) {
            if (!targetAction.Parameters.Any(p => string.Equals(p.Name, binding.PropertyName, StringComparison.Ordinal))) {
                context.ReportError(
                    binding,
                    $"ForEachInvoke binding references unknown parameter '{binding.PropertyName}' on action '{targetAction.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
            // Arg expressions may reference the binder root (line Qty); anything else that
            // looks like a relationship-navigation root is rejected (binder is the only root).
            foreach (var nav in EnumerateRelationshipNavigations(binding.Expression)) {
                if (!string.Equals(nav.RelationshipName, efe.BinderName, StringComparison.Ordinal)) {
                    context.ReportError(
                        binding,
                        $"ForEachInvoke argument path-prefix root '{nav.RelationshipName}' is not the binder " +
                        $"'{efe.BinderName}'. Only the binder may be dereferenced.",
                        DomainModelDiagnosticCodes.EffectInvokeShape);
                }
            }
        }
    }

    private static IEnumerable<RelationshipNavigation> EnumerateRelationshipNavigations(DomainExpression expr) {
        if (expr is RelationshipNavigation rn)
            yield return rn;
        foreach (var child in expr.Children.OfType<DomainExpression>())
            foreach (var nested in EnumerateRelationshipNavigations(child))
                yield return nested;
    }

    private static void ValidateInvokeAction(
        AnalysisContext context, InvokeActionEffect iae, Entity entity, Domain domain) {
        // Fail-closed policy: reject ambiguous/unanalyzed shapes now; relax only when
        // analyzers can prove the edge case is safe (see guide + effect-surface plan).
        var hasRel = iae.TargetRelationship is not null;
        var hasFilter = iae.Filter is not null;
        var quantifier = iae.Quantifier;
        var hasCollectionQuantifier = quantifier is StageSubscriptionQuantifier.Any
            or StageSubscriptionQuantifier.All;

        // ── Local shape gates (no domain resolution required) ──
        if (quantifier is not null && !hasCollectionQuantifier) {
            context.ReportError(
                iae,
                quantifier is StageSubscriptionQuantifier.Each
                    ? "InvokeAction does not support quantifier 'Each'. Use 'any' or 'all' for collection invoke, " +
                      "or omit the quantifier for singular/self invoke."
                    : $"InvokeAction has unsupported quantifier '{quantifier}'.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
        }

        if (hasCollectionQuantifier && !hasRel) {
            context.ReportError(
                iae,
                $"InvokeAction quantifier '{quantifier}' requires a relationship target " +
                $"(e.g. 'invoke {quantifier!.Value.ToString().ToLowerInvariant()} RelName.{iae.ActionName}'). " +
                "Self-invoke cannot use any/all.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
        }

        if (hasFilter && !hasRel) {
            context.ReportError(
                iae,
                "InvokeAction 'where' filter requires a relationship target on a collection relationship " +
                "(e.g. 'invoke any RelName.Action where …'). Self-invoke cannot use where.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
        }

        if (hasFilter && !hasCollectionQuantifier) {
            context.ReportError(
                iae,
                "InvokeAction 'where' filter requires a collection quantifier ('any' or 'all'). " +
                "Singular cross-entity invoke cannot filter.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
        }

        // ── Resolve target entity (self or relationship target; source-side only) ──
        Entity targetEntity;

        if (hasRel) {
            // amu-w1-1 / F1: catalog+RLM name resolve (no domain.Relationships scan);
            // bag-missing reports a structural failure (fail-closed) before returning.
            if (!TryResolveRelationship(context, domain, entity.Name, iae.TargetRelationship!, iae, out var relationship)) {
                return;
            }
            if (relationship is null) {
                // Reverse-side detection: the name exists on a different source entity,
                // so this is a wrong-direction invoke, not an unknown relationship.
                var relLookup = ResolveRelationshipLookup(context, domain);
                if (relLookup is not null) {
                    var elsewhere = relLookup.FindByNameAcrossSources(iae.TargetRelationship!).FirstOrDefault();
                    if (elsewhere is not null) {
                        context.ReportError(
                            iae,
                            $"InvokeAction relationship '{iae.TargetRelationship}' may only be used from source entity " +
                            $"'{elsewhere.Source.TypeName}' (caller is '{entity.Name}'). " +
                            "Reverse-side cross-entity invoke is not supported yet.",
                            DomainModelDiagnosticCodes.EffectInvokeShape);
                        return;
                    }
                }
                context.ReportError(
                    iae,
                    $"InvokeAction effect references relationship '{iae.TargetRelationship}' which does not exist on domain.",
                    DomainModelDiagnosticCodes.EffectBinding);
                return;
            }

            // Strict: only the relationship source may cross-invoke via RelName.
            // Reverse-side navigate is rejected until analysis can own that edge case.
            if (!string.Equals(relationship.Source.TypeName, entity.Name, StringComparison.Ordinal)) {
                context.ReportError(
                    iae,
                    $"InvokeAction relationship '{iae.TargetRelationship}' may only be used from source entity " +
                    $"'{relationship.Source.TypeName}' (caller is '{entity.Name}'). " +
                    "Reverse-side cross-entity invoke is not supported yet.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;
            }

            // Strict: only OneToOne (singular) and OneToMany (any/all) from source.
            // ManyToOne / ManyToMany rejected until analyzable.
            if (relationship.Cardinality is not (RelationshipCardinality.OneToOne or RelationshipCardinality.OneToMany)) {
                context.ReportError(
                    iae,
                    $"InvokeAction on relationship '{iae.TargetRelationship}' with cardinality " +
                    $"{relationship.Cardinality} is not supported yet. " +
                    "Use OneToOne (bare Rel.Action) or OneToMany (any/all) from the source entity.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;
            }

            // Strict: self-relationships (same type both ends) rejected until proven safe.
            if (string.Equals(relationship.Source.TypeName, relationship.Target.TypeName, StringComparison.Ordinal)) {
                context.ReportError(
                    iae,
                    $"InvokeAction on self-relationship '{iae.TargetRelationship}' " +
                    $"(source and target both '{relationship.Source.TypeName}') is not supported yet.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;
            }

            var isCollectionFromSource = relationship.Cardinality is RelationshipCardinality.OneToMany;

            var targetTypeName = relationship.Target.TypeName;
            // amu-w1-1 / F1: catalog+RLM entity resolve (no domain.Types.OfType scan);
            // bag-missing reports a structural failure (fail-closed) before returning.
            if (!TryResolveEntity(context, domain, targetTypeName, iae, out var resolvedTarget))
                return;
            if (resolvedTarget is null) {
                context.ReportError(
                    iae,
                    $"Target entity type '{targetTypeName}' for relationship '{iae.TargetRelationship}' not found.",
                    DomainModelDiagnosticCodes.EffectBinding);
                return;
            }
            targetEntity = resolvedTarget;

            if (hasCollectionQuantifier && !isCollectionFromSource) {
                context.ReportError(
                    iae,
                    $"InvokeAction quantifier '{quantifier}' requires OneToMany from source " +
                    $"'{entity.Name}', but '{iae.TargetRelationship}' is {relationship.Cardinality}. " +
                    "Omit any/all for singular cross-entity invoke.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
            }

            if (!hasCollectionQuantifier && isCollectionFromSource) {
                context.ReportError(
                    iae,
                    $"InvokeAction on OneToMany relationship '{iae.TargetRelationship}' from '{entity.Name}' " +
                    "requires a quantifier ('any' or 'all'). Bare 'invoke Rel.Action' is only valid for OneToOne.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
            }

            if (hasFilter && !isCollectionFromSource) {
                context.ReportError(
                    iae,
                    $"InvokeAction 'where' filter requires OneToMany from '{entity.Name}', " +
                    $"but '{iae.TargetRelationship}' is {relationship.Cardinality}.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
            }
        }
        else {
            targetEntity = entity;
        }

        var targetAction = targetEntity.Actions.FirstOrDefault(a =>
            string.Equals(a.Name, iae.ActionName, StringComparison.Ordinal));
        // Stage actions are invokable targets too (self-invoke on a stage action,
        // or cross-entity invoke of a stage action by name).
        if (targetAction is null) {
            targetAction = targetEntity.Stages
                .SelectMany(s => s.Actions)
                .FirstOrDefault(a => string.Equals(a.Name, iae.ActionName, StringComparison.Ordinal));
        }
        if (targetAction is null) {
            context.ReportError(
                iae,
                $"InvokeAction effect references action '{iae.ActionName}' which does not exist " +
                $"on entity '{targetEntity.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // Unknown bindings
        foreach (var binding in iae.ParameterBindings) {
            if (!targetAction.Parameters.Any(p => string.Equals(p.Name, binding.PropertyName, StringComparison.Ordinal))) {
                context.ReportError(
                    binding,
                    $"InvokeAction effect binding references unknown parameter '{binding.PropertyName}' on action '{targetAction.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }

        // Strict: every declared parameter must be bound (no implicit defaults yet).
        foreach (var param in targetAction.Parameters) {
            if (!iae.ParameterBindings.Any(b =>
                string.Equals(b.PropertyName, param.Name, StringComparison.Ordinal))) {
                context.ReportError(
                    iae,
                    $"InvokeAction '{iae.ActionName}' is missing required parameter binding '{param.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }

        // Duplicate bindings
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in iae.ParameterBindings) {
            if (!seen.Add(binding.PropertyName)) {
                context.ReportError(
                    binding,
                    $"InvokeAction effect has duplicate parameter binding '{binding.PropertyName}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }

        // Filter: target-local predicate only (restricted expression surface).
        if (iae.Filter is not null) {
            ValidateInvokeFilterExpression(context, iae.Filter, targetEntity);
        }
    }

    /// <summary>
    /// Fail-closed filter surface: local target properties, literals, comparisons,
    /// boolean compose, and arithmetic only. Reject path-prefix / params / owned / exists / dates
    /// until those cases are analyzable end-to-end.
    /// </summary>
    private static void ValidateInvokeFilterExpression(
        AnalysisContext context, DomainExpression expr, Entity targetEntity) {
        switch (expr) {
            case PropertyAccess pa:
                if (!targetEntity.Properties.Any(p =>
                    string.Equals(p.Name, pa.Name, StringComparison.Ordinal))) {
                    context.ReportError(
                        expr,
                        $"InvokeAction filter references property '{pa.Name}' which does not exist on target entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.EffectBinding);
                }
                return;

            case Literal:
                return;

            case Comparison:
            case And:
            case Or:
            case Not:
            case Add:
            case Subtract:
            case Multiply:
            case Divide:
                foreach (var child in expr.Children.OfType<DomainExpression>())
                    ValidateInvokeFilterExpression(context, child, targetEntity);
                return;

            case ParameterAccess pa:
                context.ReportError(
                    expr,
                    $"InvokeAction filter cannot reference action parameter '{pa.Name}'. " +
                    "Filters are target-scoped only (no caller args).",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;

            case RelationshipNavigation rn:
                context.ReportError(
                    expr,
                    $"InvokeAction filter cannot navigate relationship '{rn.RelationshipName}'. " +
                    "Only local target properties are allowed until related-filter analysis ships.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;

            case OwnedAccess oa:
                context.ReportError(
                    expr,
                    $"InvokeAction filter cannot use owned access '{oa.OwnedName}' yet.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;

            case Exists:
            case NotExists:
                context.ReportError(
                    expr,
                    "InvokeAction filter cannot use exists/not-exists yet.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;

            case DateOperation:
                context.ReportError(
                    expr,
                    "InvokeAction filter cannot use date operations yet.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;

            default:
                context.ReportError(
                    expr,
                    $"InvokeAction filter expression '{expr.GetType().Name}' is not supported yet.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
                return;
        }
    }

    private static void ValidateAssign(
        AnalysisContext context, AssignEffect ae, Action? action, Entity entity) {
        if (ae.Target is not PropertyAccess propAccess) return;

        var targetProp = entity.Properties
            .FirstOrDefault(p => string.Equals(p.Name, propAccess.Name, StringComparison.Ordinal));

        if (targetProp is null) {
            context.ReportError(
                ae,
                $"Assign effect targets property '{propAccess.Name}' which does not exist on entity '{entity.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        if (targetProp.Constraints.Count == 0) return;

        // ── Validate literal assignments against property constraints ──────
        if (ae.Value is Literal lit) {
            ValidateLiteralAgainstConstraints(context, ae, lit.Value, targetProp);
            return;
        }

        // ── Propagate the target's range onto derived (arithmetic) values ────
        if (ae.Value is Add or Subtract or Multiply) {
            ValidateDerivedValueRange(context, ae, ae.Value, entity, action, targetProp);
        }

        // ── Validate parameter-to-property constraint compatibility ─────────
        if (ae.Value is ParameterAccess pa && action is not null) {
            var sourceParam = action.Parameters
                .FirstOrDefault(p => string.Equals(p.Name, pa.Name, StringComparison.Ordinal));
            if (sourceParam is not null) {
                ValidateParameterConstraintCompatibility(context, ae, sourceParam, targetProp);

                // Also check DownstreamConstraintsMetadata if ConstraintPropagationAnalyzer has run
                var downstreamMeta = context.GetMetadata<DownstreamConstraintsMetadata>(sourceParam);
                if (downstreamMeta is not null) {
                    ValidateDownstreamConstraints(context, ae, downstreamMeta.Constraints, targetProp, sourceParam);
                }
            }
        }
    }

    private static void ValidateLiteralAgainstConstraints(
        AnalysisContext context, Node errorNode, object? literalValue, Property targetProp) {
        foreach (var constraint in targetProp.Constraints) {
            if (!ConstraintValidation.IsSatisfiedBy(constraint, literalValue)) {
                var display = FormatValueForMessage(literalValue);
                context.ReportError(
                    errorNode,
                    $"Assigned value '{display}' violates constraint {ConstraintValidation.Describe(constraint)} " +
                    $"on property '{targetProp.Name}'.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
            }
        }
    }

    /// <summary>
    /// Constraint propagation onto derived (arithmetic) values: infer the value range the
    /// RHS expression can produce (from literals, property/param ranges, and arithmetic
    /// composition) and check it against the target property's RangeConstraint. A range
    /// entirely outside the constraint is a definite violation (error); a range that can
    /// extend outside is a possible violation (warning).
    /// </summary>
    private static void ValidateDerivedValueRange(
        AnalysisContext context,
        Node errorNode,
        DomainExpression value,
        Entity entity,
        Action? action,
        Property targetProp) {
        var range = targetProp.Constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (range is null) return;

        var meta = action is null ? null : context.GetMetadata<ActionInvariantMetadata>(action);
        if (meta is null) {
            // No action context (create-initializer): infer against declared ranges only.
            var (lo, hi) = EffectInvariantAnalyzer.InferNumericRange(value, entity, null, null);
            CheckDerivedRange(context, errorNode, lo, hi, range, targetProp);
            return;
        }

        // The effect is valid in one or more stage contexts (entity states); check the
        // postcondition in each — a violation in ANY state the action can run is reported.
        foreach (var ctx in meta.StageContexts) {
            var post = ctx.Postconditions.FirstOrDefault(p => ReferenceEquals(p.Effect, errorNode));
            if (post?.ValueRange is { } vr)
                CheckDerivedRange(context, errorNode, vr.Min, vr.Max, range, targetProp);
        }
    }

    private static void CheckDerivedRange(
        AnalysisContext context,
        Node errorNode,
        double? loNullable,
        double? hiNullable,
        RangeConstraint range,
        Property targetProp) {
        if (loNullable is null || hiNullable is null) return;
        double lo = loNullable.Value, hi = hiNullable.Value;

        var tmin = ToDouble(range.Minimum);
        var tmax = ToDouble(range.Maximum);

        bool fullyBelow = tmin is not null && hi < tmin.Value;
        bool fullyAbove = tmax is not null && lo > tmax.Value;
        if (fullyBelow || fullyAbove) {
            context.ReportError(
                errorNode,
                $"Assigned expression value range [{FormatRangeValue(lo)}, {FormatRangeValue(hi)}] is entirely outside " +
                $"constraint {ConstraintValidation.Describe(range)} on property '{targetProp.Name}'.",
                DomainModelDiagnosticCodes.EffectConstraintViolation);
            return;
        }

        bool canViolate = (tmin is not null && lo < tmin.Value)
                          || (tmax is not null && hi > tmax.Value);
        if (canViolate) {
            context.ReportWarning(
                errorNode,
                $"Assigned expression value range [{FormatRangeValue(lo)}, {FormatRangeValue(hi)}] can fall outside " +
                $"constraint {ConstraintValidation.Describe(range)} on property '{targetProp.Name}'.",
                DomainModelDiagnosticCodes.EffectConstraintViolation);
        }
    }

    /// <summary>
    /// Validates the call-chain postconditions: effects of actions this action invokes
    /// (transitively), whose value ranges were computed under the caller's narrowed context.
    /// A callee assignment that can violate its target's constraint while running under this
    /// caller is reported on the callee effect with the caller chain named.
    /// </summary>
    private static void ValidateCallChainPostconditions(
        AnalysisContext context, Action action, Entity entity) {
        var meta = context.GetMetadata<ActionInvariantMetadata>(action);
        if (meta is null) return;

        foreach (var stageCtx in meta.StageContexts()) {
            foreach (var post in stageCtx.Postconditions) {
                if (post.DeclaringAction == action) continue; // direct effects — validated per-effect
                if (post.ValueRange is not { } vr) continue;
                // The postcondition's Constraints are the target's net constraint (declared +
                // param/binder merges), valid for cross-entity callees whose target property
                // lives on a different entity than the caller.
                var range = post.Constraints.OfType<RangeConstraint>().FirstOrDefault();
                if (range is null) continue;

                var tmin = ToDouble(range.Minimum);
                var tmax = ToDouble(range.Maximum);
                double lo = vr.Min!.Value, hi = vr.Max!.Value;
                bool fullyBelow = tmin is not null && hi < tmin.Value;
                bool fullyAbove = tmax is not null && lo > tmax.Value;
                if (fullyBelow || fullyAbove) {
                    context.ReportError(
                        post.Effect,
                        $"Call-chain postcondition ({action.Name} → {post.DeclaringAction.Name}): assigned value range " +
                        $"[{FormatRangeValue(lo)}, {FormatRangeValue(hi)}] is entirely outside constraint " +
                        $"{ConstraintValidation.Describe(range)} on property '{post.TargetProperty}'.",
                        DomainModelDiagnosticCodes.EffectConstraintViolation);
                    continue;
                }
                bool canViolate = (tmin is not null && lo < tmin.Value)
                                  || (tmax is not null && hi > tmax.Value);
                if (canViolate) {
                    context.ReportWarning(
                        post.Effect,
                        $"Call-chain postcondition ({action.Name} → {post.DeclaringAction.Name}): assigned value range " +
                        $"[{FormatRangeValue(lo)}, {FormatRangeValue(hi)}] can fall outside constraint " +
                        $"{ConstraintValidation.Describe(range)} on property '{post.TargetProperty}'.",
                        DomainModelDiagnosticCodes.EffectConstraintViolation);
                }
            }
        }
    }

    private static string FormatRangeValue(double d) =>
        d == Math.Floor(d) ? d.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    private static void ValidateParameterConstraintCompatibility(
        AnalysisContext context, Node errorNode, Property sourceParam, Property targetProp) {
        // Check that the parameter's constraints don't allow values the property prohibits.
        // For each property constraint, check if any value allowed by the parameter would violate it.
        // We use a simple subsumption check: the parameter's range/length must be within the property's.
        foreach (var propConstraint in targetProp.Constraints) {
            // Find the matching constraint on the parameter (same type)
            var paramConstraint = sourceParam.Constraints
                .FirstOrDefault(c => c.GetType() == propConstraint.GetType());
            if (paramConstraint is null) {
                // Parameter has no constraint of this type — parameter could carry any value,
                // including ones that violate the property constraint.
                context.ReportWarning(
                    errorNode,
                    $"Parameter '{sourceParam.Name}' has no {ConstraintValidation.Describe(propConstraint)} constraint, " +
                    $"but flows to property '{targetProp.Name}' which requires " +
                    $"{ConstraintValidation.Describe(propConstraint)}. The parameter may carry values " +
                    $"that violate the property constraint.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                continue;
            }

            // Check parameter constraint does not exceed property constraint bounds
            if (!IsConstraintSubsumed(paramConstraint, propConstraint)) {
                context.ReportWarning(
                    errorNode,
                    $"Parameter '{sourceParam.Name}' has {ConstraintValidation.Describe(paramConstraint)} " +
                    $"which allows values outside property '{targetProp.Name}' constraint " +
                    $"{ConstraintValidation.Describe(propConstraint)}.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
            }
        }
    }

    private static void ValidateDownstreamConstraints(
        AnalysisContext context, Node errorNode,
        IReadOnlyList<Constraint> downstreamConstraints, Property targetProp, Property sourceParam) {
        foreach (var dc in downstreamConstraints) {
            // Check if downstream constraint is compatible with target property constraints
            var matchingPropConstraint = targetProp.Constraints
                .FirstOrDefault(c => c.GetType() == dc.GetType());
            if (matchingPropConstraint is null) continue;

            if (!IsConstraintSubsumed(dc, matchingPropConstraint)) {
                context.ReportWarning(
                    errorNode,
                    $"Effect-chain constraint {ConstraintValidation.Describe(dc)} from parameter " +
                    $"'{sourceParam.Name}' exceeds property '{targetProp.Name}' constraint " +
                    $"{ConstraintValidation.Describe(matchingPropConstraint)}.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
            }
        }
    }

    /// <summary>
    /// Returns true if <paramref name="inner"/> is at least as restrictive as <paramref name="outer"/>,
    /// meaning any value satisfying <c>inner</c> also satisfies <c>outer</c>.
    /// </summary>
    private static bool IsConstraintSubsumed(Constraint inner, Constraint outer) {
        if (inner is RangeConstraint ir && outer is RangeConstraint or) {
            return IsRangeSubsumed(ir, or);
        }
        if (inner is LengthConstraint il && outer is LengthConstraint ol) {
            return il.MinLength >= ol.MinLength && il.MaxLength <= ol.MaxLength;
        }
        // For other constraint types, assume compatible (exact match on type was already checked)
        return true;
    }

    private static bool IsRangeSubsumed(RangeConstraint inner, RangeConstraint outer) {
        var innerMin = inner.Minimum is not null ? ToDouble(inner.Minimum) : double.NegativeInfinity;
        var innerMax = inner.Maximum is not null ? ToDouble(inner.Maximum) : double.PositiveInfinity;
        var outerMin = outer.Minimum is not null ? ToDouble(outer.Minimum) : double.NegativeInfinity;
        var outerMax = outer.Maximum is not null ? ToDouble(outer.Maximum) : double.PositiveInfinity;

        if (innerMin is null || innerMax is null || outerMin is null || outerMax is null) return false;

        return innerMin.Value >= outerMin.Value && innerMax.Value <= outerMax.Value;
    }

    private static string FormatValueForMessage(object? value) => value switch {
        null => "<null>",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "?"
    };

    private static double? ToDouble(object? value) {
        try { return Convert.ToDouble(value); }
        catch { return null; }
    }


    private static bool TryResolveDomainType(
        AnalysisContext context,
        DomainTypeReference typeRef,
        DomainTypeLookupMetadata lookup,
        Node reportNode,
        out DomainType? resolvedType) {
        if (typeRef is null) {
            context.ReportError(reportNode, "Effect is missing a type reference.", DomainModelDiagnosticCodes.EffectBinding);
            resolvedType = null;
            return false;
        }

        var resolved = context.GetMetadata<ResolvedTypeReferenceMetadata>(typeRef);
        if (resolved is not null) {
            resolvedType = resolved.Type;
            return true;
        }

        if (!lookup.Types.TryGetValue(typeRef.TypeName, out resolvedType)) {
            context.ReportError(
                reportNode,
                $"Effect references unknown type '{typeRef.TypeName}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return false;
        }

        return true;
    }

    private static void ValidateUnsatisfiedRequirements(
        AnalysisContext context, Action action, Entity entity, DomainTypeLookupMetadata lookup) {
        var covered = CollectCoveredProperties(action.Effects);

        foreach (var effect in FlattenEffects(action.Effects)) {
            switch (effect) {
                case StageTransitionEffect ste:
                    ValidateStageTransitionRequirements(context, ste, entity, action, covered);
                    break;
                case CreateEntityInstance cei:
                    ValidateCreateEntityRequirements(context, cei, lookup, action, covered);
                    break;
            }
        }
    }

    private static void ValidateStageTransitionRequirements(
        AnalysisContext context, StageTransitionEffect ste, Entity entity,
        Action action, HashSet<string> coveredProperties) {
        var targetStage = entity.Stages.FirstOrDefault(
            s => string.Equals(s.Name, ste.TargetStage.StageName, StringComparison.Ordinal));
        if (targetStage is null) return;

        // STAGE-scoped required metadata only. Entity-level required metadata is NOT
        // a transition concern — it conflates two invariants that transitions cannot
        // (and should not) establish by assignment:
        //   (1) `required`-constraint props are CREATION invariants — set by create
        //       initializers, enforced by the Create factory (DMEFF011) /
        //       ValidateCreateEntityRequirements;
        //   (2) entity-policy `Exists` targets (e.g. `HasSource: policy { source exists }`)
        //       are LINK-TIME invariants — established by create-in / link_instances,
        //       not by transition assigns.
        // Falling back to entity metadata here produced false positives: every
        // transition warned that an entity-required prop (e.g. EntryPath) or a linked
        // nav (source) had no AssignEffect, even though both were set at construction /
        // link time. Genuine transition requirements are STAGE-scoped: stage-policy
        // Exists targets that entering the stage should establish via entry effects.
        var requiredMeta = context.GetMetadata<RequiredPropertiesMetadata>(targetStage);
        if (requiredMeta is null) return;

        foreach (var required in requiredMeta.RequiredProperties) {
            if (!coveredProperties.Contains(required.Name)) {
                context.ReportWarning(
                    action,
                    $"Action '{action.Name}' transitions to stage '{ste.TargetStage.StageName}' which requires property '{required.Name}', but no AssignEffect produces a value for it.",
                    DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement);
            }
        }
    }

    private static void ValidateCreateEntityRequirements(
        AnalysisContext context, CreateEntityInstance cei, DomainTypeLookupMetadata lookup,
        Action action, HashSet<string> coveredProperties) {
        if (!TryResolveDomainType(context, cei.Type, lookup, cei, out var resolvedType))
            return;

        if (resolvedType is not Entity targetEntity) return;

        var requiredMeta = context.GetMetadata<RequiredPropertiesMetadata>(targetEntity);
        if (requiredMeta is null) return;

        foreach (var required in requiredMeta.RequiredProperties) {
            bool coveredByInitializer = cei.Initializers.Any(
                i => string.Equals(i.PropertyName, required.Name, StringComparison.Ordinal));
            if (!coveredByInitializer && !coveredProperties.Contains(required.Name)) {
                context.ReportWarning(
                    action,
                    $"Action '{action.Name}' creates '{cei.Type.TypeName}' which requires property '{required.Name}', but no initializer or AssignEffect provides a value.",
                    DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement);
            }
        }
    }

    private static HashSet<string> CollectCoveredProperties(IReadOnlyList<Effect> effects) {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in FlattenEffects(effects)) {
            if (effect is AssignEffect ae && ae.Target is PropertyAccess pa) {
                covered.Add(pa.Name);
            }
        }
        return covered;
    }

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            switch (effect) {
                case ConditionalEffect ce:
                    foreach (var nested in FlattenEffects(ce.ThenEffects))
                        yield return nested;
                    if (ce.ElseEffects is not null) {
                        foreach (var nested in FlattenEffects(ce.ElseEffects))
                            yield return nested;
                    }
                    break;
                case CompositeEffect ce:
                    foreach (var nested in FlattenEffects(ce.Effects))
                        yield return nested;
                    break;
            }
        }
    }
}