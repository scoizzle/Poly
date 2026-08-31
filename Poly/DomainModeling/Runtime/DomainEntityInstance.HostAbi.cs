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
    /// Residual dispatcher. All product arms throw: create / create-in execute
    /// via CreateChildInstance / ExecuteCreateInRelationship (probes+Failure).
    /// Invoke, stage, and for must lower to Ast.
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

        protected override object? CreateEntityInstance(CreateEntityInstance create) =>
            throw new InvalidOperationException(
                "CreateEntityInstance must not use EffectExecutor; probes+Failure and CreateChildInstance are the shipped path.");

        protected override object? CreateEntityInRelationship(CreateEntityInRelationshipEffect createIn) =>
            throw new InvalidOperationException(
                "CreateEntityInRelationship must not use EffectExecutor; probes+Failure and ExecuteCreateInRelationship are the shipped path.");

        protected override object? InvokeAction(InvokeActionEffect invoke) =>
            throw new InvalidOperationException(
                "InvokeActionEffect must lower to Ast; EffectExecutor is not the shipped path.");

        protected override object? ForEachInvoke(ForEachInvokeEffect efe) =>
            throw new InvalidOperationException(
                "ForEachInvokeEffect must lower to Ast; EffectExecutor is not the shipped path.");
    }

    /// <summary>
    /// Real instance method invoked from the lowered StageTransition tree
    /// (<c>Invoke(Member(This, "Notify"), stageName)</c>) after a stage
    /// assignment. Store subscription fan-out only — does not re-run exit/entry
    /// (those belong in the lowered tree). Skips when executing a subscription
    /// (cascade is store-owned) or when no store is attached.
    /// </summary>
    public void Notify(string targetStageName) {
        if (Store is not null && !_isExecutingSubscription)
            Store.NotifyTransition(this, targetStageName);
    }

    /// <summary>
    /// Leftover helper for nested OnEntry/OnExit depth bounding and test callers.
    /// Action <see cref="StageTransitionEffect"/> must lower via <see cref="ExecuteEffect"/>;
    /// this is not the shipped action path.
    /// When the helper runs, order is OnExit (current stage), set
    /// <see cref="CurrentStage"/>, OnEntry (target; partial-entry if an effect throws),
    /// then store notify in <c>finally</c>. Notify fires when <paramref name="notifyStore"/>
    /// is true, <c>Store</c> is set, and we are not inside
    /// <see cref="ExecuteSubscriptionEffects"/> (subscription cascades through
    /// <see cref="DomainInstanceStore.NotifyTransition"/>, not a second store call).
    /// Nested same-instance transitions are bounded by <see cref="MaxTransitionDepth"/>.
    /// </summary>
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

            if (previousStageName is not null) {
                var prevStage = ResolveTransitionStage(analysis, previousStageName);
                if (prevStage?.OnExitEffects is { Count: > 0 }) {
                    var exitPass = new EffectLoweringPass(Entity, loweringContext);
                    RunTransitionEffectList(prevStage.OnExitEffects, exitPass, notifyStore);
                }
            }

            CurrentStage = targetStageName;

            try {
                var targetStage = ResolveTransitionStage(analysis, targetStageName);
                if (targetStage?.OnEntryEffects is { Count: > 0 }) {
                    var entryPass = new EffectLoweringPass(Entity, loweringContext);
                    RunTransitionEffectList(targetStage.OnEntryEffects, entryPass, notifyStore);
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
    /// Nested entry/exit <see cref="StageTransitionEffect"/> recurses through
    /// <see cref="TransitionStage"/> for depth bounding and test callers.
    /// Action-level stage transitions must lower via <see cref="ExecuteEffect"/>.
    /// </summary>
    private void RunTransitionEffect(Effect effect, EffectLoweringPass pass, bool notifyStore) {
        RunTransitionEffectList([effect], pass, notifyStore);
    }

    /// <summary>
    /// Nested <see cref="StageTransitionEffect"/> still recurses
    /// <see cref="TransitionStage"/>. Mixed if+create in the same entry/exit
    /// list compiles via <c>LowerActionBody</c> (ExecuteStructured was deleted).
    /// </summary>
    private void RunTransitionEffectList(
        IReadOnlyList<Effect> effects, EffectLoweringPass pass, bool notifyStore) {
        var batch = new List<Effect>();
        void Flush() {
            if (batch.Count == 0) return;
            ExecuteEffectList(batch, pass, _typeDefAnalyzer);
            batch.Clear();
        }
        foreach (var effect in effects) {
            if (effect is StageTransitionEffect nested) {
                Flush();
                TransitionStage(nested.TargetStage.StageName, notifyStore);
            }
            else {
                batch.Add(effect);
            }
        }
        Flush();
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

            var bound = effects.Select(effect => peerBinding is { Length: > 0 }
                    ? BindPeerInEffect(effect, peerBinding, peerInstance)
                    : effect)
                .ToList();
            ExecuteEffectList(bound, effectPass, _typeDefAnalyzer);
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
    /// VM-called runtime factories for lowered create / create-in (body and probes).
    /// Args: name (type or relationship), then property name/value pairs.
    /// Probe does not register a child. Stay.Create is C# export only.
    /// </summary>
    private DomainResult RuntimeCreateFactory(string name, object?[] args) {
        if (args.Length < 1 || args[0] is not string key || key.Length == 0)
            return DomainResult.Failure("Create factory requires a type or relationship name.");
        if ((args.Length - 1) % 2 != 0)
            return DomainResult.Failure("Create factory arguments must be name/value pairs.");

        var bindings = new List<PropertyBinding>();
        for (var i = 1; i < args.Length; i += 2) {
            if (args[i] is not string propName)
                return DomainResult.Failure("Create factory property names must be strings.");
            bindings.Add(new PropertyBinding(propName, DomainExpression.Literal(args[i + 1])));
        }

        if (name == "ProbeCreateByType") {
            Entity? target;
            if (Domain is not null) {
                var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
                if (!analysis.TryGetEntity(Domain, key, out target) || target is null)
                    return DomainResult.Failure(
                        $"Entity type '{key}' not found in domain '{Domain.Name}'.");
            }
            else {
                target = Entity;
            }
            var err = PrevalidateCreateInitializers(bindings, target);
            return err is null ? DomainResult.Success() : DomainResult.Failure(err);
        }

        try {
            if (name == "CreateInNav") {
                var child = ExecuteCreateInRelationship(
                    new CreateEntityInRelationshipEffect(key, bindings),
                    _bindingTypeProvider ?? _typeDefAnalyzer);
                return DomainResult.Success(child);
            }
            var created = CreateChildInstance(
                new CreateEntityInstance(new DomainTypeReference(key), bindings),
                _bindingTypeProvider ?? _typeDefAnalyzer);
            return DomainResult.Success(created);
        }
        catch (InvalidOperationException ex) {
            return DomainResult.Failure(ex.Message);
        }
        catch (ArgumentException ex) {
            return DomainResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Parking dogfood: <c>assign Occupied + 1</c> then <c>create in</c> left Occupied
    /// bumped when Plate failed the pattern. Unconditional create/create-in and
    /// creates on a taken <c>if</c> branch are constraint-checked before any effect runs.
    /// Taken-ness is evaluated on the pre-effect bag: a condition that reads a
    /// property assigned earlier in the same action is not re-eval'd, so that
    /// assign still applies when the post-assign bag would take an illegal create.
    /// Untaken then/else branches are not probed (illegal initializer on an untaken
    /// branch must not fail the action).
    /// </summary>
    private string? PrevalidateUnconditionalCreates(IReadOnlyList<Effect> effects) {
        foreach (var effect in effects) {
            switch (effect) {
                case CompositeEffect composite: {
                        var nested = PrevalidateUnconditionalCreates(composite.Effects);
                        if (nested is not null)
                            return nested;
                        break;
                    }
                case ConditionalEffect cond: {
                        // Only ifs that contain create/create-in. Taken-branch creates
                        // fail closed before any prior assign. Untaken branch is not probed.
                        if (!ContainsDirectExecutionEffect(cond))
                            break;
                        if (!TryEvalEffectCondition(PreprocessQuantifiers(cond.Condition), out var taken))
                            return "Cannot evaluate if condition for create prevalidation.";
                        var branch = taken ? cond.ThenEffects : (cond.ElseEffects ?? []);
                        var nested = PrevalidateUnconditionalCreates(branch);
                        if (nested is not null)
                            return nested;
                        break;
                    }
                case CreateEntityInRelationshipEffect createIn: {
                        if (Domain is null)
                            break;
                        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
                        var relationship = ResolveSourceRelationshipOrThrow(createIn.RelationshipName,
                            $"Relationship '{createIn.RelationshipName}' not found in domain '{Domain.Name}'.");
                        if (!analysis.TryGetEntity(Domain, relationship.Target.TypeName, out var target)
                            || target is null)
                            return $"Target entity '{relationship.Target.TypeName}' not found.";
                        var err = PrevalidateCreateInitializers(createIn.Initializers, target);
                        if (err is not null)
                            return err;
                        break;
                    }
                case CreateEntityInstance create: {
                        if (Domain is null)
                            break;
                        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
                        if (!analysis.TryGetEntity(Domain, create.Type.TypeName, out var target)
                            || target is null)
                            return $"Entity type '{create.Type.TypeName}' not found in domain '{Domain.Name}'.";
                        var err = PrevalidateCreateInitializers(create.Initializers, target);
                        if (err is not null)
                            return err;
                        break;
                    }
            }
        }

        return null;
    }

    private string? PrevalidateCreateInitializers(
        IReadOnlyList<PropertyBinding> initializers, Entity targetEntity) {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var scalarNames = new HashSet<string>(
            targetEntity.Properties.Select(p => p.Name), StringComparer.Ordinal);
        foreach (var binding in initializers) {
            if (!scalarNames.Contains(binding.PropertyName))
                continue;
            if (TryEvalActionParamPath(binding.Expression, out var fromParam)) {
                values[binding.PropertyName] = fromParam;
                continue;
            }
            var lowered = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity"))).Lower(
                binding.Expression,
                new Parameter("entity", new TypeReference(Entity.Name)));
            var compiled = Interpreter.Compile(lowered, _bindingTypeProvider ?? _typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { this }));
            values[binding.PropertyName] = exec.Result.GetValue<object>();
        }

        foreach (var prop in targetEntity.Properties) {
            if (values.ContainsKey(prop.Name))
                continue;
            if (prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault() is { } def)
                values[prop.Name] = EvaluateDefaultValue(def.Expression, prop.Type.TypeName, Domain);
            else
                values[prop.Name] = null;
        }

        return ValidateConstraints(targetEntity, values);
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
        var navValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var scalarNames = new HashSet<string>(
            targetEntity.Properties.Select(p => p.Name), StringComparer.Ordinal);
        var singularNavs = targetEntity.Navigations
            .Where(n => n.Cardinality is not (RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany))
            .Select(n => n.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var binding in createEffect.Initializers) {
            object? value;
            if (TryEvalActionParamPath(binding.Expression, out var fromParam)) {
                value = fromParam;
            }
            else {
                var lowered = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity"))).Lower(
                    binding.Expression,
                    new Parameter("entity", new TypeReference(Entity.Name)));
                var compiled = Interpreter.Compile(lowered, initializerTypeProvider);
                using var exec = Interpreter.Execute(compiled,
                    s => s.SetArgs(new object?[] { this }));
                value = exec.Result.GetValue<object>();
            }
            if (scalarNames.Contains(binding.PropertyName))
                initialValues[binding.PropertyName] = value;
            else if (singularNavs.Contains(binding.PropertyName))
                navValues[binding.PropertyName] = value;
            else
                throw new ArgumentException(
                    $"Property '{binding.PropertyName}' does not exist on entity '{targetEntity.Name}'. " +
                    $"Available: {string.Join(", ", scalarNames)}.");
        }

        var child = Create(targetEntity, initialValues, Domain);
        _createdChildren.Add(child);

        // BR.3.3: Auto-register child in the parent's store, if present.
        if (Store is not null && !Store.TryAdd(child, out var addError)) {
            _createdChildren.Remove(child);
            throw new InvalidOperationException(addError);
        }

        if (Store is not null) {
            foreach (var (navName, raw) in navValues) {
                if (raw is not DomainEntityInstance linked)
                    throw new InvalidOperationException(
                        $"Create-in initializer '{navName}' on '{targetEntity.Name}' must resolve to a linked instance.");
                if (!ReferenceEquals(linked.Store, Store)
                    && !Store.TryAdd(linked, out var linkAddError)) {
                    _createdChildren.Remove(child);
                    throw new InvalidOperationException(linkAddError);
                }
                Store.Link(navName, child, linked);
                TryLinkInverseCollection(linked, child);
            }
        }

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
            TryLinkCreateInBackReference(child);
        }

        return child;
    }

    /// <summary>
    /// After a create-in to-one initializer (<c>section: offering</c>), also link the
    /// peer's unique collection of this child type (<c>Section.enrollments</c>). Skip
    /// when zero or several collections match — ambiguous inverses stay explicit.
    /// </summary>
    /// <summary>
    /// <c>lead Name</c> in a create-in initializer: the first hop is an action
    /// parameter holding a <see cref="DomainEntityInstance"/>, not a navigation
    /// on this entity (CRM ConvertLead).
    /// </summary>
    private bool TryEvalActionParamPath(DomainExpression expr, out object? value) {
        value = null;
        if (expr is not RelationshipNavigation { TargetProperty: PropertyAccess leaf } nav)
            return false;
        if (!_values.TryGetValue(nav.RelationshipName, out var raw)
            || raw is not DomainEntityInstance peer)
            return false;
        if (ContainsRelationshipNavigation(leaf))
            return false;
        peer.TryGetRaw(leaf.Name, out value);
        return true;
    }

    private static bool ContainsRelationshipNavigation(DomainExpression expr) =>
        expr is RelationshipNavigation
        || expr.Children.OfType<DomainExpression>().Any(ContainsRelationshipNavigation);

    /// <summary>
    /// After <c>create in opportunities { … }</c>, bind the child's unique to-one
    /// back to this source (<c>Opportunity.account</c>) so Rel-exists policies match
    /// the C# auto-wired back-ref.
    /// </summary>
    private void TryLinkCreateInBackReference(DomainEntityInstance child) {
        if (Store is null) return;
        var backs = child.Entity.Navigations
            .Where(n => n.Cardinality is not (RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany)
                && string.Equals(n.Target.TypeName, Entity.Name, StringComparison.Ordinal))
            .ToList();
        if (backs.Count != 1)
            return;
        Store.Link(backs[0].Name, child, this);
    }

    private void TryLinkInverseCollection(DomainEntityInstance peer, DomainEntityInstance child) {
        if (Store is null) return;
        var inverses = peer.Entity.Navigations
            .Where(n => n.Cardinality is RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany
                && string.Equals(n.Target.TypeName, child.Entity.Name, StringComparison.Ordinal))
            .ToList();
        if (inverses.Count != 1)
            return;
        Store.Link(inverses[0].Name, peer, child);
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