using Poly.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Analysis;

internal sealed class EffectAnalyzer : INodeAnalyzer {
    public const string Id = "DomainEffectAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
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
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) {
            return;
        }

        DomainAnalysis.ForEachEntity(domain, entity => {
            foreach (var action in entity.Actions) {
                ValidateEffects(context, action.Effects, action, entity, domain, lookup);
                ValidateUnsatisfiedRequirements(context, action, entity, lookup);
                ValidateActionParameterUsage(context, action);
            }
            foreach (var stage in entity.Stages) {
                ValidateEffects(context, stage.OnEntryEffects, null, entity, domain, lookup);
                ValidateEffects(context, stage.OnExitEffects, null, entity, domain, lookup);
            }
        });
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
            case StageTransitionEffect:
            case DeleteEntityInstance:
            case LinkRelationshipEffect:
            case UnlinkRelationshipEffect:
            case TransitionRelationshipEffect:
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

        // ── Cross-effect ordering (folded from EffectOrderingAnalyzer — D2.4) ──
        var flattened = EffectHelpers.FlattenEffects(effects).ToArray();
        var deleteIndex = Array.FindIndex(flattened, static e => e is DeleteEntityInstance);
        if (deleteIndex >= 0 && flattened.Skip(deleteIndex + 1).Any(IsMutatingEffect)) {
            context.ReportWarning(
                flattened[deleteIndex],
                "Mutating effect executes after DeleteEntityInstance, which is a no-op on a deleted instance.",
                DomainModelDiagnosticCodes.EffectPrePostCondition);
        }
    }

    private static bool IsMutatingEffect(Effect effect) =>
        effect is AssignEffect
            or CreateEntityInstance
            or StageTransitionEffect
            or LinkRelationshipEffect
            or UnlinkRelationshipEffect
            or TransitionRelationshipEffect;

    private static void ValidateEffect(
        AnalysisContext context,
        Effect effect,
        Action? action,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup) {
        switch (effect) {
            case CreateEntityInstance cei:
                ValidateCreateEntityInstance(context, cei, entity, lookup, domain);
                break;
            case CreateEntityInRelationshipEffect createIn:
                ValidateCreateEntityInRelationship(context, createIn, entity, domain, lookup);
                break;

            case StageTransitionEffect ste:
                ValidateStageTransition(context, ste, entity);
                break;
            case InvokeActionEffect iae:
                ValidateInvokeAction(context, iae, entity, domain);
                break;
            case AssignEffect ae:
                ValidateAssign(context, ae, action, entity);
                break;
            case DeleteEntityInstance dei:
                ValidateDeleteEntityInstance(context, dei, lookup);
                break;
            case LinkRelationshipEffect lre:
                ValidateRelationshipName(context, lre.RelationshipName, domain, lre);
                break;
            case UnlinkRelationshipEffect ure:
                ValidateRelationshipName(context, ure.RelationshipName, domain, ure);
                break;
            case TransitionRelationshipEffect tre:
                ValidateTransitionRelationship(context, tre, domain);
                // DMEFF005: TransitionRelationshipEffect is analyzed but NOT executed at runtime.
                // It is stored in the model for evolution/planning but has no case in
                // DomainEntityInstance.ExecuteEffect — calls to it are silent no-ops.
                context.ReportWarning(
                    tre,
                    $"TransitionRelationshipEffect ('{tre.RelationshipName}' → '{tre.TargetStage.StageName}') " +
                    "is parsed and stored but is NOT executed at runtime. " +
                    "Use StageTransitionEffect to transition the current instance's stage instead.",
                    DomainModelDiagnosticCodes.EffectNotExecutable);
                break;
            case ConditionalEffect ce:
                ValidateEffects(context, ce.ThenEffects, action, entity, domain, lookup);
                if (ce.ElseEffects is not null) {
                    ValidateEffects(context, ce.ElseEffects, action, entity, domain, lookup);
                }
                // DMEFF006: warn if the conditional contains direct-execution effects
                // that would be silently dropped by EffectLoweringPass
                WarnNestedDirectEffects(context, "ConditionalEffect (then)", ce.ThenEffects);
                if (ce.ElseEffects is not null)
                    WarnNestedDirectEffects(context, "ConditionalEffect (else)", ce.ElseEffects);
                break;
            case CompositeEffect ce:
                ValidateEffects(context, ce.Effects, action, entity, domain, lookup);
                // DMEFF006: warn if composite contains direct-execution effects
                // that would be silently dropped by EffectLoweringPass
                WarnNestedDirectEffects(context, "CompositeEffect", ce.Effects);
                break;
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

                if (targetProp is null) {
                    context.ReportError(
                        initializer,
                        $"CreateEntityInstance initializer references unknown property '{initializer.PropertyName}' on entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.EffectBinding);
                    continue;
                }

                // Validate literal initializer values against property constraints
                if (initializer.Expression is Literal lit && targetProp.Constraints.Count > 0) {
                    ValidateLiteralAgainstConstraints(context, initializer, lit.Value, targetProp);
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
        // Check the relationship exists
        var relationship = domain.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, cei.RelationshipName, StringComparison.Ordinal));
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

    private static void ValidateCreateEntityInRelationship(
        AnalysisContext context, CreateEntityInRelationshipEffect createIn, Entity entity, Domain domain, DomainTypeLookupMetadata lookup) {
        // Validate relationship name exists
        var relationship = domain.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, createIn.RelationshipName, StringComparison.Ordinal));
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

        // Validate initializer property names against target entity
        foreach (var initializer in createIn.Initializers) {
            var targetProp = targetEntity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, initializer.PropertyName, StringComparison.Ordinal));

            if (targetProp is null) {
                context.ReportError(
                    initializer,
                    $"CreateIn initializer references unknown property '{initializer.PropertyName}' on entity '{targetEntity.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
                continue;
            }

            // Validate literal initializer values against property constraints
            if (initializer.Expression is Literal lit && targetProp.Constraints.Count > 0) {
                ValidateLiteralAgainstConstraints(context, initializer, lit.Value, targetProp);
            }
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
            var relationship = domain.Relationships.FirstOrDefault(r =>
                string.Equals(r.Name, iae.TargetRelationship, StringComparison.Ordinal));
            if (relationship is null) {
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
            var resolvedTarget = domain.Types.OfType<Entity>()
                .FirstOrDefault(e => string.Equals(e.Name, targetTypeName, StringComparison.Ordinal));
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
        if (inner is EnumConstraint ie && outer is EnumConstraint oe) {
            // Parameter's enum member set must be a subset of property's enum member set
            return ie.Members.All(im =>
                oe.Members.Any(om => string.Equals(im.Name, om.Name, StringComparison.Ordinal)));
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

    private static void ValidateDeleteEntityInstance(
        AnalysisContext context, DeleteEntityInstance dei, DomainTypeLookupMetadata lookup) {
        if (!TryResolveDomainType(context, dei.EntityType, lookup, dei, out var resolvedType)) {
            return;
        }

        if (resolvedType is not Entity) {
            context.ReportError(
                dei,
                $"DeleteEntityInstance type '{dei.EntityType.TypeName}' must resolve to an Entity.",
                DomainModelDiagnosticCodes.EffectBinding);
        }
    }

    private static void ValidateRelationshipName(
        AnalysisContext context, string relationshipName, Domain domain, Effect effect) {
        if (!domain.Relationships.Any(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal))) {
            context.ReportError(
                effect,
                $"Relationship effect references unknown relationship '{relationshipName}' in domain '{domain.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
        }
    }

    private static void ValidateTransitionRelationship(
        AnalysisContext context, TransitionRelationshipEffect tre, Domain domain) {
        var relationship = domain.Relationships.FirstOrDefault(r => string.Equals(r.Name, tre.RelationshipName, StringComparison.Ordinal));
        if (relationship is null) {
            context.ReportError(
                tre,
                $"TransitionRelationship effect references unknown relationship '{tre.RelationshipName}' in domain '{domain.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        if (!relationship.Stages.Any(s => string.Equals(s.Name, tre.TargetStage.StageName, StringComparison.Ordinal))) {
            context.ReportError(
                tre,
                $"TransitionRelationship effect targets stage '{tre.TargetStage.StageName}' which does not exist on relationship '{tre.RelationshipName}'.",
                DomainModelDiagnosticCodes.EffectBinding);
        }
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

        var requiredMeta = context.GetMetadata<RequiredPropertiesMetadata>(targetStage)
            ?? context.GetMetadata<RequiredPropertiesMetadata>(entity);
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