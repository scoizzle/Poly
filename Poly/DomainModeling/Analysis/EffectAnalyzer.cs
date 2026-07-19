using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

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
        if (!context.TryBeginAnalyzerVisit<EffectAnalyzer>(domain)) {
            return;
        }

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) {
            return;
        }

        foreach (var entity in lookup.Entities) {
            foreach (var action in entity.Actions) {
                ValidateEffects(context, action.Effects, entity, domain, lookup);
                ValidateUnsatisfiedRequirements(context, action, entity, lookup);
            }

            foreach (var stage in entity.Stages) {
                ValidateEffects(context, stage.OnEntryEffects, entity, domain, lookup);
                ValidateEffects(context, stage.OnExitEffects, entity, domain, lookup);
            }
        }
    }

    private static void ValidateEffects(
        AnalysisContext context,
        IReadOnlyList<Effect> effects,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup) {
        foreach (var effect in effects) {
            ValidateEffect(context, effect, entity, domain, lookup);
        }
    }

    private static void ValidateEffect(
        AnalysisContext context,
        Effect effect,
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
                ValidateAssign(context, ae, entity);
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
                ValidateEffects(context, ce.ThenEffects, entity, domain, lookup);
                if (ce.ElseEffects is not null) {
                    ValidateEffects(context, ce.ElseEffects, entity, domain, lookup);
                }
                // DMEFF006: warn if the conditional contains direct-execution effects
                // that would be silently dropped by EffectLoweringPass
                WarnNestedDirectEffects(context, "ConditionalEffect (then)", ce.ThenEffects);
                if (ce.ElseEffects is not null)
                    WarnNestedDirectEffects(context, "ConditionalEffect (else)", ce.ElseEffects);
                break;
            case CompositeEffect ce:
                ValidateEffects(context, ce.Effects, entity, domain, lookup);
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
                if (!targetEntity.Properties.Any(p => string.Equals(p.Name, initializer.PropertyName, StringComparison.Ordinal))) {
                    context.ReportError(
                        initializer,
                        $"CreateEntityInstance initializer references unknown property '{initializer.PropertyName}' on entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.EffectBinding);
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
            if (!targetEntity.Properties.Any(p => string.Equals(p.Name, initializer.PropertyName, StringComparison.Ordinal))) {
                context.ReportError(
                    initializer,
                    $"CreateIn initializer references unknown property '{initializer.PropertyName}' on entity '{targetEntity.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
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
        // Resolve which entity the action must live on
        Entity targetEntity;
        if (iae.TargetRelationship is not null) {
            // E3b: cross-entity invoke — action is on the other side of a relationship
            var relationship = domain.Relationships.FirstOrDefault(r =>
                string.Equals(r.Name, iae.TargetRelationship, StringComparison.Ordinal));
            if (relationship is null) {
                context.ReportError(
                    iae,
                    $"InvokeAction effect references relationship '{iae.TargetRelationship}' which does not exist on domain.",
                    DomainModelDiagnosticCodes.EffectBinding);
                return;
            }

            // Determine which side of the relationship we are on
            string otherSideTypeName;
            if (string.Equals(relationship.Source.TypeName, entity.Name, StringComparison.Ordinal))
                otherSideTypeName = relationship.Target.TypeName;
            else if (string.Equals(relationship.Target.TypeName, entity.Name, StringComparison.Ordinal))
                otherSideTypeName = relationship.Source.TypeName;
            else {
                context.ReportError(
                    iae,
                    $"Entity '{entity.Name}' does not participate in relationship '{iae.TargetRelationship}' " +
                    $"(source={relationship.Source.TypeName}, target={relationship.Target.TypeName}).",
                    DomainModelDiagnosticCodes.EffectBinding);
                return;
            }

            targetEntity = domain.Types.OfType<Entity>()
                .FirstOrDefault(e => string.Equals(e.Name, otherSideTypeName))!;
            if (targetEntity is null) {
                context.ReportError(
                    iae,
                    $"Target entity type '{otherSideTypeName}' for relationship '{iae.TargetRelationship}' not found.",
                    DomainModelDiagnosticCodes.EffectBinding);
                return;
            }
        }
        else {
            // E3a: self invoke
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

        foreach (var binding in iae.ParameterBindings) {
            if (!targetAction.Parameters.Any(p => string.Equals(p.Name, binding.PropertyName, StringComparison.Ordinal))) {
                context.ReportError(
                    binding,
                    $"InvokeAction effect binding references unknown parameter '{binding.PropertyName}' on action '{targetAction.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }

        // Validate filter expression properties exist on target entity
        if (iae.Filter is not null && targetEntity is not null) {
            ValidateFilterProperties(context, iae.Filter, targetEntity);
        }
    }

    private static void ValidateFilterProperties(
        AnalysisContext context, DomainExpression expr, Entity targetEntity) {
        if (expr is PropertyAccess pa) {
            if (!targetEntity.Properties.Any(p =>
                string.Equals(p.Name, pa.Name, StringComparison.Ordinal))) {
                context.ReportError(
                    expr,
                    $"InvokeAction filter references property '{pa.Name}' which does not exist on target entity '{targetEntity.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }
        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            ValidateFilterProperties(context, child, targetEntity);
        }
    }

    private static void ValidateAssign(
        AnalysisContext context, AssignEffect ae, Entity entity) {
        if (ae.Target is PropertyAccess propAccess) {
            if (!entity.Properties.Any(p => string.Equals(p.Name, propAccess.Name, StringComparison.Ordinal))) {
                context.ReportError(
                    ae,
                    $"Assign effect targets property '{propAccess.Name}' which does not exist on entity '{entity.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }
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