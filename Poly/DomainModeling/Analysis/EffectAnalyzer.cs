using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Validate pack: effect binding, ordering, parameter usage, and requirement coverage diagnostics.
/// Publishes one fact bag — <see cref="WriteVerificationFacts"/> (the write channel); create-in
/// resolution stays on <see cref="EffectFactsPass"/>.
/// </summary>
internal sealed partial class EffectAnalyzer : INodeAnalyzer {
    public const string Id = "DomainEffectAnalyzer";
    public string PassName => Id;
    // Lint-only: reads catalog types/resolved refs, RequiredProperties,
    // DownstreamConstraints, ActionInvariantMetadata. Its only publication is the
    // write-verification fact bag on the domain (V-03/V-04).
    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            ValidateDomainEffects(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomainEffects(AnalysisContext context, Domain domain) {
        // Fail-closed for V-04 (review H3): the facts bucket exists from the moment the domain pass
        // starts, so a missing bucket means this analyzer never ran — never "there was nothing to
        // report".
        WriteVerificationFactsStore.For(context, domain);

        var lookup = ResolveTypeLookup(context, domain);
        if (lookup is null) {
            // Fail closed (review F1): domain-bound validation without any type
            // lookup bag would silently omit every effect check. Report loudly.
            context.ReportError(
                domain,
                "Domain type lookup bag is unavailable; effect binding cannot be validated. " +
                "Run DomainCatalogPass before EffectAnalyzer.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return;
        }

        DomainAnalysis.ForEachEntity(domain, entity => {
            foreach (var action in entity.Actions) {
                ValidateEffects(context, action.Effects, action, entity, domain, lookup, currentStage: null);
                ValidateUnsatisfiedRequirements(context, action, entity, lookup);
                ValidateActionParameterUsage(context, action);
                ValidateActionReturnProducer(context, action, entity, domain, lookup);
                ValidateActionReturnFinalStatement(context, action, entity, domain, lookup);
                ValidateCallChainPostconditions(context, domain, action, entity);
            }
            // C4e (a): when Tracks B { transition to Nope } — subscription effects skipped this pass.
            foreach (var sub in entity.Subscriptions)
                ValidateEffects(context, sub.Effects, null, entity, domain, lookup, currentStage: null, sub.PeerBinding);
            foreach (var stage in entity.Stages) {
                ValidateEffects(context, stage.OnEntryEffects, null, entity, domain, lookup, stage.Name);
                ValidateEffects(context, stage.OnExitEffects, null, entity, domain, lookup, stage.Name);
                foreach (var action in stage.Actions) {
                    ValidateEffects(context, action.Effects, action, entity, domain, lookup, stage.Name);
                    ValidateUnsatisfiedRequirements(context, action, entity, lookup);
                    ValidateActionParameterUsage(context, action);
                    ValidateActionReturnProducer(context, action, entity, domain, lookup);
                    ValidateActionReturnFinalStatement(context, action, entity, domain, lookup);
                }
                foreach (var sub in stage.Subscriptions)
                    ValidateEffects(context, sub.Effects, null, entity, domain, lookup, stage.Name, sub.PeerBinding);
            }
            ValidateUnconditionalAutomaticTransitionCycles(context, entity);
            ValidateExitBlockEffects(context, entity);
            ValidateExitBlockTransitionTargets(context, entity);
            ProvableGuards.Validate(context, domain, entity);
        });

        ValidateObservedFlows(context, domain);
        ObservedFlowChecks.ReportUnusedAllowances(context, domain);
    }

    /// <summary>
    /// Each <see cref="ObservedFlow"/> is a copy the existing assign checks decide on, and it is
    /// recorded like any other write. An unresolvable or self flow is DMSEM005 on the domain (no
    /// allowance clears it). Otherwise a source with no same-kind constraint is not verified — a
    /// <c>DMCS003</c> warning naming the flow plus an <c>UnknownBound</c> fact (V-04 A4) — a source
    /// whose length or range sits inside the target's is verified, and a wider source keeps its
    /// <c>DMEFF008</c> on the target property (a matching allowance makes it a warning) with a
    /// <c>PossibleViolation</c> fact beside it. An optional source cannot flow into a required
    /// target (DMEFF014, #439): an observed flow has no guard site, so no allowance or guard clears
    /// it. Nothing is inserted into the tree.
    /// </summary>
    private static void ValidateObservedFlows(AnalysisContext context, Domain domain) {
        foreach (var flow in RuntimeAnalysisCache.Session(domain).ObservedFlows) {
            var source = ObservedFlowChecks.Resolve(domain, flow.SourcePath);
            var target = ObservedFlowChecks.Resolve(domain, flow.TargetPath);
            if (source is null || target is null
                || string.Equals(flow.SourcePath, flow.TargetPath, StringComparison.Ordinal)) {
                context.ReportError(
                    domain,
                    $"Observed flow '{flow.SourcePath}' -> '{flow.TargetPath}' does not name two distinct " +
                    "entity properties (use Entity.Property).",
                    DomainModelDiagnosticCodes.SemanticReferenceResolution);
                continue;
            }

            foreach (var targetConstraint in target.Value.Property.Constraints) {
                if (ConstraintKinds.Of(targetConstraint) is not { WriteRelevant: true })
                    continue;
                var sourceConstraint = source.Value.Property.Constraints
                    .FirstOrDefault(c => c.GetType() == targetConstraint.GetType());
                var constraint = ConstraintValidation.Describe(targetConstraint);

                // A source with NO same-kind constraint is not verified: the flow can carry any
                // value of that kind. A4(i) routes it through the recorder — a DMCS003 warning
                // naming the flow, plus an UnknownBound fact on the target — so the copy is never
                // silently skipped.
                if (sourceConstraint is null) {
                    context.ReportWarning(
                        target.Value.Property,
                        $"Observed flow '{flow.SourcePath}' -> '{flow.TargetPath}': the source has no " +
                        $"{constraint} constraint, so the copy may carry any value of that kind and " +
                        $"'{flow.TargetPath}' cannot be verified.",
                        DomainModelDiagnosticCodes.ConstraintUndecided);
                    WriteRecorder.Record(
                        context, domain, target.Value.Property, target.Value.Property.Name, constraint,
                        WriteVerdict.Unverified(
                            UnverifiedReason.UnknownBound, DomainModelDiagnosticCodes.ConstraintUndecided));
                    continue;
                }

                // A4(ii): a Range pair is decided by the one procedure every assign uses; a Length
                // pair keeps the declared-bounds rule.
                var subsumed = targetConstraint is RangeConstraint rangeTarget
                               && sourceConstraint is RangeConstraint rangeSource
                    ? WriteVerification.DecideRange(rangeTarget, AbstractValue.From([rangeSource]))
                        == ConstraintVerdict.Satisfied
                    : IsConstraintSubsumed(sourceConstraint, targetConstraint);
                if (subsumed) {
                    WriteRecorder.Record(
                        context, domain, target.Value.Property, target.Value.Property.Name, constraint,
                        WriteVerdict.Satisfied);
                    continue;
                }

                // A pattern or equals that differs proves nothing about a value being outside: an
                // unverified warning plus its fact, never an error (only Range and Length can err).
                if (targetConstraint is PatternConstraint or EqualityConstraint) {
                    context.ReportWarning(
                        target.Value.Property,
                        $"Observed flow '{flow.SourcePath}' -> '{flow.TargetPath}': " +
                        $"{ConstraintValidation.Describe(sourceConstraint)} differs from " +
                        $"{ConstraintValidation.Describe(targetConstraint)}, so the copy cannot be verified.",
                        DomainModelDiagnosticCodes.ConstraintUndecided);
                    WriteRecorder.Record(
                        context, domain, target.Value.Property, target.Value.Property.Name, constraint,
                        WriteVerdict.Unverified(
                            UnverifiedReason.PossibleViolation, DomainModelDiagnosticCodes.ConstraintUndecided));
                    continue;
                }

                // A4(iii): the finding is unchanged (an allowance may still downgrade it) and the
                // recorder gets a PossibleViolation fact beside it, so the summary can never read
                // proven while the flow is undecided.
                var severity = ObservedFlowChecks.SeverityFor(
                    context, domain, target.Value.Property, DomainModelDiagnosticCodes.EffectConstraintViolation);
                context.ReportDiagnostic(
                    target.Value.Property,
                    severity,
                    $"Observed flow '{flow.SourcePath}' -> '{flow.TargetPath}': " +
                    $"{ConstraintValidation.Describe(sourceConstraint)} allows values outside " +
                    $"{ConstraintValidation.Describe(targetConstraint)}.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                WriteRecorder.Record(
                    context, domain, target.Value.Property, target.Value.Property.Name, constraint,
                    WriteVerdict.Unverified(
                        UnverifiedReason.PossibleViolation, DomainModelDiagnosticCodes.EffectConstraintViolation));
            }

            // #439: `optional` is the only way an entity property may hold null, so a required
            // target paired with a non-required source may write null where the target cannot.
            if (EntityStructureAnalyzer.IsRequired(target.Value.Property)
                && !EntityStructureAnalyzer.IsRequired(source.Value.Property)) {
                context.ReportError(
                    target.Value.Property,
                    $"Observed flow '{flow.SourcePath}' -> '{flow.TargetPath}': the source is optional " +
                    $"and may carry null, but '{flow.TargetPath}' is required.",
                    DomainModelDiagnosticCodes.ObservedFlowNullability);
                ObservedFlowChecks.NoteNullabilityFinding(context, domain, target.Value.Property);
            }
        }
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
                CollectFromExpression(ae.Target, referenced, paramNames, valuePosition: false);
                CollectFromExpression(ae.Value, referenced, paramNames);
                break;
            case CreateEntityInstance cei:
                foreach (var init in cei.Initializers)
                    CollectFromExpression(init.Expression, referenced, paramNames);
                break;
            case CreateEntityInRelationshipEffect createIn:
                foreach (var init in createIn.Initializers)
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
            case ForEachEffect fe:
                if (fe.Where is not null)
                    CollectFromExpression(fe.Where, referenced, paramNames);
                foreach (var e in fe.Body) CollectFromEffect(e, referenced, paramNames);
                break;
            case StageTransitionEffect:
                break;
        }
    }

    private static void CollectFromExpression(
        DomainExpression expr,
        HashSet<string> referenced,
        HashSet<string> paramNames,
        bool valuePosition = true) {
        if (expr is ParameterAccess pa) {
            if (paramNames.Contains(pa.Name))
                referenced.Add(pa.Name);
            return;
        }
        // Parsed idents are PropertyAccess (`Name: name`). A path root (`lead Name`) is
        // the relationship name. An assign target is the property being written.
        if (valuePosition && expr is PropertyAccess prop && paramNames.Contains(prop.Name)) {
            referenced.Add(prop.Name);
            return;
        }
        if (valuePosition && expr is RelationshipNavigation rn && paramNames.Contains(rn.RelationshipName))
            referenced.Add(rn.RelationshipName);
        // Recursively walk Children to find parameter uses at any depth.
        foreach (var child in expr.Children) {
            if (child is DomainExpression de)
                CollectFromExpression(de, referenced, paramNames, valuePosition);
        }
    }

    private static void ValidateEffects(
        AnalysisContext context,
        IReadOnlyList<Effect> effects,
        Action? action,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup,
        string? currentStage,
        string? peerBinding = null) {
        // ── Per-effect validation ─────────────────────────────
        foreach (var effect in effects) {
            ValidateEffect(context, effect, action, entity, domain, lookup, currentStage, peerBinding);
        }
    }

    private static void ValidateEffect(
        AnalysisContext context,
        Effect effect,
        Action? action,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup,
        string? currentStage,
        string? peerBinding) =>
        new EffectValidationDispatch(context, action, entity, domain, lookup, currentStage, peerBinding).Route(effect);

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
        DomainTypeLookupMetadata lookup,
        string? currentStage,
        string? peerBinding,
        IReadOnlyDictionary<string, string?>? forBinders = null)
        : EffectDispatch<object?> {
        protected override object? Default() => null;

        private void RouteAll(IEnumerable<Effect> effects) {
            foreach (var effect in effects)
                Route(effect);
        }

        protected override object? CreateEntityInstance(CreateEntityInstance e) {
            ValidateCreateEntityInstance(context, e, entity, lookup, domain, action);
            return null;
        }

        protected override object? CreateEntityInRelationship(CreateEntityInRelationshipEffect e) {
            ValidateCreateEntityInRelationship(context, e, entity, domain, lookup, action);
            return null;
        }

        protected override object? StageTransition(StageTransitionEffect e) {
            ValidateStageTransition(context, e, entity);
            return null;
        }

        protected override object? InvokeAction(InvokeActionEffect e) {
            // `invoke line.Mark` inside `for line in …` targets the loop binder, not a
            // relationship: the binder is typed as the iterated relationship's target entity,
            // so the callee gets the same checks a relationship-targeted invoke runs (#455 F3).
            if (e.TargetRelationship is { } target && forBinders?.TryGetValue(target, out var binderTargetTypeName) == true) {
                if (binderTargetTypeName is not null
                    && TryResolveEntity(context, domain, binderTargetTypeName, e, out var binderTargetEntity)
                    && binderTargetEntity is not null)
                    ValidateInvokeCallee(context, e, binderTargetEntity, dispatchStage: null);
                return null;
            }
            ValidateInvokeAction(context, e, entity, domain, currentStage);
            return null;
        }

        protected override object? Link(LinkEffect e) {
            ValidateLinkEffect(context, e, e.RelationshipName, e.Target,
                $"link {e.RelationshipName} to {TargetText(e.Target)}", entity, domain, forBinders);
            return null;
        }

        protected override object? Unlink(UnlinkEffect e) {
            ValidateLinkEffect(context, e, e.RelationshipName, e.Target,
                $"unlink {e.RelationshipName} from {TargetText(e.Target)}", entity, domain, forBinders);
            return null;
        }

        private static string TargetText(DomainExpression target) =>
            target is PropertyAccess { Name: var name } ? name : "<expression>";

        protected override object? ForEachInvoke(ForEachInvokeEffect e) {
            ValidateForEachInvoke(context, e, entity, domain);
            return null;
        }

        // #409: the general `for` lowers and executes (slice 6); its body is checked for effect
        // legality here. #455: it iterates a relationship exactly as the fan-out form does, so
        // the relationship and callee checks are shared — analysis must reject what lowering
        // and the C# export cannot compile.
        protected override object? ForEach(ForEachEffect e) {
            var binders = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (forBinders is not null)
                foreach (var (name, targetTypeName) in forBinders)
                    binders[name] = targetTypeName;
            binders[e.BinderName] = null;
            if (ValidateForRelationship(context, domain, entity, e.RelationshipName, e, "ForEach") is { } iterated)
                binders[e.BinderName] = iterated.Name;
            // Legality: the body may write only the owning entity's own state. An assign
            // through a relationship or the loop binder is another entity's state (DMEFF012,
            // the same rule as actions and `when`); mutate that entity with `invoke binder.Action`.
            new EffectValidationDispatch(context, action, entity, domain, lookup, currentStage, peerBinding, binders)
                .RouteAll(e.Body);
            return null;
        }

        protected override object? Assign(AssignEffect e) {
            if (e.Target is RelationshipNavigation { RelationshipName: var root } && forBinders?.ContainsKey(root) == true) {
                context.ReportError(
                    e,
                    $"Assign targets state of loop binder '{root}', which belongs to another entity. " +
                    $"Only '{entity.Name}' may assign its own state; cross-entity access is read-only. " +
                    $"Invoke an action on the other entity instead (e.g. 'invoke {root}.SomeAction'); in an exit block, " +
                    $"move that work into an action — an exit block rejects 'invoke', 'create' and 'for … invoke' (DMEFF018).",
                    DomainModelDiagnosticCodes.EffectCrossEntityMutation);
                return null;
            }
            ValidateAssign(context, e, action, entity, domain, peerBinding);
            return null;
        }

        protected override object? Conditional(ConditionalEffect e) {
            RouteAll(e.ThenEffects);
            if (e.ElseEffects is not null) {
                RouteAll(e.ElseEffects);
            }
            return null;
        }

        protected override object? Composite(CompositeEffect e) {
            RouteAll(e.Effects);
            return null;
        }
    }

    private static void ValidateCreateEntityInstance(
        AnalysisContext context, CreateEntityInstance cei, Entity actionEntity, DomainTypeLookupMetadata lookup, Domain domain, Action? action) {
        if (!TryResolveDomainType(context, cei.Type, lookup, cei, out var resolvedType)) {
            return;
        }

        if (resolvedType is Entity targetEntity) {
            var entryAssigned = EntryAssignedOrEmpty(targetEntity, domain);
            foreach (var initializer in cei.Initializers) {
                var targetProp = targetEntity.Properties
                    .FirstOrDefault(p => string.Equals(p.Name, initializer.PropertyName, StringComparison.Ordinal));

                // Initializers may also bind a singular navigation property of the
                // target entity (e.g. `create in loans { book: book }`) — the runtime
                // evaluates the binding into the child's value bag and the exporter
                // wires it as a Create(...) nav parameter. Rejecting it here would
                // contradict both (and the shipped demo).
                if (targetProp is null && !IsSingularTargetNavigation(context, domain, targetEntity.Name, initializer.PropertyName)) {
                    context.ReportError(
                        initializer,
                        $"CreateEntityInstance initializer references unknown property '{initializer.PropertyName}' on entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.EffectBinding);
                    continue;
                }

                if (targetProp is null) continue; // nav binding — no property constraints to check

                if (ValidateNotEntryAssigned(context, targetEntity, entryAssigned, initializer)) continue;

                // Every initializer write gets exactly one verdict, recorded once (A3): a literal
                // through the constraint evaluator, every other value through the flow channel.
                // The value is inferred in the CREATOR's scope, with the creating action in scope (A1).
                ValidateInitializerValue(context, domain, initializer, actionEntity, action, targetEntity, targetProp);
            }

            // P2′.2: Reject bare create of exclusively-owned entity types
            // An entity is exclusively-owned if it is only ever the target of SourceOwnsTarget relationships
            // and no relationship has it as source.
            if (cei.RelationshipName is null && IsExclusivelyOwned(context, targetEntity, domain)) {
                context.ReportError(
                    cei,
                    $"Cannot directly create '{targetEntity.Name}': it is exclusively owned by other entities. " +
                    $"Use 'create in' on a relationship that has '{targetEntity.Name}' as the target.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }

            // D: every required property must be provided (auto-wire only in create-in).
            ValidateRequiredInitializerCoverage(context, cei, targetEntity,
                autoWireSourceEntity: null, createInRelationship: null, cei.Initializers);

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

    private static bool IsExclusivelyOwned(AnalysisContext context, Entity entity, Domain domain) {
        var entityName = entity.Name;
        bool isSource = false;
        bool isOwnedTarget = false;

        foreach (var rel in context.GetAllRelationships(domain)) {
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
    /// Catalog relationship lookup.
    /// </summary>
    private static RelationshipLookupMetadata? ResolveRelationshipLookup(AnalysisContext context, Domain domain) =>
        context.GetRelationshipLookup(domain) ?? context.GetRelationshipLookup();

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
    /// Catalog type lookup.
    /// </summary>
    private static DomainTypeLookupMetadata? ResolveTypeLookup(AnalysisContext context, Domain domain) =>
        context.GetTypeLookup(domain) ?? context.GetTypeLookup();

    /// <summary>
    /// Fail-closed: domain-bound validation cannot proceed without the semantic
    /// lookup bags. Reports a structural failure instead of silently skipping
    /// the check (review F1 — bag-skip is not fail-closed).
    /// </summary>
    private static void ReportCatalogUnavailable(AnalysisContext context, Node node) =>
        context.ReportError(
            node,
            "Domain relationship/type lookup bags are unavailable; effect binding cannot be validated. " +
            "Run DomainCatalogPass before EffectAnalyzer.",
            DomainModelDiagnosticCodes.SemanticReferenceResolution);

    // DMEFF019 reads the may-assign fact (Q15 b); every relief consumer keeps the definite flat fact.
    // An unresolvable hop is reported on its own, so the check sees no names rather than throwing.
    private static IReadOnlySet<string> EntryAssignedOrEmpty(Entity targetEntity, Domain domain) {
        StageEntryFacts.TryEntryAssignedProperties(targetEntity, domain, out var names);
        return names;
    }

    /// <summary>
    /// True (after reporting DMEFF019 on the initializer) when it supplies a property the target's
    /// entry chain may assign. The caller skips the value verdict: the value can never reach the object.
    /// </summary>
    private static bool ValidateNotEntryAssigned(
        AnalysisContext context, Entity targetEntity, IReadOnlySet<string> entryAssigned, PropertyBinding initializer) {
        if (!entryAssigned.Contains(initializer.PropertyName))
            return false;
        context.ReportError(
            initializer,
            StageEntryFacts.CannotSupplyMessage(targetEntity, initializer.PropertyName),
            DomainModelDiagnosticCodes.CreateSuppliesEntryAssignedProperty);
        return true;
    }

    private static void ValidateCreateEntityInRelationship(
        AnalysisContext context, CreateEntityInRelationshipEffect createIn, Entity entity, Domain domain, DomainTypeLookupMetadata lookup, Action? action) {
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
        var entryAssigned = EntryAssignedOrEmpty(targetEntity, domain);
        foreach (var initializer in createIn.Initializers) {
            var targetProp = targetEntity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, initializer.PropertyName, StringComparison.Ordinal));

            // Singular nav bindings are legal (see ValidateCreateEntityInstance) —
            // the demo's `create in loans { book: book }` relies on this.
            if (targetProp is null && !IsSingularTargetNavigation(context, domain, targetEntity.Name, initializer.PropertyName)) {
                context.ReportError(
                    initializer,
                    $"CreateIn initializer references unknown property '{initializer.PropertyName}' on entity '{targetEntity.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
                continue;
            }

            if (targetProp is null) continue; // nav binding — no property constraints to check

            if (ValidateNotEntryAssigned(context, targetEntity, entryAssigned, initializer)) continue;

            // Same channel, same creator scope: `create in` runs inside the source entity's action.
            ValidateInitializerValue(context, domain, initializer, entity, action, targetEntity, targetProp);
        }

        // D: every required property must be provided — the back-reference nav
        // (typed as the source entity) is auto-wired and may be omitted.
        ValidateRequiredInitializerCoverage(context, createIn, targetEntity,
            autoWireSourceEntity: entity, createInRelationship: relationship, createIn.Initializers);
    }

    /// <summary>
    /// True when <paramref name="name"/> is a SINGULAR navigation property declared
    /// on <paramref name="targetEntityName"/> (a one-to-one relationship sourced from
    /// that entity). Collection navs are not bindable initializer targets — the
    /// exporter emits empty collections for those.
    /// </summary>
    private static bool IsSingularTargetNavigation(AnalysisContext context, Domain domain, string targetEntityName, string name) {
        var lookup = context.GetRelationshipLookup(domain);
        return lookup is not null
            && lookup.TryGetRelationship(targetEntityName, name, out var rel)
            && rel.Cardinality == RelationshipCardinality.OneToOne;
    }

    /// <summary>
    /// DMEFF011: a <c>create</c> / <c>create in</c> must provide a value for every
    /// required property of the created entity — a property without <c>optional</c> —
    /// unless the property has a <c>default</c>, is initialized by the initial stage's
    /// entry effects, or is the auto-wired back-reference navigation (the target's nav
    /// typed as the create-in source entity). Without this, the generated <c>Create</c>
    /// factory would fail at runtime on a missing required value — analysis catches the
    /// bad authoring shape before the export/runtime path. A required to-one navigation
    /// (L-15a) must be bound the same way, unless it is the navigation <c>create in</c>
    /// fills with the source (<see cref="CreateInWiring.FilledNavigation"/>, the rule
    /// lowering reads).
    /// </summary>
    private static void ValidateRequiredInitializerCoverage(
        AnalysisContext context,
        Effect effect,
        Entity targetEntity,
        Entity? autoWireSourceEntity,
        Relationship? createInRelationship,
        IReadOnlyList<PropertyBinding> initializers) {
        var provided = new HashSet<string>(
            initializers.Select(i => i.PropertyName), StringComparer.Ordinal);
        var required = context.GetStructure(targetEntity)?.RequiredPropertyNames
            ?? EntityStructureAnalyzer.ComputeRequiredPropertyNames(targetEntity);
        var entryAssigned = context.GetStructure(targetEntity)?.EntryAssignedPropertyNames
            ?? EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(targetEntity);
        foreach (var prop in targetEntity.Properties) {
            if (!required.Contains(prop.Name)) continue;
            if (prop.Constraints.Any(static c => c is DefaultValueConstraint)) continue;
            if (entryAssigned.Contains(prop.Name)) continue;
            if (autoWireSourceEntity is not null
                && string.Equals(prop.Type.TypeName, autoWireSourceEntity.Name, StringComparison.Ordinal))
                continue; // auto-wired back-reference
            if (provided.Contains(prop.Name)) continue;

            context.ReportError(
                effect,
                $"Create effect on '{targetEntity.Name}' does not provide required property '{prop.Name}'. " +
                $"Every property without 'optional' must be set in the create / create in initializers (or have a default).",
                DomainModelDiagnosticCodes.CreateMissingRequiredProperty);
        }

        var filled = autoWireSourceEntity is not null && createInRelationship is not null
            ? CreateInWiring.FilledNavigation(autoWireSourceEntity, createInRelationship, targetEntity)
            : null;
        foreach (var nav in targetEntity.Navigations) {
            if (!nav.IsRequiredToOne()) continue;
            if (ReferenceEquals(nav, filled)) continue; // `create in` fills it with the source
            if (provided.Contains(nav.Name)) continue;

            context.ReportError(
                effect,
                $"Create effect on '{targetEntity.Name}' does not bind '{nav.Name}': " +
                $"navigation '{nav.Name}' is required: bind it, or declare it `optional`.",
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

    /// <summary>
    /// Shared <c>for</c> relationship check (#409 / #455 F1–F2): a <c>for</c> iterates a
    /// relationship declared on the caller's source side, OneToMany (iterating a known singular
    /// makes no sense), and never a self-relationship. Both the fan-out form
    /// (<see cref="ForEachInvokeEffect"/>, <c>for Rel as x … invoke</c>) and the general form
    /// (<see cref="ForEachEffect"/>, <c>for x in Rel { … }</c>) go through here, so analysis
    /// rejects what lowering and the C# export cannot compile.
    /// Returns the iterated target entity, or null after reporting a positioned error.
    /// </summary>
    private static Entity? ValidateForRelationship(
        AnalysisContext context, Domain domain, Entity entity, string relationshipName, Node reportNode,
        string formName) {
        if (!TryResolveRelationship(context, domain, entity.Name, relationshipName, reportNode, out var relationship))
            return null;
        if (relationship is null) {
            // Reverse-side detection: the name exists on a different source entity,
            // so this is a wrong-direction `for`, not an unknown relationship.
            // (Discovery round5 F9 — the old message sent authors hunting for a
            // missing declaration that exists elsewhere.)
            var relLookup = ResolveRelationshipLookup(context, domain);
            if (relLookup is not null) {
                var elsewhere = relLookup.FindByNameAcrossSources(relationshipName).FirstOrDefault();
                if (elsewhere is not null) {
                    context.ReportError(
                        reportNode,
                        $"{formName} relationship '{relationshipName}' is declared on source entity " +
                        $"'{elsewhere.Source.TypeName}', not '{entity.Name}' — reverse-side iteration " +
                        "is not supported.",
                        DomainModelDiagnosticCodes.EffectInvokeShape);
                    return null;
                }
            }
            context.ReportError(
                reportNode,
                $"{formName} references relationship '{relationshipName}' which does not exist on domain.",
                DomainModelDiagnosticCodes.EffectBinding);
            return null;
        }
        if (!string.Equals(relationship.Source.TypeName, entity.Name, StringComparison.Ordinal)) {
            context.ReportError(
                reportNode,
                $"{formName} relationship '{relationshipName}' may only be used from source entity " +
                $"'{relationship.Source.TypeName}' (caller is '{entity.Name}').",
                DomainModelDiagnosticCodes.EffectInvokeShape);
            return null;
        }
        if (relationship.Cardinality is not RelationshipCardinality.OneToMany) {
            context.ReportError(
                reportNode,
                $"{formName} requires OneToMany relationship '{relationshipName}', " +
                $"got {relationship.Cardinality}. Iterating a singular relationship is not supported.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
            return null;
        }
        if (string.Equals(relationship.Source.TypeName, relationship.Target.TypeName, StringComparison.Ordinal)) {
            context.ReportError(
                reportNode,
                $"{formName} on self-relationship '{relationshipName}' is not supported.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
            return null;
        }

        if (!TryResolveEntity(context, domain, relationship.Target.TypeName, reportNode, out var targetEntity))
            return null;
        if (targetEntity is null) {
            context.ReportError(
                reportNode,
                $"Target entity type '{relationship.Target.TypeName}' for relationship '{relationshipName}' not found.",
                DomainModelDiagnosticCodes.EffectBinding);
            return null;
        }
        return targetEntity;
    }

    private static void ValidateForEachInvoke(
        AnalysisContext context, ForEachInvokeEffect efe, Entity entity, Domain domain) {
        if (ValidateForRelationship(context, domain, entity, efe.RelationshipName, efe, "ForEachInvoke")
            is not { } targetEntity)
            return;

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
                var predicatePolicy = targetEntity.Policies.FirstOrDefault(p =>
                    string.Equals(p.Name, policyName, StringComparison.Ordinal));
                if (predicatePolicy is null) {
                    context.ReportError(
                        efe,
                        $"ForEachInvoke predicate policy '{policyName}' does not exist on entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.EffectBinding);
                }
                else if (EnumerateRelationshipNavigations(predicatePolicy.Expression).Any()) {
                    // An unlinked hop simulates false, but the export dereferences
                    // null, so a `for` must not call such a policy.
                    context.ReportError(
                        efe,
                        $"ForEachInvoke predicate policy '{policyName}' reads a path-prefix hop; " +
                        "an unlinked hop simulates false but the export dereferences null. " +
                        "Use a local policy over the record's own properties.",
                        DomainModelDiagnosticCodes.EffectInvokeShape);
                }
                else if (TestsPropertyExists(context, domain, predicatePolicy.Expression, targetEntity)) {
                    // Simulate and export disagree on whether an unset property exists
                    // (an unset Text simulates present; the export's unset Number is
                    // never null), so the two would pick different records.
                    context.ReportError(
                        efe,
                        $"ForEachInvoke predicate policy '{policyName}' tests 'exists' on a property; " +
                        "simulate and export disagree on whether an unset property exists. " +
                        "Compare the property to a value instead.",
                        DomainModelDiagnosticCodes.EffectInvokeShape);
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
        var targetAction = DomainAnalysis.FindAction(targetEntity, efe.ActionName)
            ?? DomainAnalysis.FindAnyNamedAction(targetEntity, efe.ActionName);
        if (targetAction is null) {
            context.ReportError(
                efe,
                $"ForEachInvoke references action '{efe.ActionName}' which does not exist on entity '{targetEntity.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // Discovery round5 F8: fan-out invoking an entity-returning action from a void
        // body drops the created instance and the export cannot compile it (CS0029 —
        // DomainResult<T> returned from a DomainResult method). Fail-closed at analysis.
        if (targetAction.Result.Members.Count > 0) {
            context.ReportError(
                efe,
                $"ForEachInvoke references action '{targetAction.Name}' which declares a return type " +
                $"('{targetAction.Result.Members[0].Type.TypeName}'). Fan-out invoking an entity-returning " +
                "action is not supported — the created instance would be discarded.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
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

    internal static IEnumerable<RelationshipNavigation> EnumerateRelationshipNavigations(DomainExpression expr) {
        if (expr is RelationshipNavigation rn)
            yield return rn;
        foreach (var child in expr.Children.OfType<DomainExpression>())
            foreach (var nested in EnumerateRelationshipNavigations(child))
                yield return nested;
    }

    /// <summary>True when an expression tests <c>exists</c> / <c>not exists</c> on a property
    /// (not a relationship) of <paramref name="entity"/>, or, inside an
    /// <c>any</c>/<c>all</c>/<c>none</c>/<c>count</c> body, of the related entity.</summary>
    private static bool TestsPropertyExists(
        AnalysisContext context, Domain domain, DomainExpression expr, Entity entity) {
        var target = expr switch { Exists e => e.Target, NotExists ne => ne.Target, _ => null };
        if (target is PropertyAccess pa
            && entity.Properties.Any(p => string.Equals(p.Name, pa.Name, StringComparison.Ordinal)))
            return true;
        var quantified = expr switch {
            AnyExpr a => a.RelationshipName,
            AllExpr a => a.RelationshipName,
            NoneExpr n => n.RelationshipName,
            CountExpr c => c.RelationshipName,
            _ => null
        };
        var scope = entity;
        if (quantified is not null) {
            // Quantifiers are source-side only. An unresolved quantifier at the top of the
            // policy is reported by the policy analyzer; one nested inside another
            // quantifier's body is not validated anywhere yet, so its body goes unchecked here.
            if (!TryResolveRelationship(context, domain, entity.Name, quantified, expr, out var relationship)
                || relationship is null
                || !TryResolveEntity(context, domain, relationship.Target.TypeName, expr, out var related)
                || related is null)
                return false;
            scope = related;
        }
        return expr.Children.OfType<DomainExpression>().Any(child => TestsPropertyExists(context, domain, child, scope));
    }

    private static void ValidateInvokeAction(
        AnalysisContext context, InvokeActionEffect iae, Entity entity, Domain domain,
        string? currentStage) {
        // Fail-closed policy: reject ambiguous/unanalyzed shapes now; relax only when
        // analyzers can prove the edge case is safe (see guide + effect-surface plan).
        var hasRel = iae.TargetRelationship is not null;

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

            // Singular cross-entity invoke is OneToOne only. OneToMany fan-out uses `for`.
            if (isCollectionFromSource) {
                context.ReportError(
                    iae,
                    $"InvokeAction on OneToMany relationship '{iae.TargetRelationship}' from '{entity.Name}' " +
                    "is not supported — use 'for Rel as name invoke name.Action' for fan-out.",
                    DomainModelDiagnosticCodes.EffectInvokeShape);
            }
        }
        else {
            targetEntity = entity;
        }

        ValidateInvokeCallee(context, iae, targetEntity, iae.TargetRelationship is null ? currentStage : null);
    }

    /// <summary>
    /// Callee checks shared by every invoke that resolves a target entity — a relationship
    /// target and a general-<c>for</c> loop binder (#455 F3): the action exists on the target
    /// entity, it is not entity-returning, and every parameter binding is known, present
    /// and unique.
    /// </summary>
    private static void ValidateInvokeCallee(
        AnalysisContext context, InvokeActionEffect iae, Entity targetEntity, string? dispatchStage) {
        var targetAction = DomainAnalysis.FindAction(targetEntity, iae.ActionName, dispatchStage);
        if (targetAction is null && dispatchStage is null)
            targetAction = DomainAnalysis.FindAnyNamedAction(targetEntity, iae.ActionName);
        if (targetAction is null) {
            context.ReportError(
                iae,
                $"InvokeAction effect references action '{iae.ActionName}' which does not exist " +
                $"on entity '{targetEntity.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        // Discovery round5 F8: invoking an entity-returning action as an effect drops
        // the created instance (and the export cannot compile it in a void body — CS0029).
        if (targetAction.Result.Members.Count > 0) {
            context.ReportError(
                iae,
                $"InvokeAction effect references action '{targetAction.Name}' which declares a return type " +
                $"('{targetAction.Result.Members[0].Type.TypeName}'). Invoking an entity-returning action " +
                "as an effect is not supported — the created instance would be discarded.",
                DomainModelDiagnosticCodes.EffectInvokeShape);
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
    }

    /// <summary>
    /// Validates the call-chain postconditions: effects of actions this action invokes
    /// (transitively), whose value ranges were computed under the caller's narrowed context.
    /// A callee assignment that can violate its target's constraint while running under this
    /// caller is reported on the callee effect with the caller chain named.
    /// </summary>
    private static void ValidateCallChainPostconditions(
        AnalysisContext context, Domain domain, Action action, Entity entity) {
        var meta = context.GetMetadata<ActionInvariantMetadata>(action);
        if (meta is null) return;

        // The same callee write is reachable from several stage contexts of the caller, and each context
        // folds its own policies into the written range, so the contexts can disagree. One finding per
        // (callee effect, call) carries the WORST verdict over every context (Violated beats Undecided),
        // and one fact per callee effect carries the worst of its calls: V-04 counts writes, not contexts
        // (V-05b B1, V-03 A2). The call is the occurrence index of the effect in a context's postconditions;
        // two invokes of one callee each report.
        var findings = new Dictionary<(Node Effect, int Call), (EffectPostcondition Post, RangeConstraint Range, ValueRange Value, bool Violated)>();
        var order = new List<(Node Effect, int Call)>();

        foreach (var stageCtx in meta.StageContexts()) {
            var calls = new Dictionary<Node, int>(ReferenceEqualityComparer.Instance);
            foreach (var post in stageCtx.Postconditions) {
                if (post.DeclaringAction == action) continue; // direct effects — validated per-effect
                calls[post.Effect] = calls.GetValueOrDefault(post.Effect) + 1;
                var call = calls[post.Effect];
                // The postcondition's Constraints are the target's net constraint (declared +
                // param/binder merges), valid for cross-entity callees whose target property
                // lives on a different entity than the caller.
                var range = post.Constraints.OfType<RangeConstraint>().FirstOrDefault();
                if (range is null) continue;

                // A postcondition with no value range is skipped: the callee's own assign already records
                // DMCS003 for that write, so the chain adds no second finding (V-05b fix round 1).
                if (post.ValueRange is not { } vr) continue;

                // The written value is the callee's own range under the caller's narrowed context, as
                // an exact interval (V-05b S3b): a both-open range carries no information (A3), and the
                // decision is the one write procedure every assign uses.
                var value = vr.ToRangeConstraint() is { } written
                    ? AbstractValue.From([written])
                    : AbstractValue.Unknown;
                var verdict = WriteVerification.DecideRange(range, value);
                if (verdict == ConstraintVerdict.Satisfied) continue;

                var key = (post.Effect, call);
                var violated = verdict == ConstraintVerdict.Violated;
                if (!findings.TryGetValue(key, out var existing)) {
                    findings[key] = (post, range, vr, violated);
                    order.Add(key);
                }
                else if (violated && !existing.Violated)
                    findings[key] = (post, range, vr, true);
            }
        }

        var recorded = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        foreach (var effect in order.Select(k => k.Effect).Distinct(ReferenceEqualityComparer.Instance).Cast<Node>()) {
            var calls = order.Where(k => ReferenceEquals(k.Effect, effect)).Select(k => findings[k]).ToList();
            var worst = calls.FirstOrDefault(f => f.Violated);
            if (worst.Post is null) worst = calls[0];
            if (!recorded.Add(effect)) continue;
            WriteRecorder.Record(
                context, domain, effect, worst.Post.TargetProperty,
                ConstraintValidation.Describe(worst.Range),
                worst.Violated
                    ? WriteVerdict.Violated
                    : WriteVerdict.Unverified(
                        UnverifiedReason.PossibleViolation,
                        DomainModelDiagnosticCodes.EffectConstraintViolation));
        }

        foreach (var key in order) {
            var (post, range, vr, violated) = findings[key];
            var message =
                $"Call-chain postcondition ({action.Name} → {post.DeclaringAction.Name}): assigned value range " +
                $"[{FormatBound(vr.Min, lower: true)}, {FormatBound(vr.Max, lower: false)}] " +
                (violated ? "is entirely outside" : "can fall outside") +
                $" constraint {ConstraintValidation.Describe(range)} on property '{post.TargetProperty}'.";
            if (violated)
                context.ReportError(post.Effect, message, DomainModelDiagnosticCodes.EffectConstraintViolation);
            else
                context.ReportWarning(post.Effect, message, DomainModelDiagnosticCodes.EffectConstraintViolation);
        }
    }

    private static string FormatRangeValue(decimal d) =>
        d.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A written edge of a value range. An absent side is unbounded and prints as the
    /// <see cref="ConstraintValidation"/> glyph, never as the number 0 (#475).</summary>
    private static string FormatBound(decimal? bound, bool lower) =>
        bound is { } value ? FormatRangeValue(value) : lower ? "−∞" : "+∞";

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
        // A pattern or an equality is subsumed only by the identical constraint.
        if (inner is PatternConstraint ip && outer is PatternConstraint op) {
            return string.Equals(ip.Pattern, op.Pattern, StringComparison.Ordinal);
        }
        if (inner is EqualityConstraint ie && outer is EqualityConstraint oe) {
            return Equals(ie.ExpectedValue, oe.ExpectedValue);
        }
        // For other constraint types, assume compatible (exact match on type was already checked)
        return true;
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

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) =>
        EffectHelpers.FlattenEffects(effects);
}