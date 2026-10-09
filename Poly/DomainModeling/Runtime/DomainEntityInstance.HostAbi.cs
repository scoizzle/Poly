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
    /// the shared StageTransition lowering do not call this (they use
    /// <c>Notify{Stage}Subscribers</c> for watched stages). Kept for HostAbi
    /// and direct callers. Skips when executing a subscription (cascade is
    /// store-owned) or when no store is attached.
    /// </summary>
    public void Notify(string targetStageName) =>
        Notify(targetStageName, previousStageName: null);

    /// <summary>
    /// Store fan-out after a stage assignment. <paramref name="previousStageName"/>
    /// is the stage left by this transition; All handlers use it as the once-edge.
    /// </summary>
    public void Notify(string targetStageName, string? previousStageName) {
        if (Store is not null && !_isExecutingSubscription)
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
                if (notifyStore && Store is not null && !_isExecutingSubscription) {
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
        string? peerBinding = null,
        SubscriptionDispatchPlanEntry? planEntry = null,
        string? targetStageName = null,
        string? previousStageName = null) {
        _isExecutingSubscription = true;
        // A subscription is its own trigger for the automatic-transition loop guard.
        ClearAutomaticStageChain();

        try {
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
            // Miss or missing plan entry throws — never BindPeerInEffect + LowerActionBody.
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
            if (peerBinding is { Length: > 0 })
                cached = MaterializePeerInSyntax(cached, peerBinding, peerInstance);
            // C1a: previousStage is a real SetArgs slot after this (when the handler declares it).
            var rootParameters = ContainsPreviousStageParameter(cached)
                ? (IReadOnlyList<Parameter>)[new Parameter("previousStage")]
                : [];
            ThrowIfEffectListFailed(
                ExecuteCachedSubscriptionTree(cached, rootParameters, previousStageName),
                "subscription");
        }
        finally {
            _isExecutingSubscription = false;
        }
    }

    private DomainResult? ExecuteCachedSubscriptionTree(
        Node tree,
        IReadOnlyList<Parameter> rootParameters,
        string? previousStageName) {
        var compiled = CompileBody(
            tree, ModuleAwareTypeProvider(_typeDefAnalyzer), rootParameters);
        var setArgs = new object?[1 + rootParameters.Count];
        setArgs[0] = this;
        if (rootParameters.Count > 0)
            setArgs[1] = previousStageName;
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
    /// Substitutes peer <see cref="Parameter"/> reads in a GetOrLower-cached
    /// subscription tree with constants from the transitioned peer bag — not a re-lower.
    /// </summary>
    private static Node MaterializePeerInSyntax(
        Node node, string peerBinding, DomainEntityInstance peer) => node switch {
            Member { Value: Parameter p } m
                when string.Equals(p.Name, peerBinding, StringComparison.Ordinal) =>
                new Constant(peer.GetProperty<object?>(m.MemberName)),
            Parameter p when string.Equals(p.Name, peerBinding, StringComparison.Ordinal) =>
                new Constant(peer),
            // Non-peer parameters (entity subject, loop vars, etc.) — leave unchanged.
            Parameter => node,
            Block b => new Block(
                b.Nodes.Select(n => MaterializePeerInSyntax(n, peerBinding, peer)),
                b.Variables.Select(n => MaterializePeerInSyntax(n, peerBinding, peer))),
            IfStatement i => new IfStatement(
                MaterializePeerInSyntax(i.Condition, peerBinding, peer),
                MaterializePeerInSyntax(i.ThenBranch, peerBinding, peer),
                i.ElseBranch is null ? null : MaterializePeerInSyntax(i.ElseBranch, peerBinding, peer)),
            Return r => r.Value is null ? r : new Return(MaterializePeerInSyntax(r.Value, peerBinding, peer)),
            Assignment a => new Assignment(
                MaterializePeerInSyntax(a.Destination, peerBinding, peer),
                MaterializePeerInSyntax(a.Value, peerBinding, peer)),
            Invoke inv => new Invoke(
                MaterializePeerInSyntax(inv.Delegate, peerBinding, peer),
                [.. inv.Arguments.Select(a => MaterializePeerInSyntax(a, peerBinding, peer))]) {
                TypeArguments = inv.TypeArguments
            },
            Member m => new Member(MaterializePeerInSyntax(m.Value, peerBinding, peer), m.MemberName),
            Poly.Ast.Nodes.Not n => new Poly.Ast.Nodes.Not(MaterializePeerInSyntax(n.Value, peerBinding, peer)),
            Equal e => new Equal(
                MaterializePeerInSyntax(e.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(e.RightHandValue, peerBinding, peer)),
            NotEqual ne => new NotEqual(
                MaterializePeerInSyntax(ne.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(ne.RightHandValue, peerBinding, peer)),
            LessThan lt => new LessThan(
                MaterializePeerInSyntax(lt.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(lt.RightHandValue, peerBinding, peer)),
            LessThanOrEqual le => new LessThanOrEqual(
                MaterializePeerInSyntax(le.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(le.RightHandValue, peerBinding, peer)),
            GreaterThan gt => new GreaterThan(
                MaterializePeerInSyntax(gt.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(gt.RightHandValue, peerBinding, peer)),
            GreaterThanOrEqual ge => new GreaterThanOrEqual(
                MaterializePeerInSyntax(ge.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(ge.RightHandValue, peerBinding, peer)),
            Poly.Ast.Nodes.Add add => new Poly.Ast.Nodes.Add(
                MaterializePeerInSyntax(add.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(add.RightHandValue, peerBinding, peer)),
            Poly.Ast.Nodes.Subtract sub => new Poly.Ast.Nodes.Subtract(
                MaterializePeerInSyntax(sub.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(sub.RightHandValue, peerBinding, peer)),
            Poly.Ast.Nodes.Multiply mul => new Poly.Ast.Nodes.Multiply(
                MaterializePeerInSyntax(mul.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(mul.RightHandValue, peerBinding, peer)),
            Poly.Ast.Nodes.Divide div => new Poly.Ast.Nodes.Divide(
                MaterializePeerInSyntax(div.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(div.RightHandValue, peerBinding, peer)),
            Poly.Ast.Nodes.And and => new Poly.Ast.Nodes.And(
                MaterializePeerInSyntax(and.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(and.RightHandValue, peerBinding, peer)),
            Poly.Ast.Nodes.Or or => new Poly.Ast.Nodes.Or(
                MaterializePeerInSyntax(or.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(or.RightHandValue, peerBinding, peer)),
            Coalesce c => new Coalesce(
                MaterializePeerInSyntax(c.LeftHandValue, peerBinding, peer),
                MaterializePeerInSyntax(c.RightHandValue, peerBinding, peer)),
            Conditional cond => new Conditional(
                MaterializePeerInSyntax(cond.Condition, peerBinding, peer),
                MaterializePeerInSyntax(cond.IfTrue, peerBinding, peer),
                MaterializePeerInSyntax(cond.IfFalse, peerBinding, peer)),
            ForEachLoop f => new ForEachLoop(
                f.LoopVariable,
                MaterializePeerInSyntax(f.Collection, peerBinding, peer),
                MaterializePeerInSyntax(f.Body, peerBinding, peer),
                f.Label),
            WhileLoop w => new WhileLoop(
                MaterializePeerInSyntax(w.Condition, peerBinding, peer),
                MaterializePeerInSyntax(w.Body, peerBinding, peer),
                w.Label),
            DoWhileLoop dw => new DoWhileLoop(
                MaterializePeerInSyntax(dw.Body, peerBinding, peer),
                MaterializePeerInSyntax(dw.Condition, peerBinding, peer),
                dw.Label),
            ForLoop fl => new ForLoop(
                fl.Initializer is null ? null : MaterializePeerInSyntax(fl.Initializer, peerBinding, peer),
                fl.Condition is null ? null : MaterializePeerInSyntax(fl.Condition, peerBinding, peer),
                fl.Increment is null ? null : MaterializePeerInSyntax(fl.Increment, peerBinding, peer),
                MaterializePeerInSyntax(fl.Body, peerBinding, peer),
                fl.Label),
            TryCatchFinally tcf => new TryCatchFinally(
                MaterializePeerInSyntax(tcf.TryBlock, peerBinding, peer),
                tcf.CatchClauses?.Select(c => new CatchClause(
                    c.ExceptionType is null ? null : MaterializePeerInSyntax(c.ExceptionType, peerBinding, peer),
                    c.VariableName,
                    MaterializePeerInSyntax(c.Body, peerBinding, peer))).ToList(),
                tcf.FinallyBlock is null ? null : MaterializePeerInSyntax(tcf.FinallyBlock, peerBinding, peer)),
            UsingStatement us => new UsingStatement(
                MaterializePeerInSyntax(us.Resource, peerBinding, peer),
                MaterializePeerInSyntax(us.Body, peerBinding, peer)),
            New n => new New(
                MaterializePeerInSyntax(n.Type, peerBinding, peer),
                [.. n.Arguments.Select(a => MaterializePeerInSyntax(a, peerBinding, peer))]),
            ThrowStatement ts => new ThrowStatement(MaterializePeerInSyntax(ts.Exception, peerBinding, peer)),
            TypeCast tc => new TypeCast(
                MaterializePeerInSyntax(tc.Operand, peerBinding, peer),
                MaterializePeerInSyntax(tc.TargetTypeReference, peerBinding, peer),
                tc.IsChecked),
            TypeIs ti => new TypeIs(
                MaterializePeerInSyntax(ti.Operand, peerBinding, peer),
                MaterializePeerInSyntax(ti.TargetTypeReference, peerBinding, peer)),
            TypeAs ta => new TypeAs(
                MaterializePeerInSyntax(ta.Operand, peerBinding, peer),
                MaterializePeerInSyntax(ta.TargetTypeReference, peerBinding, peer)),
            IndexAccess ia => new IndexAccess(
                MaterializePeerInSyntax(ia.Value, peerBinding, peer),
                [.. ia.Arguments.Select(a => MaterializePeerInSyntax(a, peerBinding, peer))]),
            UnaryMinus um => new UnaryMinus(MaterializePeerInSyntax(um.Operand, peerBinding, peer)),
            NullForgiving nf => new NullForgiving(MaterializePeerInSyntax(nf.Operand, peerBinding, peer)),
            LabelDeclaration ld => new LabelDeclaration(
                ld.Name, MaterializePeerInSyntax(ld.Statement, peerBinding, peer)),
            Default d => d.TargetType is null
                ? d
                : new Default(MaterializePeerInSyntax(d.TargetType, peerBinding, peer)),
            BreakStatement or ContinueStatement or GotoStatement
                or Variable or Constant or NamedTypeReference or TypeReference
                or PrimitiveTypeReference or ClrTypeReference or ThisReference
                or Comment => node,
            _ => throw new InvalidOperationException(
                $"MaterializePeerInSyntax: unhandled node type '{node.GetType().Name}' " +
                $"(peer binding '{peerBinding}').")
        };

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
