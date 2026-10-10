using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;

using Action = Poly.DomainModeling.Ontology.Action;
using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Runtime;

public sealed partial record DomainEntityInstance {
    /// <summary>
    /// Store subscription fan-out after a stage assignment. Printed classes and
    /// the shared StageTransition lowering call <c>Notify{Stage}Subscribers</c>
    /// for watched stages, which routes here. Skips when no store is attached.
    /// </summary>
    public void Notify(string targetStageName) =>
        Notify(targetStageName, previousStageName: null);

    /// <summary>
    /// Store fan-out after a stage assignment. <paramref name="previousStageName"/>
    /// is the stage left by this transition; All handlers use it as the once-edge.
    /// </summary>
    public void Notify(string targetStageName, string? previousStageName) {
        if (Store is not null)
            Store.NotifyTransition(this, targetStageName, previousStageName: previousStageName);
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

    private static bool IsConstraintFailureMessage(string message) =>
        message.Contains("Unique", StringComparison.Ordinal)
        || message.Contains("required", StringComparison.Ordinal)
        || message.Contains("pattern", StringComparison.Ordinal)
        || message.Contains("does not exist on entity", StringComparison.Ordinal);

    /// <summary>
    /// Loop-guard backstop for automatic stage chains (entry/exit transitions).
    /// Each stage may be entered at most once per trigger; a second entry fails loud
    /// instead of overflowing the stack. Cleared at the start of each trigger: outermost
    /// action invoke, subscription, create, and outermost <see cref="TransitionStage"/>.
    /// </summary>
    public void NoteAutomaticStage(string stageName) {
        ArgumentException.ThrowIfNullOrEmpty(stageName);
        // Same-target confirm (already sitting on this stage) is not a new step.
        if (string.Equals(CurrentStage, stageName, StringComparison.Ordinal))
            return;
        _automaticStageChain ??= new HashSet<string>(StringComparer.Ordinal);
        if (!_automaticStageChain.Add(stageName))
            throw new InvalidOperationException(
                $"Automatic stage transition loop on entity '{Entity.Name}': " +
                $"stage '{stageName}' was already entered in this chain.");
    }

    /// <summary>Clears the automatic-transition visit set for a new trigger.</summary>
    public void ClearAutomaticStageChain() => _automaticStageChain = null;

    /// <summary>
    /// Leftover helper for nested OnEntry/OnExit depth bounding and test callers.
    /// Action <see cref="StageTransitionEffect"/> lowers via <see cref="ExecuteEffectList"/>;
    /// this is not the shipped action path.
    /// When the helper runs, order is OnExit (current stage), set
    /// <see cref="CurrentStage"/>, OnEntry (target; partial-entry if an effect throws),
    /// then store notify in <c>finally</c>. Notify fires when <paramref name="notifyStore"/>
    /// is true and <c>Store</c> is set.
    /// Nested same-instance transitions are bounded by <see cref="MaxTransitionDepth"/>.
    /// </summary>
    internal void TransitionStage(string targetStageName, bool notifyStore = true) {
        if (!Entity.Stages.Any(s => string.Equals(s.Name, targetStageName, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Stage '{targetStageName}' does not exist on entity '{Entity.Name}'.");

        var previousStageName = CurrentStage;
        if (string.Equals(previousStageName, targetStageName, StringComparison.Ordinal))
            return;

        if (_transitionDepth >= MaxTransitionDepth)
            throw new InvalidOperationException(
                $"Automatic stage transition loop on entity '{Entity.Name}' exceeded max depth " +
                $"({MaxTransitionDepth}) while entering '{targetStageName}'.");

        if (_transitionDepth == 0)
            ClearAutomaticStageChain();

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

            if (previousStageName is not null) {
                var prevStage = ResolveTransitionStage(analysis, previousStageName);
                // A transition nested in this exit finds the exit already running: skip it
                // rather than run the same exit again (that recursed without end).
                if (prevStage?.OnExitEffects is { Count: > 0 }
                    && _exitsRunning.Add(previousStageName)) {
                    try {
                        RunTransitionEffectList(
                            prevStage.OnExitEffects, notifyStore,
                            exitStageName: previousStageName);
                    }
                    finally {
                        _exitsRunning.Remove(previousStageName);
                    }
                }
            }

            // Exit may have nested-transitioned to the target already.
            if (string.Equals(CurrentStage, targetStageName, StringComparison.Ordinal))
                return;

            NoteAutomaticStage(targetStageName);
            CurrentStage = targetStageName;

            try {
                var targetStage = ResolveTransitionStage(analysis, targetStageName);
                if (targetStage?.OnEntryEffects is { Count: > 0 }) {
                    RunTransitionEffectList(
                        targetStage.OnEntryEffects, notifyStore,
                        entryStageName: targetStageName);
                }
            }
            finally {
                if (notifyStore && Store is not null) {
                    Store.NotifyTransition(
                        this, targetStageName, previousStageName: previousStageName);
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
    private void RunTransitionEffect(Effect effect, bool notifyStore) {
        RunTransitionEffectList([effect], notifyStore);
    }

    /// <summary>
    /// Nested <see cref="StageTransitionEffect"/> still recurses
    /// <see cref="TransitionStage"/>. When the list has no nested transitions,
    /// Domain-bound batches bind OnEntry/OnExit module methods from GetOrLower.
    /// Mixed lists flush/recurse binding GetOrLower segment bodies — never LowerActionBody.
    /// </summary>
    private void RunTransitionEffectList(
        IReadOnlyList<Effect> effects,
        bool notifyStore,
        string? entryStageName = null,
        string? exitStageName = null) {
        // No nested stage transitions: bind the whole batch via module OnEntry/OnExit.
        if (effects.All(e => e is not StageTransitionEffect)) {
            ThrowIfEffectListFailed(
                ExecuteEffectList(
                    effects, _typeDefAnalyzer,
                    entryStageName: entryStageName,
                    exitStageName: exitStageName),
                "stage entry/exit");
            return;
        }

        var segmentIndex = 0;
        var batch = new List<Effect>();
        void Flush() {
            if (batch.Count == 0) return;
            // Partial flush after nested transition — bind cached segment from GetOrLower.
            ThrowIfEffectListFailed(
                ExecuteEffectList(
                    batch, _typeDefAnalyzer,
                    entryStageName: entryStageName,
                    exitStageName: exitStageName,
                    entryExitSegmentIndex: segmentIndex),
                "stage entry/exit");
            segmentIndex++;
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
    /// Adds <paramref name="subscriber"/> to the linked-subscriber list the
    /// compiled <c>Notify{Stage}Subscribers</c> body iterates. Called from
    /// <see cref="DomainInstanceStore.Link"/>.
    /// </summary>
    internal void AddSubscriber(string fieldName, DomainEntityInstance subscriber) {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(subscriber);
        if (_values.TryGetValue(fieldName, out var raw) && raw is List<DomainEntityInstance> list) {
            foreach (var existing in list) {
                if (ReferenceEquals(existing, subscriber))
                    return;
            }
            list.Add(subscriber);
            return;
        }
        _values[fieldName] = new List<DomainEntityInstance> { subscriber };
    }

    /// <summary>
    /// Drops <paramref name="subscriber"/> from a registry list. Empty becomes
    /// null so the body's <c>!= null</c> guard skips. Called from
    /// <see cref="DomainInstanceStore.Unlink"/>.
    /// </summary>
    internal void RemoveSubscriber(string fieldName, DomainEntityInstance subscriber) {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(subscriber);
        if (!_values.TryGetValue(fieldName, out var raw) || raw is not List<DomainEntityInstance> list)
            return;
        list.RemoveAll(s => ReferenceEquals(s, subscriber));
        if (list.Count == 0)
            _values[fieldName] = null;
    }

    /// <summary>
    /// Runs the module's compiled <c>Notify{Stage}Subscribers</c> body
    /// (registry foreach → <c>sub.When…</c>). Missing method means this
    /// stage is not watched.
    /// </summary>
    internal void ExecuteNotifyStageSubscribers(
        string targetStageName, string? previousStageName) {
        ArgumentException.ThrowIfNullOrEmpty(targetStageName);
        if (Domain is null)
            return;
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        RuntimeAnalysisCache.GetOrLower(
            Domain, RuntimeAnalysisCache.Session(Domain), analysis);
        var methodName = $"Notify{targetStageName}Subscribers";
        if (!RuntimeAnalysisCache.TryGetModuleMethod(Domain, Entity.Name, methodName, out var method)
            || method?.Body is null)
            return;
        var (tree, rootParameters) = BindModuleMethodBody(method);
        var setArgs = new List<object?> { this };
        foreach (var parameter in rootParameters) {
            if (string.Equals(parameter.Name, "previousStage", StringComparison.Ordinal))
                setArgs.Add(previousStageName);
            else
                setArgs.Add(null);
        }
        ThrowIfEffectListFailed(
            ExecuteCachedSubscriptionTree(tree, rootParameters, setArgs),
            "notify subscribers");
    }

    /// <summary>
    /// Executes subscription effects in this instance's context (subscriber).
    /// <paramref name="peerInstance"/> is the related entity that transitioned.
    /// When <paramref name="peerBinding"/> is set (<c>when Rel Stage as name</c>),
    /// the peer is a typed SetArgs slot after this and previousStage.
    /// Notification-only subscriptions omit the binder.
    /// Called from the <c>When…</c> InvokeNamed arm (the compiled Notify body
    /// calls <c>sub.When…</c>; Link filled the registry). A transition
    /// inside the handler emits <c>Notify{Target}Subscribers</c>, which fans
    /// out the next hop.
    /// </summary>
    internal void ExecuteSubscriptionEffects(
        IReadOnlyList<Effect> effects,
        DomainEntityInstance peerInstance,
        string? peerBinding = null,
        SubscriptionDispatchPlanEntry? planEntry = null,
        string? targetStageName = null,
        string? previousStageName = null) {
        // A subscription is its own trigger for the automatic-transition loop guard.
        ClearAutomaticStageChain();

        // Empty subscription (notify-only) — avoid GetOrLower side effects for fail-closed tests.
        if (effects.Count == 0)
            return;

        // Domain-null: fail closed immediately (mirror EvaluatePolicy Domain-bound requirement).
        if (Domain is null)
            throw new InvalidOperationException(
                $"Cannot execute subscription effects on '{Entity.Name}' without a Domain-bound module.");

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        RuntimeAnalysisCache.GetOrLower(
            Domain, RuntimeAnalysisCache.Session(Domain), analysis);

        // Domain-bound: run the module handler cached at GetOrLower (same tree print emits).
        // Miss or missing plan entry throws. Bound peer is a typed SetArgs slot.
        if (planEntry is null)
            throw new InvalidOperationException(
                $"Subscription dispatch on '{Entity.Name}' requires a plan entry for cache bind.");
        if (targetStageName is not { Length: > 0 })
            throw new InvalidOperationException(
                $"Subscription dispatch on '{Entity.Name}' requires a target stage name.");
        if (!RuntimeAnalysisCache.TryGetSubscriptionBody(
                Domain, planEntry, targetStageName, out var body)
            || body is null)
            throw new InvalidOperationException(
                $"Subscription body is missing on entity '{Entity.Name}'.");
        var cached = body;
        var rootParameters = new List<Parameter>();
        var setArgs = new List<object?> { this };
        if (ContainsPreviousStageParameter(cached)) {
            rootParameters.Add(new Parameter("previousStage"));
            setArgs.Add(previousStageName);
        }
        if (peerBinding is { Length: > 0 }) {
            rootParameters.Add(new Parameter(
                peerBinding, new NamedTypeReference(peerInstance.Entity.Name)));
            setArgs.Add(peerInstance);
        }
        ThrowIfEffectListFailed(
            ExecuteCachedSubscriptionTree(cached, rootParameters, setArgs),
            "subscription");
    }

    private DomainResult? ExecuteCachedSubscriptionTree(
        Node tree,
        IReadOnlyList<Parameter> rootParameters,
        IReadOnlyList<object?> setArgs) {
        var compiled = CompileBody(
            tree, ModuleAwareTypeProvider(_typeDefAnalyzer), rootParameters);
        try {
            using var exec = Interpreter.Execute(compiled, s => s.SetArgs(setArgs));
            if (exec.Result.Value is DomainResult { IsSuccess: false } failed)
                return failed;
            return null;
        }
        catch (ConstraintFailureException ex) {
            return DomainResult.Failure(ex.Message);
        }
    }

    private static bool ContainsPreviousStageParameter(Node node) {
        if (node is Parameter p
            && string.Equals(p.Name, "previousStage", StringComparison.Ordinal))
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ContainsPreviousStageParameter(child))
                return true;
        }
        return false;
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
    /// Member-read <see cref="Store"/>. No Store: attach the C8d named twin so
    /// the compiled tree still runs.
    /// </summary>
    public DomainResult Create(string typeName, IReadOnlyDictionary<string, object?> values) {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(values);
        return StoreOrDefault().Create(this, typeName, values);
    }

    /// <summary>
    /// Notify-shaped Store bind for create-in (allocate, register, link).
    /// No Store: attach the C8d named twin so the compiled tree still runs.
    /// </summary>
    public DomainResult CreateIn(string relationshipName, IReadOnlyDictionary<string, object?> values) {
        ArgumentException.ThrowIfNullOrEmpty(relationshipName);
        ArgumentNullException.ThrowIfNull(values);
        return StoreOrDefault().CreateIn(this, relationshipName, values);
    }

    /// <summary>
    /// Constraint probe without allocating. Fail-before-mutate prefix in the lowered tree.
    /// No Store: attach the C8d named twin so the compiled tree still runs.
    /// </summary>
    public DomainResult ProbeCreate(string typeName, IReadOnlyDictionary<string, object?> values) {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(values);
        return StoreOrDefault().ProbeCreate(this, typeName, values);
    }

    /// <summary>
    /// C8d named twin: a default internal store so compiled Create trees always run.
    /// Attached only from Create / CreateIn / ProbeCreate, not from construction.
    /// </summary>
    private DomainInstanceStore StoreOrDefault() {
        var store = Store;
        if (store is not null)
            return store;
        store = new DomainInstanceStore();
        store.Add(this);
        return store;
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

    /// <summary>
    /// After a create-in to-one initializer (<c>section: offering</c>), also link the
    /// peer's unique collection of this child type (<c>Section.enrollments</c>). Skip
    /// when zero or several collections match — ambiguous inverses stay explicit.
    /// </summary>
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
}
