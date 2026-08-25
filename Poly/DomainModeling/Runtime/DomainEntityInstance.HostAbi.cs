using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;

using Action = Poly.DomainModeling.Ontology.Action;
using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Runtime;

public sealed partial record DomainEntityInstance {
    /// <summary>
    /// Dispatches direct-execution effects using <see cref="EffectDispatch{TResult}"/>.
    /// Named by the Effect subtype, not by the pattern (no Visit*).
    /// </summary>
    private sealed class EffectExecutor : EffectDispatch<object?> {
        private readonly DomainEntityInstance _instance;
        private readonly EffectLoweringPass _effectPass;
        private readonly TypeDefinitionNodeAnalyzer _typeProvider;

        private EffectExecutor(DomainEntityInstance instance,
            EffectLoweringPass effectPass, TypeDefinitionNodeAnalyzer typeProvider) {
            _instance = instance;
            _effectPass = effectPass;
            _typeProvider = typeProvider;
        }

        protected override object? Default() => null;

        public static void Run(DomainEntityInstance instance,
            EffectLoweringPass effectPass, TypeDefinitionNodeAnalyzer typeProvider,
            Effect effect) {
            new EffectExecutor(instance, effectPass, typeProvider).Route(effect);
        }

        protected override object? StageTransition(StageTransitionEffect transition) =>
            throw new InvalidOperationException(
                "StageTransitionEffect must lower to Ast; EffectExecutor is not the shipped path.");

        protected override object? CreateEntityInstance(CreateEntityInstance create) {
            return _instance.CreateChildInstance(create, _typeProvider);
        }

        protected override object? CreateEntityInRelationship(CreateEntityInRelationshipEffect createIn) {
            return _instance.ExecuteCreateInRelationship(createIn, _typeProvider);
        }

        protected override object? InvokeAction(InvokeActionEffect invoke) {
            if (invoke.TargetRelationship is null)
                throw new InvalidOperationException(
                    "InvokeActionEffect self-invoke must lower to Ast; EffectExecutor is not the shipped path.");
            _instance.ExecuteInvokeEffect(invoke);
            return null;
        }

        protected override object? ForEachInvoke(ForEachInvokeEffect efe) {
            _instance.ExecuteForEachInvoke(efe);
            return null;
        }
    }

    /// <summary>
    /// Resolves <paramref name="targetExpr"/> to a <see cref="DomainEntityInstance"/> and
    /// records an instance link (source = this, target = resolved) in the store.
    /// Target must be a <see cref="PropertyAccess"/> whose current value is a
    /// <see cref="DomainEntityInstance"/> (set via property bag or prior effects).
    /// Prefer <see cref="DomainInstanceStore.Link"/> for direct API linking.
    /// </summary>
    private void ExecuteInvokeEffect(InvokeActionEffect invoke) {
        var chainedArgs = EvaluateParameterBindings(invoke.ParameterBindings);

        ActionInvocationResult nestedResult;
        if (invoke.TargetRelationship is not null) {
            var target = ResolveRelationshipTarget(invoke.TargetRelationship);
            nestedResult = target.InvokeAction(invoke.ActionName, chainedArgs);
        }
        else {
            nestedResult = InvokeAction(invoke.ActionName, chainedArgs);
        }
        if (!nestedResult.Succeeded) {
            throw new InvalidOperationException(
                nestedResult.ErrorMessage
                ?? (nestedResult.FailedGuards.Count > 0
                    ? $"invoke '{invoke.ActionName}' blocked by guards: {string.Join(", ", nestedResult.FailedGuards)}"
                    : $"invoke '{invoke.ActionName}' failed."));
        }
    }

    /// <summary>
    /// Executes a <see cref="ForEachInvokeEffect"/>: fetch every outbound linked record on
    /// the relationship (fail if none), apply the named-policy / stage predicate, and invoke
    /// the action on each matching record — fail-fast on the first failure.
    /// </summary>
    private void ExecuteForEachInvoke(ForEachInvokeEffect efe) {
        var targets = GetOutboundRelatedInstances(efe.RelationshipName);
        if (targets.Count == 0) {
            throw new InvalidOperationException(
                $"for '{efe.RelationshipName}.{efe.ActionName}' matched zero targets.");
        }

        var matched = false;
        foreach (var target in targets) {
            if (!ForEachPredicateMatches(efe.Predicate, target)) continue;
            matched = true;

            // Bind the binder name to the current target so args like `line Qty` resolve.
            var boundBindings = efe.ParameterBindings
                .Select(b => b with { Expression = BindPeerInExpression(b.Expression, efe.BinderName, target) })
                .ToList();
            var args = EvaluateParameterBindings(boundBindings);

            var result = target.InvokeAction(efe.ActionName, args);
            if (!result.Succeeded) {
                throw new InvalidOperationException(
                    $"for '{efe.RelationshipName}.{efe.ActionName}' failed on a '{target.Entity.Name}' record: " +
                    (result.ErrorMessage ?? "action failed."));
            }
        }
        if (!matched) {
            throw new InvalidOperationException(
                $"for '{efe.RelationshipName}.{efe.ActionName}' matched zero targets after predicate.");
        }
    }

    private bool ForEachPredicateMatches(ForEachPredicate? predicate, DomainEntityInstance target) {
        switch (predicate) {
            case null:
                return true;
            case ForEachNamedPolicy { PolicyName: var policyName }:
                var policy = target.Entity.Policies.FirstOrDefault(p =>
                    string.Equals(p.Name, policyName, StringComparison.Ordinal));
                if (policy is null)
                    throw new InvalidOperationException(
                        $"ForEachInvoke predicate policy '{policyName}' does not exist on entity '{target.Entity.Name}'.");
                return target.EvaluatePolicy(policy);
            case ForEachStageMembership { StageName: var stageName }:
                return string.Equals(target.CurrentStage, stageName, StringComparison.Ordinal);
            default:
                throw new InvalidOperationException($"Unsupported ForEachInvoke predicate '{predicate.GetType().Name}'.");
        }
    }

    /// <summary>
    /// Instance method invoked from the lowered StageTransition tree
    /// (<c>Invoke(Member(This, "Notify"), stageName)</c>) after a stage
    /// assignment. Store subscription fan-out only — does not re-run exit/entry
    /// (those belong in the lowered tree). Skips when executing a subscription
    /// (cascade is store-owned) or when no store is attached. Not a CallExternal
    /// host ABI.
    /// </summary>
    public void Notify(string targetStageName) {
        if (Store is not null && !_isExecutingSubscription)
            Store.NotifyTransition(this, targetStageName);
    }

    /// <summary>
    /// Transitions to a target stage. Execution order:
    /// <list type="number">
    ///   <item>OnExit effects on the current stage (before any state change).</item>
    ///   <item>Set <see cref="CurrentStage"/> to <paramref name="targetStageName"/>.</item>
    ///   <item>OnEntry effects on the target stage (stage already set — partial-entry state
    ///     possible if an effect throws).</item>
    ///   <item>Notify store subscribers (in a <c>finally</c> block — fires even if OnEntry throws).</item>
    /// </list>
    /// Store notification fires when:
    /// <list type="bullet">
    ///   <item><paramref name="notifyStore"/> is <c>true</c>,</item>
    ///   <item><c>Store</c> is set,</item>
    ///   <item>we are <b>not</b> inside a <see cref="ExecuteSubscriptionEffects"/> call
    ///     (subscription-triggered transitions cascade through <see cref="DomainInstanceStore.NotifyTransition"/>
    ///     recursion, not through a second store call).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <b>Stage policy vs action hierarchy:</b> Stage-scoped policies are evaluated only on the
    /// <b>current</b> stage (not the parent chain), while <see cref="InvokeAction"/> walks the
    /// parent chain for actions. This asymmetry exists because effective-policy computation
    /// is performed by analyzers (walking the hierarchy), while runtime scenario gating is
    /// still a per-stage concern.
    ///
    /// <b>OnEntry re-entrancy:</b> Nested same-instance transitions (e.g. OnEntry →
    /// another <c>TransitionStage</c>) are bounded by <see cref="MaxTransitionDepth"/>
    /// (default 16). Exceeding it throws <see cref="InvalidOperationException"/>.
    /// Partial stage application is possible if a nested throw occurs after
    /// <c>CurrentStage</c> was already updated. Store subscription fan-out remains
    /// separately bounded by <see cref="DomainInstanceStore"/> cascade <c>maxDepth</c>.
    /// </remarks>
    internal void TransitionStage(string targetStageName, bool notifyStore = true) {
        if (!Entity.Stages.Any(s => string.Equals(s.Name, targetStageName, StringComparison.Ordinal)))
            return;

        var previousStageName = CurrentStage;
        if (string.Equals(previousStageName, targetStageName, StringComparison.Ordinal))
            return;

        if (_transitionDepth >= MaxTransitionDepth)
            throw new InvalidOperationException(
                $"Stage transition re-entrancy exceeded max depth ({MaxTransitionDepth}) on entity '{Entity.Name}'.");

        _transitionDepth++;
        try {
            // Domain-bound: structure metadata + analysis-aware effect lowering.
            // Standalone: Entity.Stages scan (reduced contract).
            AnalysisResult? analysis = null;
            if (Domain is not null) {
                analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
                if (analysis.GetCatalog(Domain) is null)
                    throw new InvalidOperationException(
                        $"Runtime transition requires {nameof(DomainCatalogMetadata)} for domain '{Domain.Name}' (TransitionStage).");
            }

            var subject = new Parameter("entity", new TypeReference(Entity.Name));
            var loweringContext = new LoweringContext(
                subject,
                Analysis: analysis,
                Domain: Domain);

            // ── Run OnExit effects on the current stage ────────────
            if (previousStageName is not null) {
                var prevStage = ResolveTransitionStage(analysis, previousStageName);
                if (prevStage?.OnExitEffects is { Count: > 0 }) {
                    var exitPass = new EffectLoweringPass(Entity, loweringContext);
                    foreach (var effect in prevStage.OnExitEffects)
                        RunTransitionEffect(effect, exitPass, notifyStore);
                }
            }

            // ── Set new stage ──────────────────────────────────────
            CurrentStage = targetStageName;

            // ── Run OnEntry effects on the target stage ────────────
            // Notify subscribers runs in a finally block so it fires even if
            // OnEntry effects throw (the stage is already set).
            try {
                var targetStage = ResolveTransitionStage(analysis, targetStageName);
                if (targetStage?.OnEntryEffects is { Count: > 0 }) {
                    var entryPass = new EffectLoweringPass(Entity, loweringContext);
                    foreach (var effect in targetStage.OnEntryEffects)
                        RunTransitionEffect(effect, entryPass, notifyStore);
                }
            }
            finally {
                if (notifyStore && Store is not null && !_isExecutingSubscription) {
                    Store.NotifyTransition(this, targetStageName);
                }
            }
        }
        finally {
            _transitionDepth--;
        }
    }

    /// <summary>
    /// Nested <see cref="StageTransitionEffect"/> in entry/exit must recurse through
    /// <see cref="TransitionStage"/> so depth, intermediate exits, and OnEntry chains
    /// stay on the runtime helper. Flattening them into one VM tree skips the bound.
    /// </summary>
    private void RunTransitionEffect(Effect effect, EffectLoweringPass pass, bool notifyStore) {
        if (effect is StageTransitionEffect nested) {
            TransitionStage(nested.TargetStage.StageName, notifyStore);
            return;
        }
        ExecuteEffect(effect, pass, _typeDefAnalyzer);
    }

    /// <summary>
    /// Domain-bound: resolve stage via ESM (fail closed on miss). Standalone: structural scan.
    /// </summary>
    private Stage? ResolveTransitionStage(AnalysisResult? analysis, string stageName) {
        if (analysis is not null) {
            if (!analysis.TryGetStage(Entity, stageName, out var stage) || stage is null)
                throw new InvalidOperationException(
                    $"Stage '{stageName}' not resolvable for entity '{Entity.Name}' during transition " +
                    $"(requires {nameof(EntityStructureMetadata)}).");
            return stage;
        }

        if (Domain is null) {
            return Entity.Stages.FirstOrDefault(
                s => string.Equals(s.Name, stageName, StringComparison.Ordinal));
        }

        return null;
    }

    /// <summary>
    /// Executes subscription effects in this instance's context (subscriber).
    /// <paramref name="peerInstance"/> is the related entity that transitioned.
    /// When <paramref name="peerBinding"/> is set (<c>when Rel Stage as name</c>),
    /// path-prefix roots equal to that name resolve against the peer bag before
    /// lowering (notification-only subscriptions omit the binder).
    /// Called by <see cref="DomainInstanceStore.NotifyTransition"/>.
    ///
    /// Subscription-triggered transitions suppress store notification via
    /// <c>_isExecutingSubscription</c> — cascading is handled by the store's
    /// depth-limited recursion instead.
    /// </summary>
    internal void ExecuteSubscriptionEffects(
        IReadOnlyList<Effect> effects,
        DomainEntityInstance peerInstance,
        string? peerBinding = null) {
        _isExecutingSubscription = true;

        try {
            var subjectParam = new Parameter("entity", new TypeReference(Entity.Name));
            EffectLoweringPass effectPass;
            if (Domain is not null) {
                var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
                effectPass = new EffectLoweringPass(Entity, new LoweringContext(
                    subjectParam,
                    Analysis: analysis,
                    Domain: Domain));
            }
            else {
                effectPass = new EffectLoweringPass(Entity, subjectParam);
            }

            foreach (var effect in effects) {
                var bound = peerBinding is { Length: > 0 }
                    ? BindPeerInEffect(effect, peerBinding, peerInstance)
                    : effect;
                ExecuteEffect(bound, effectPass, _typeDefAnalyzer);
            }
        }
        finally {
            _isExecutingSubscription = false;
        }
    }

    /// <summary>
    /// Rewrites peer path-prefix roots (<c>name Prop</c> → <see cref="RelationshipNavigation"/>)
    /// into literals evaluated against the transitioned peer bag.
    /// </summary>
    private static Effect BindPeerInEffect(Effect effect, string peerBinding, DomainEntityInstance peer) {
        return effect switch {
            AssignEffect a => a with {
                // Peer path-prefix is value-side only (F4/F16 defense in depth).
                Target = RejectPeerAssignTarget(a.Target, peerBinding),
                Value = BindPeerInExpression(a.Value, peerBinding, peer)
            },
            ConditionalEffect c => c with {
                Condition = BindPeerInExpression(c.Condition, peerBinding, peer),
                ThenEffects = c.ThenEffects.Select(e => BindPeerInEffect(e, peerBinding, peer)).ToList(),
                ElseEffects = c.ElseEffects?.Select(e => BindPeerInEffect(e, peerBinding, peer)).ToList()
            },
            CompositeEffect c => c with {
                Effects = c.Effects.Select(e => BindPeerInEffect(e, peerBinding, peer)).ToList()
            },
            CreateEntityInstance cei => cei with {
                Initializers = cei.Initializers
                    .Select(i => i with { Expression = BindPeerInExpression(i.Expression, peerBinding, peer) })
                    .ToList()
            },
            CreateEntityInRelationshipEffect cir => cir with {
                Initializers = cir.Initializers
                    .Select(i => i with { Expression = BindPeerInExpression(i.Expression, peerBinding, peer) })
                    .ToList()
            },
            InvokeActionEffect iae => iae with {
                ParameterBindings = iae.ParameterBindings
                    .Select(b => b with { Expression = BindPeerInExpression(b.Expression, peerBinding, peer) })
                    .ToList()
            },
            _ => effect
        };
    }

    private static DomainExpression RejectPeerAssignTarget(DomainExpression target, string peerBinding) {
        if (target is RelationshipNavigation rn
            && string.Equals(rn.RelationshipName, peerBinding, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Peer binder '{peerBinding}' cannot be an assign target in a subscription effect. " +
                "Use peer fields only on the right-hand side.");
        }
        return target;
    }

    private static DomainExpression BindPeerInExpression(
        DomainExpression expr, string peerBinding, DomainEntityInstance peer) =>
        new PeerBindingRewrite(peerBinding, peer).Route(expr);

    /// <summary>
    /// Rewrites peer path-prefix roots (<c>name Prop</c>) into literals evaluated
    /// against the transitioned peer bag (coh-d1 — leaf override on the shared
    /// <see cref="DomainExpressionRewriteBase"/>; composites recurse in the base).
    /// </summary>
    private sealed class PeerBindingRewrite(string peerBinding, DomainEntityInstance peer)
        : DomainExpressionRewriteBase {
        protected override DomainExpression RelationshipNavigation(RelationshipNavigation e) {
            if (string.Equals(e.RelationshipName, peerBinding, StringComparison.Ordinal))
                return DomainExpression.Literal(EvaluateExprOnPeer(e.TargetProperty, peer));
            return base.RelationshipNavigation(e);
        }
    }

    /// <summary>
    /// Lowers and executes <paramref name="expr"/> against the peer instance bag.
    /// </summary>
    private static object? EvaluateExprOnPeer(DomainExpression expr, DomainEntityInstance peer) {
        var pass = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity")));
        var lowered = pass.Lower(expr,
            new Parameter("entity", new TypeReference(peer.Entity.Name)));
        var compiled = Interpreter.Compile(lowered, peer._typeDefAnalyzer);
        using var exec = Interpreter.Execute(compiled,
            s => s.SetArgs(new object?[] { peer }));
        return exec.Result.GetValue<object>();
    }

    /// <summary>
    /// Creates a child entity instance from a <see cref="CreateEntityInstance"/>
    /// effect. Looks up the target entity by type name — first from the parent
    /// <see cref="Domain"/> if available, otherwise falls back to the current
    /// entity (same-type creation). Initializer expressions are evaluated
    /// against the <em>parent</em> instance and bound to the child's properties.
    /// </summary>
    private DomainEntityInstance CreateChildInstance(
        CreateEntityInstance createEffect,
        TypeDefinitionNodeAnalyzer? parentTypeProvider = null) {
        var targetTypeName = createEffect.Type.TypeName;

        // Resolve analysis once for the whole creation (F21).
        var analysis = Domain is not null ? RuntimeAnalysisCache.GetOrAnalyze(Domain) : null;

        // Resolve target entity definition via catalog/DTLM.
        // With analysis present a miss is a genuine not-found — fail closed.
        Entity targetEntity;
        if (analysis is not null) {
            targetEntity = analysis.TryGetEntity(Domain!, targetTypeName, out var resolvedEntity)
                ? resolvedEntity!
                : throw new InvalidOperationException(
                    $"Entity type '{targetTypeName}' not found in domain '{Domain!.Name}'.");
        }
        else {
            targetEntity = Entity; // same-type creation when no domain reference
        }

        // Evaluate initializers against the parent instance. Use the action-scoped type
        // provider (entity props + action parameters) when available — the instance-level
        // analyzer lacks action params, so `Capacity: qty` compiled with it would resolve
        // the parameter as an unresolved member passthrough (garbage value).
        var initializerTypeProvider = parentTypeProvider ?? _typeDefAnalyzer;
        var initialValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var binding in createEffect.Initializers) {
            var lowered = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity"))).Lower(
                binding.Expression,
                new Parameter("entity", new TypeReference(Entity.Name)));
            var compiled = Interpreter.Compile(lowered, initializerTypeProvider);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { this }));
            initialValues[binding.PropertyName] = exec.Result.GetValue<object>();
        }

        var child = Create(targetEntity, initialValues, Domain);
        _createdChildren.Add(child);

        // BR.3.3: Auto-register child in the parent's store, if present.
        Store?.Add(child);

        // P2.1 / P2′.3 / P2′′′.3: Auto-link child to creator if the effect specifies a relationship name.
        // Link direction: creator (this) = source, child = target.
        // If Domain is available, validate the relationship exists, source entity, and target type.
        // If Domain is null, link is best-effort (standalone instance).
        if (createEffect.RelationshipName is not null && Store is not null) {
            if (analysis is not null) {
                // Catalog/RLM miss with analysis present is a genuine not-found — fail closed.
                var relationship = ResolveSourceRelationshipOrThrow(createEffect.RelationshipName,
                    $"Relationship '{createEffect.RelationshipName}' not found in domain '{Domain!.Name}'.");
                // Verify created type matches relationship target
                if (!string.Equals(targetEntity.Name, relationship.Target.TypeName, StringComparison.Ordinal)) {
                    throw new InvalidOperationException(
                        $"CreateEntityInstance creates type '{targetEntity.Name}' but relationship " +
                        $"'{createEffect.RelationshipName}' targets '{relationship.Target.TypeName}'.");
                }
            }
            Store.Link(createEffect.RelationshipName, this, child);
        }

        return child;
    }

    /// <summary>
    /// Executes a <see cref="CreateEntityInRelationshipEffect"/>: resolves the target
    /// entity type from the relationship definition on the domain, creates the instance,
    /// auto-registers it, and links it via the named relationship.
    /// Returns the created <see cref="DomainEntityInstance"/>.
    /// </summary>
    private DomainEntityInstance ExecuteCreateInRelationship(
        CreateEntityInRelationshipEffect effect,
        TypeDefinitionNodeAnalyzer? parentTypeProvider = null) {
        if (Domain is null)
            throw new InvalidOperationException(
                "Cannot execute 'create in' effect without a domain to resolve relationship targets.");

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        // Catalog/RLM miss with analysis present is a genuine not-found — fail closed.
        // ResolveSourceRelationshipOrThrow also reports the precise cause when the
        // relationship exists on a different source entity.
        var relationship = ResolveSourceRelationshipOrThrow(effect.RelationshipName,
            $"Relationship '{effect.RelationshipName}' not found in domain '{Domain.Name}'.");

        // Catalog/DTLM miss with analysis present is a genuine not-found — fail closed.
        if (!analysis.TryGetEntity(Domain, relationship.Target.TypeName, out var targetEntity)
            || targetEntity is null)
            throw new InvalidOperationException(
                $"Target entity '{relationship.Target.TypeName}' for relationship '{effect.RelationshipName}' not found.");

        // Wrap into a CreateEntityInstance with the relationship name for auto-linking
        var createEffect = new CreateEntityInstance(
            new DomainTypeReference(targetEntity.Name),
            effect.Initializers,
            effect.RelationshipName);

        return CreateChildInstance(createEffect, parentTypeProvider);
    }
}