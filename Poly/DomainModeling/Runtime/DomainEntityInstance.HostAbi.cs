using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;

using Action = Poly.DomainModeling.Ontology.Action;
using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Runtime;

public sealed partial record DomainEntityInstance {
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
    /// VM-callable Store bind for unique assign (Notify-shaped). Dictionary-backed
    /// <c>This</c> cannot Member-read <see cref="Store"/>; the lowered tree invokes
    /// this method. No Store bound means no peers — Success, then the assign proceeds.
    /// </summary>
    public DomainResult EnsureUnique(string propertyName, object? value) {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        if (Store is null)
            return DomainResult.Success();
        return Store.EnsureUnique(this, propertyName, value);
    }

    /// <summary>
    /// Notify-shaped Store read: true when the named relationship has any
    /// outbound link. Dictionary <c>This</c> cannot Member-read <see cref="Store"/>.
    /// </summary>
    public bool ExistsRelated(string relationshipName) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        if (TryEvaluateRelationshipPresence(new PropertyAccess(relationshipName), out var present))
            return present;
        throw new InvalidOperationException(
            Domain is null
                ? $"Cannot resolve relationship '{relationshipName}' without a domain."
                : $"Relationship '{relationshipName}' not found in domain '{Domain.Name}'.");
    }

    /// <summary>
    /// Notify-shaped Store read: the unique outbound target of a to-one hop.
    /// Zero or many links fail closed (path-prefix contract).
    /// </summary>
    public DomainEntityInstance GetRelatedOne(string relationshipName) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        var targets = GetOutboundRelatedInstances(relationshipName);
        if (targets.Count == 0)
            throw new InvalidOperationException(
                $"No linked instances found for relationship '{relationshipName}' on entity '{Entity.Name}'.");
        if (targets.Count > 1)
            throw new InvalidOperationException(
                $"Path-prefix on relationship '{relationshipName}' requires exactly one linked target " +
                $"(found {targets.Count} on entity '{Entity.Name}'). Use any/all quantifiers for collections.");
        return targets[0];
    }

    /// <summary>
    /// Notify-shaped Store bind: link a just-created child. No Store is a no-op
    /// (CreateEntityInstance.RelationshipName without a store still allocates).
    /// Unknown relationship with a Store bound fails loud.
    /// </summary>
    public void LinkRelated(string relationshipName, object? target) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        if (Store is null)
            return;
        if (target is not DomainEntityInstance child)
            throw new InvalidOperationException(
                $"LinkRelated target must be a domain instance, got {target?.GetType().Name ?? "null"}.");
        var relationship = ResolveCreateInRelationship(relationshipName);
        if (!string.Equals(child.Entity.Name, relationship.Target.TypeName, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"CreateEntityInstance creates type '{child.Entity.Name}' but relationship " +
                $"'{relationshipName}' targets '{relationship.Target.TypeName}'.");
        }
        Store.Link(relationshipName, this, child);
        TryLinkCreateInBackReference(child);
    }

    public bool AnyRelated(string relationshipName, object? body) =>
        EvaluateAnyExpr(new AnyExpr(relationshipName, RequirePredicate(body, "AnyRelated")));

    public bool AllRelated(string relationshipName, object? body) =>
        EvaluateAllExpr(new AllExpr(relationshipName, RequirePredicate(body, "AllRelated")));

    public bool NoneRelated(string relationshipName, object? body) =>
        EvaluateNoneExpr(new NoneExpr(relationshipName, RequirePredicate(body, "NoneRelated")));

    public long CountRelated(string relationshipName, object? body) =>
        EvaluateCountExpr(body is null
            ? new CountExpr(relationshipName, Body: null)
            : new CountExpr(relationshipName, RequirePredicate(body, "CountRelated")));

    private static DomainExpression RequirePredicate(object? body, string job) {
        if (body is DomainExpression expr)
            return expr;
        throw new InvalidOperationException(
            $"{job} predicate must be a domain expression, got {body?.GetType().Name ?? "null"}.");
    }

    private static bool IsConstraintFailureMessage(string message) =>
        message.Contains("Unique", StringComparison.Ordinal)
        || message.Contains("required", StringComparison.Ordinal)
        || message.Contains("pattern", StringComparison.Ordinal)
        || message.Contains("does not exist on entity", StringComparison.Ordinal);

    /// <summary>
    /// Leftover helper for nested OnEntry/OnExit depth bounding and test callers.
    /// Action <see cref="StageTransitionEffect"/> lowers via <see cref="ExecuteEffectList"/>;
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
            ThrowIfEffectListFailed(
                ExecuteEffectList(batch, pass, _typeDefAnalyzer),
                "stage entry/exit");
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
            ThrowIfEffectListFailed(
                ExecuteEffectList(bound, effectPass, _typeDefAnalyzer),
                "subscription");
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
    /// VM-called Store jobs for lowered create / create-in (body and probes).
    /// Args: name (type or relationship) plus an initializer dictionary, or
    /// flattened name/value pairs. Probe does not register a child.
    /// </summary>
    private DomainResult RuntimeCreateFactory(string name, object?[] args) {
        if (args.Length < 1 || args[0] is not string key || key.Length == 0)
            return DomainResult.Failure("Create factory requires a type or relationship name.");

        Dictionary<string, object?> values;
        if (args.Length >= 2 && TryReadCreateValues(args[1], out var fromDict)) {
            values = fromDict;
        }
        else {
            if ((args.Length - 1) % 2 != 0)
                return DomainResult.Failure("Create factory arguments must be name/value pairs.");
            values = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i += 2) {
                if (args[i] is not string propName)
                    return DomainResult.Failure("Create factory property names must be strings.");
                values[propName] = args[i + 1];
            }
        }

        try {
            return name switch {
                "ProbeCreate" => ProbeCreate(key, values),
                "CreateIn" => CreateIn(key, values),
                "Create" => Create(key, values),
                _ => DomainResult.Failure($"Unknown create job '{name}'.")
            };
        }
        catch (InvalidOperationException ex) when (IsConstraintFailureMessage(ex.Message)) {
            return DomainResult.Failure(ex.Message);
        }
        catch (ArgumentException ex) {
            return DomainResult.Failure(ex.Message);
        }
    }

    private static bool TryReadCreateValues(object? arg, out Dictionary<string, object?> values) {
        values = new Dictionary<string, object?>(StringComparer.Ordinal);
        switch (arg) {
            case IReadOnlyDictionary<string, object?> typed:
                foreach (var kv in typed)
                    values[kv.Key] = kv.Value;
                return true;
            case IDictionary<string, object?> typed2:
                foreach (var kv in typed2)
                    values[kv.Key] = kv.Value;
                return true;
            case System.Collections.IDictionary untyped:
                foreach (System.Collections.DictionaryEntry entry in untyped) {
                    if (entry.Key is string key)
                        values[key] = entry.Value;
                }
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Notify-shaped Store bind for by-type create. Dictionary <c>This</c> cannot
    /// Member-read <see cref="Store"/>.
    /// </summary>
    public DomainResult Create(string typeName, IReadOnlyDictionary<string, object?> values) {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(values);
        if (Store is not null)
            return Store.Create(this, typeName, values);
        var bindings = ValuesAsLiteralBindings(values);
        var created = CreateChildInstance(
            new CreateEntityInstance(new DomainTypeReference(typeName), bindings),
            _bindingTypeProvider ?? _typeDefAnalyzer);
        return DomainResult.Success(created);
    }

    /// <summary>
    /// Notify-shaped Store bind for create-in (allocate, register, link).
    /// </summary>
    public DomainResult CreateIn(string relationshipName, IReadOnlyDictionary<string, object?> values) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(values);
        if (Store is not null)
            return Store.CreateIn(this, relationshipName, values);
        var bindings = ValuesAsLiteralBindings(values);
        var child = ExecuteCreateInRelationship(
            new CreateEntityInRelationshipEffect(relationshipName, bindings),
            _bindingTypeProvider ?? _typeDefAnalyzer);
        return DomainResult.Success(child);
    }

    /// <summary>
    /// Constraint probe without allocating. Fail-before-mutate prefix in the lowered tree.
    /// </summary>
    public DomainResult ProbeCreate(string typeName, IReadOnlyDictionary<string, object?> values) {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(values);
        if (Store is not null)
            return Store.ProbeCreate(this, typeName, values);
        Entity? target;
        if (Domain is not null) {
            var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
            if (!analysis.TryGetEntity(Domain, typeName, out target) || target is null)
                return DomainResult.Failure(
                    $"Entity type '{typeName}' not found in domain '{Domain.Name}'.");
        }
        else {
            target = Entity;
        }
        var err = PrevalidateCreateInitializers(ValuesAsLiteralBindings(values), target);
        return err is null ? DomainResult.Success() : DomainResult.Failure(err);
    }

    private static List<PropertyBinding> ValuesAsLiteralBindings(
        IReadOnlyDictionary<string, object?> values) {
        var bindings = new List<PropertyBinding>();
        foreach (var (name, value) in values)
            bindings.Add(new PropertyBinding(name, DomainExpression.Literal(value)));
        return bindings;
    }

    /// <summary>
    /// Parking dogfood: <c>assign Occupied + 1</c> then <c>create in</c> left Occupied
    /// bumped when Plate failed the pattern. Unconditional create/create-in probes live
    /// in the lowered tree (<c>ProbeCreate</c>) so Failure happens before prior assigns.
    /// </summary>
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

        return ValidateConstraints(
            targetEntity, FillCreateDefaults(targetEntity, values, Domain), Store);
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

        initialValues = FillCreateDefaults(targetEntity, initialValues, Domain);
        var uniqueOrConstraint = ValidateConstraints(targetEntity, initialValues, Store);
        if (uniqueOrConstraint is not null)
            throw new InvalidOperationException(uniqueOrConstraint);

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
        else if (Store is not null) {
            // Type-create with no RelationshipName: when this source owns exactly one
            // many-rel targeting the created type (e.g. Patron.fines → Fine), auto-link
            // outbound + unambiguous reverse so list_instances and Rel-exists agree.
            // Zero or several matching navs stay explicit (no silent pick).
            TryAutoLinkUnambiguousOutbound(child, targetEntity);
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
    /// After bare <c>create Type</c> (no relationship name), if this source owns
    /// exactly one many-rel targeting that type, link outbound and reverse like create-in.
    /// Ambiguous or absent matches leave the child registered but unlinked.
    /// </summary>
    internal void TryAutoLinkUnambiguousOutbound(DomainEntityInstance child, Entity targetEntity) {
        if (Store is null) return;
        var outs = Entity.Navigations
            .Where(n => (n.Cardinality is RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany)
                && string.Equals(n.Target.TypeName, targetEntity.Name, StringComparison.Ordinal))
            .ToList();
        if (outs.Count != 1)
            return;
        Store.Link(outs[0].Name, this, child);
        TryLinkCreateInBackReference(child);
    }

    /// <summary>
    /// After <c>create in opportunities { … }</c>, bind the child's unique to-one
    /// back to this source (<c>Opportunity.account</c>) so Rel-exists policies match
    /// the C# auto-wired back-ref.
    /// </summary>
    internal void TryLinkCreateInBackReference(DomainEntityInstance child) {
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

    internal void TryLinkInverseCollection(DomainEntityInstance peer, DomainEntityInstance child) {
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