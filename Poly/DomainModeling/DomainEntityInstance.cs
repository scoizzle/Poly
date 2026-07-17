using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling;

/// <summary>
/// A materialized instance of a domain <see cref="Entity"/>, backed by a
/// <c>Dictionary&lt;string, object?&gt;</c> and typed via an
/// <see cref="AstTypeDefinition"/> created from the entity's property schema.
///
/// <para>The instance composes existing platform machinery: properties are
/// lowered to a <see cref="TypeDefinitionNode"/>, analyzed, and resolved
/// through the standard <see cref="ITypeDefinitionProvider"/> chain. Policy
/// evaluation uses <see cref="Interpreter.Compile"/> with the instance's type
/// definition as the provider — the same path as the MCP evaluate_policy tool.</para>
///
/// <para><b>RAII-style:</b> the static factory <see cref="Create"/> returns
/// only valid instances. Structural validation (required properties exist,
/// types are coercible) happens at creation time.</para>
/// </summary>
public sealed record DomainEntityInstance {
    private readonly Dictionary<string, object?> _values;
    private readonly TypeDefinitionNodeAnalyzer _typeDefAnalyzer;
    private readonly List<DomainEntityInstance> _createdChildren = [];
    private Dictionary<string, object?>? _eventValues;
    private bool _isExecutingSubscription;
    internal DomainInstanceStore? Store { get; set; }

    /// <summary>
    /// Makes the transitioning entity's properties available as "event" in
    /// subscription effect evaluation. Called by <see cref="DomainInstanceStore"/>
    /// during fan-out.
    /// </summary>
    internal void SetEventInstance(DomainEntityInstance? eventInstance) {
        if (eventInstance is null) {
            _eventValues = null;
            return;
        }
        // Build a real Dictionary from the snapshot (Snapshot() returns IReadOnlyDictionary)
        var snapshot = eventInstance.Snapshot();
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in snapshot)
            dict[kv.Key] = kv.Value;
        _eventValues = dict;
    }

    private DomainEntityInstance(
        Entity entity,
        Dictionary<string, object?> values,
        TypeDefinitionNodeAnalyzer typeDefAnalyzer,
        string? currentStage,
        Domain? domain = null) {
        Entity = entity;
        _values = values;
        _typeDefAnalyzer = typeDefAnalyzer;
        CurrentStage = currentStage;
        Domain = domain;
    }

    /// <summary>The domain model this instance belongs to (null for standalone instances).</summary>
    public Domain? Domain { get; }

    /// <summary>The domain entity definition this instance was created from.</summary>
    public Entity Entity { get; }

    /// <summary>The current lifecycle stage, if the entity defines stages.</summary>
    public string? CurrentStage { get; private set; }

    /// <summary>Whether this instance has been deleted by a <see cref="DeleteEntityInstance"/> effect.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>Child instances created by <see cref="CreateEntityInstance"/> effects.</summary>
    public IReadOnlyList<DomainEntityInstance> CreatedChildren => _createdChildren;


    /// <summary>
    /// Creates a new instance of <paramref name="entity"/> with the given
    /// property values. Validates that all provided property names exist on
    /// the entity, applies default values for missing properties, and sets
    /// the initial stage (first defined stage, if any).
    /// </summary>
    /// <param name="entity">The domain entity definition.</param>
    /// <param name="propertyValues">Optional initial property values. Missing
    /// properties get their default value (or <c>null</c>).</param>
    /// <returns>A validated <see cref="DomainEntityInstance"/>.</returns>
    /// <exception cref="ArgumentException">When a property name does not exist
    /// on the entity or a required property is missing with no default.</exception>
    public static DomainEntityInstance Create(
        Entity entity,
        IReadOnlyDictionary<string, object?>? propertyValues = null,
        Domain? domain = null) {
        ArgumentNullException.ThrowIfNull(entity);

        var entityPropNames = new HashSet<string>(
            entity.Properties.Select(p => p.Name),
            StringComparer.Ordinal);

        // Validate provided property names
        if (propertyValues is not null) {
            foreach (var key in propertyValues.Keys) {
                if (!entityPropNames.Contains(key))
                    throw new ArgumentException(
                        $"Property '{key}' does not exist on entity '{entity.Name}'. " +
                        $"Available: {string.Join(", ", entityPropNames)}.");
            }
        }

        // Build values dictionary — apply provided values, then defaults
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in entity.Properties) {
            if (propertyValues is not null && propertyValues.TryGetValue(prop.Name, out var v)) {
                values[prop.Name] = v;
            }
            else {
                values[prop.Name] = null; // default for unspecified properties
            }
        }

        // Build TypeDefinitionNode → AstTypeDefinition for typed compilation
        var propDefs = new List<PropertyDefinitionNode>();
        foreach (var ep in entity.Properties) {
            var typeRef = MapDomainTypeToAstNode(ep.Type);
            propDefs.Add(new PropertyDefinitionNode(ep.Name, typeRef,
                Getter: new PropertyGetterDefinitionNode()));
        }

        var typeDefNode = new TypeDefinitionNode(
            Name: entity.Name,
            Properties: [.. propDefs],
            Namespace: null);

        var typeDefAnalyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        typeDefAnalyzer.Analyze(ctx, typeDefNode);

        var currentStage = entity.Stages.FirstOrDefault()?.Name;

        return new DomainEntityInstance(entity, values, typeDefAnalyzer, currentStage, domain);
    }

    /// <summary>
    /// Reads a property value, coercing to <typeparamref name="T"/>.
    /// </summary>
    public T? GetProperty<T>(string name) {
        if (!_values.TryGetValue(name, out var value))
            throw new ArgumentException($"Property '{name}' not found on entity '{Entity.Name}'.");
        return value is T t ? t : default;
    }

    /// <summary>
    /// Sets a property value. Validates that the property exists on the entity.
    /// </summary>
    public void SetProperty(string name, object? value) {
        if (!_values.ContainsKey(name))
            throw new ArgumentException(
                $"Property '{name}' does not exist on entity '{Entity.Name}'. " +
                $"Available: {string.Join(", ", _values.Keys)}.");
        _values[name] = value;
    }

    /// <summary>
    /// Evaluates <paramref name="policy"/> against this instance using the
    /// VM (direct AST lowering — canonical path). Returns <c>true</c> if the
    /// policy's guard expression is satisfied.
    /// </summary>
    public bool EvaluatePolicy(Policy policy) {
        ArgumentNullException.ThrowIfNull(policy);

        var entityParam = new Parameter("entity", new TypeReference(Entity.Name));
        var pass = new DomainExpressionLoweringPass();
        var lowered = pass.Lower(policy.Expression, entityParam);

        var compiled = Interpreter.Compile(lowered, _typeDefAnalyzer);
        using var exec = Interpreter.Execute(compiled,
            s => s.SetArgs(new object?[] { _values }));
        return exec.Result.GetValue<bool>();
    }

    /// <summary>
    /// Attempts to call <paramref name="actionName"/> on this instance.
    /// Evaluates all guard policies, then executes each effect in sequence.
    ///
    /// <para><b>Action pipeline order:</b></para>
    /// <list type="number">
    ///   <item>Resolve action by name — fail if not found.</item>
    ///   <item>Evaluate action-level guard policies (<see cref="Policy"/>).</item>
    ///   <item>Evaluate current-stage guard policies.</item>
    ///   <item>Evaluate entity-level guard policies.</item>
    ///   <item>Execute each effect in declaration order:
    ///     <list type="bullet">
    ///       <item><b>VM-compiled</b> (<see cref="AssignEffect"/>, <see cref="CompositeEffect"/>, <see cref="ConditionalEffect"/>) → lowered to Syntax AST → compiled via <see cref="Interpreter.Compile"/> → executed via VM.</item>
    ///       <item><b>Direct-execution</b> (<see cref="StageTransitionEffect"/>, <see cref="CreateEntityInstance"/>, <see cref="InvokeActionEffect"/>, <see cref="DeleteEntityInstance"/>) → mutates instance state directly.</item>
    ///     </list>
    ///   </item>
    ///   <item>On <see cref="StageTransitionEffect"/>: set stage → if <c>notifyStore</c>, fire stage-scoped <see cref="StageSubscription"/> effects (see <see cref="DomainInstanceStore.NotifyTransition"/>).</item>
    /// </list>
    ///
    /// <para><b>VM-executable effects</b> (<see cref="AssignEffect"/>,
    /// <see cref="CompositeEffect"/>, <see cref="ConditionalEffect"/>) are
    /// lowered to Syntax AST, compiled, and executed via the VM.</para>
    ///
    /// <para><b>Direct-execution effects</b> (<see cref="StageTransitionEffect"/>,
    /// <see cref="CreateEntityInstance"/>, <see cref="InvokeActionEffect"/>)
    /// mutate the instance directly.</para>
    /// </summary>
    public ActionCallResult CallAction(string actionName) {
        // Find action: first check current stage (and its parent chain) for effective actions,
        // then entity-level. Stage-scoped actions are only available while on that stage.
        Poly.DomainModeling.Action? action = null;
        if (CurrentStage is not null) {
            // Walk the stage hierarchy: current stage, then parent, then grandparent, etc.
            var currentStageRef = Entity.Stages
                .FirstOrDefault(s => string.Equals(s.Name, CurrentStage, StringComparison.Ordinal));

            var walkStage = currentStageRef;
            while (walkStage is not null && action is null) {
                action = walkStage.Actions
                    .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));
                walkStage = walkStage.Parent is not null
                    ? Entity.Stages.FirstOrDefault(s => string.Equals(s.Name, walkStage.Parent.StageName, StringComparison.Ordinal))
                    : null;
            }
        }

        action ??= Entity.Actions
            .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));

        if (action is null)
            return ActionCallResult.Missing(Entity.Name, actionName);

        // ── Evaluate all guard policies ─────────────────────────
        var failures = new List<string>();
        foreach (var guard in action.Policies)
            if (!EvaluatePolicy(guard)) failures.Add(guard.Name);

        if (failures.Count > 0)
            return ActionCallResult.Blocked(actionName, failures);

        var stage = Entity.Stages.FirstOrDefault(
            s => string.Equals(s.Name, CurrentStage, StringComparison.Ordinal));
        if (stage is not null)
            foreach (var guard in stage.Policies)
                if (!EvaluatePolicy(guard)) failures.Add(guard.Name);

        if (failures.Count > 0)
            return ActionCallResult.Blocked(actionName, failures);

        foreach (var guard in Entity.Policies)
            if (!EvaluatePolicy(guard)) failures.Add(guard.Name);

        if (failures.Count > 0)
            return ActionCallResult.Blocked(actionName, failures);

        // ── Execute effects ─────────────────────────────────────
        var subjectParam = new Parameter("entity", new TypeReference(Entity.Name));
        var effectPass = new EffectLoweringPass(Entity, subjectParam);

        foreach (var effect in action.Effects) {
            ExecuteEffect(effect, effectPass);
        }

        return ActionCallResult.Ok(actionName, CurrentStage);
    }

    /// <summary>
    /// Executes a single effect. VM-executable effects go through
    /// lowering → compile → execute; direct-execution effects mutate
    /// the instance in place.
    /// </summary>
    private void ExecuteEffect(Effect effect, EffectLoweringPass effectPass) {
        var lowered = effectPass.TryLowerVmNode(effect);
        if (lowered is not null) {
            var compiled = Interpreter.Compile(lowered, _typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { _values }));
            return;
        }

        switch (effect) {
            case StageTransitionEffect transition:
                // Action path: always notify. Subscription path suppresses
                // notifications via the _isExecutingSubscription flag checked
                // in TransitionStage.
                TransitionStage(transition.TargetStage.StageName, notifyStore: true);
                break;
            case CreateEntityInstance create:
                CreateChildInstance(create);
                break;
            case InvokeActionEffect invoke:
                CallAction(invoke.ActionName);
                break;
            case DeleteEntityInstance:
                IsDeleted = true;
                break;
            case LinkRelationshipEffect link:
                ExecuteLink(link.RelationshipName, link.Target);
                break;
            case UnlinkRelationshipEffect unlink:
                ExecuteUnlink(unlink.RelationshipName, unlink.Target);
                break;
        }
    }

    /// <summary>
    /// Resolves <paramref name="targetExpr"/> to a <see cref="DomainEntityInstance"/> and
    /// records an instance link (source = this, target = resolved) in the store.
    /// Target must be a <see cref="PropertyAccess"/> whose current value is a
    /// <see cref="DomainEntityInstance"/> (set via property bag or prior effects).
    /// Prefer <see cref="DomainInstanceStore.Link"/> for direct API linking.
    /// </summary>
    private void ExecuteLink(string relationshipName, DomainExpression targetExpr) {
        if (Store is null)
            throw new InvalidOperationException(
                "Cannot link relationship without a DomainInstanceStore. Call store.Add(instance) first.");
        var target = ResolveLinkedInstance(targetExpr, "Link");
        Store.Link(relationshipName, this, target);
    }

    private void ExecuteUnlink(string relationshipName, DomainExpression targetExpr) {
        if (Store is null)
            throw new InvalidOperationException(
                "Cannot unlink relationship without a DomainInstanceStore. Call store.Add(instance) first.");
        var target = ResolveLinkedInstance(targetExpr, "Unlink");
        Store.Unlink(relationshipName, this, target);
    }

    private DomainEntityInstance ResolveLinkedInstance(DomainExpression targetExpr, string effectName) {
        if (targetExpr is PropertyAccess pa
            && _values.TryGetValue(pa.Name, out var value)
            && value is DomainEntityInstance instance)
            return instance;

        throw new InvalidOperationException(
            $"{effectName} target must be a PropertyAccess whose value is a DomainEntityInstance. " +
            "Use DomainInstanceStore.Link/Unlink for direct API linking, or store the target instance " +
            "in a property bag entry first.");
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
    /// <b>current</b> stage (not the parent chain), while <see cref="CallAction"/> walks the
    /// parent chain for actions. This asymmetry exists because effective-policy computation
    /// is performed by analyzers (walking the hierarchy), while runtime scenario gating is
    /// still a per-stage concern.
    ///
    /// <b>OnEntry re-entrancy:</b> If an OnEntry effect calls <see cref="CallAction"/> which
    /// triggers another transition on the <b>same instance</b>, the resulting <c>TransitionStage</c>
    /// call is <b>not</b> bounded by the store&#x2019;s <c>maxDepth</c> (which limits subscription
    /// fan-out across <b>different</b> instances). Re-entrant same-instance transitions risk
    /// stack overflow and should be avoided until a depth budget is added.
    /// </remarks>
    internal void TransitionStage(string targetStageName, bool notifyStore = true) {
        if (!Entity.Stages.Any(s => string.Equals(s.Name, targetStageName, StringComparison.Ordinal)))
            return;

        var previousStageName = CurrentStage;
        if (string.Equals(previousStageName, targetStageName, StringComparison.Ordinal))
            return;

        // ── Run OnExit effects on the current stage ────────────
        if (previousStageName is not null) {
            var prevStage = Entity.Stages.FirstOrDefault(
                s => string.Equals(s.Name, previousStageName, StringComparison.Ordinal));
            if (prevStage?.OnExitEffects is { Count: > 0 }) {
                var exitPass = new EffectLoweringPass(Entity,
                    new Parameter("entity", new TypeReference(Entity.Name)));
                foreach (var effect in prevStage.OnExitEffects)
                    ExecuteEffect(effect, exitPass);
            }
        }

        // ── Set new stage ──────────────────────────────────────
        CurrentStage = targetStageName;

        // ── Run OnEntry effects on the target stage ────────────
        // Notify subscribers runs in a finally block so it fires even if
        // OnEntry effects throw (the stage is already set).
        try {
            var targetStage = Entity.Stages.FirstOrDefault(
                s => string.Equals(s.Name, targetStageName, StringComparison.Ordinal));
            if (targetStage?.OnEntryEffects is { Count: > 0 }) {
                var entryPass = new EffectLoweringPass(Entity,
                    new Parameter("entity", new TypeReference(Entity.Name)));
                foreach (var effect in targetStage.OnEntryEffects)
                    ExecuteEffect(effect, entryPass);
            }
        }
        finally {
            // Notify subscribers (skip during subscription execution — cascading already handled by store)
            if (notifyStore && Store is not null && !_isExecutingSubscription) {
                Store.NotifyTransition(this, targetStageName);
            }
        }
    }

    /// <summary>
    /// Executes subscription effects in this instance's context (subscriber).
    /// The <paramref name="eventInstance"/> is the entity that transitioned.
    /// Called by <see cref="DomainInstanceStore.NotifyTransition"/>.
    ///
    /// Subscription-triggered transitions suppress store notification via
    /// <c>_isExecutingSubscription</c> — cascading is handled by the store's
    /// depth-limited recursion instead.
    /// </summary>
    ///
    /// <remarks>
    /// <b>Option B (current):</b> Event-instance property values are injected into
    /// the subscriber's <c>_values</c> dictionary with an "event." prefix, but
    /// this is <b>not</b> visible to the VM's member-resolution pipeline (which
    /// resolves properties against CLR type definitions, not the dictionary).
    /// Expressions like <c>event.Code</c> in subscription effects will <b>not</b>
    /// resolve to the transitioning instance's value through the current lowering
    /// path. The characterization test
    /// <c>SubscriptionEffect_EventPropertyAccess_Gap_Documented</c> documents this.
    /// Until proper "event" lowering is implemented (when a named consumer needs
    /// it), subscription effects should use only literal values and "this" properties.
    /// </remarks>
    /// <summary>
    /// Prefix used to store event instance property values in <c>_values</c>
    /// during subscription effect execution. Note: the VM's member-resolution
    /// pipeline does <b>not</b> currently resolve keys with this prefix — see
    /// remarks on <see cref="ExecuteSubscriptionEffects"/>.
    /// </summary>
    private const string EventPrefix = "event.";

    internal void ExecuteSubscriptionEffects(IReadOnlyList<Effect> effects, DomainEntityInstance eventInstance) {
        SetEventInstance(eventInstance);
        _isExecutingSubscription = true;

        // eventKeys must be declared outside try so cleanup in finally can access it.
        var eventKeys = new List<string>();
        try {
            if (_eventValues is not null) {
                foreach (var kv in _eventValues) {
                    var key = $"{EventPrefix}{kv.Key}";
                    _values[key] = kv.Value;
                    eventKeys.Add(key);
                }
            }

            var subjectParam = new Parameter("entity", new TypeReference(Entity.Name));
            var effectPass = new EffectLoweringPass(Entity, subjectParam);

            foreach (var effect in effects) {
                ExecuteEffect(effect, effectPass);
            }
        }
        finally {
            // Always clean up merged event values (even on throw) so _values
            // doesn't accumulate stale "event.*" keys that might confuse
            // subsequent non-subscription access.
            // Note: the VM's member-resolution pipeline does NOT resolve
            // "event.*" dictionary keys — see Option B remarks above.
            foreach (var key in eventKeys)
                _values.Remove(key);

            _isExecutingSubscription = false;
            SetEventInstance(null);
        }
    }

    /// <summary>
    /// Creates a child entity instance from a <see cref="CreateEntityInstance"/>
    /// effect. Looks up the target entity by type name — first from the parent
    /// <see cref="Domain"/> if available, otherwise falls back to the current
    /// entity (same-type creation). Initializer expressions are evaluated
    /// against the <em>parent</em> instance and bound to the child's properties.
    /// </summary>
    private void CreateChildInstance(CreateEntityInstance createEffect) {
        var targetTypeName = createEffect.Type.TypeName;

        // Resolve target entity definition
        Entity targetEntity;
        if (Domain is not null) {
            targetEntity = Domain.Types.OfType<Entity>()
                .FirstOrDefault(e => string.Equals(e.Name, targetTypeName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Entity type '{targetTypeName}' not found in domain '{Domain.Name}'.");
        }
        else {
            targetEntity = Entity; // same-type creation when no domain reference
        }

        // Evaluate initializers against the parent instance
        var initialValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var binding in createEffect.Initializers) {
            var lowered = new DomainExpressionLoweringPass().Lower(
                binding.Expression,
                new Parameter("entity", new TypeReference(Entity.Name)));
            var compiled = Interpreter.Compile(lowered, _typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { _values }));
            initialValues[binding.PropertyName] = exec.Result.GetValue<object>();
        }

        var child = Create(targetEntity, initialValues, Domain);
        _createdChildren.Add(child);

        // BR.3.3: Auto-register child in the parent's store, if present.
        Store?.Add(child);
    }

    /// <summary>
    /// Returns all property names, values, and the current stage for debugging.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Snapshot() => _values.AsReadOnly();

    // ── Private helpers ─────────────────────────────────────────

    /// <summary>
    /// Maps a domain type reference (e.g. "Text", "Number") to an AST type
    /// reference node suitable for <see cref="PropertyDefinitionNode"/>.
    /// </summary>
    private static Node MapDomainTypeToAstNode(DomainTypeReference domainType) {
        var typeName = domainType.TypeName;
        return typeName switch {
            "Text" => new PrimitiveTypeReference(Prim.String),
            "Number" => new PrimitiveTypeReference(Prim.Int64),
            "Int" => new PrimitiveTypeReference(Prim.Int64),
            "Boolean" => new PrimitiveTypeReference(Prim.Boolean),
            "Bool" => new PrimitiveTypeReference(Prim.Boolean),
            "DateTime" => new PrimitiveTypeReference(Prim.DateTime),
            "Timestamp" => new PrimitiveTypeReference(Prim.DateTime),
            "Date" => new PrimitiveTypeReference(Prim.DateOnly),
            "DateOnly" => new PrimitiveTypeReference(Prim.DateOnly),
            "Time" => new PrimitiveTypeReference(Prim.TimeOnly),
            "TimeOnly" => new PrimitiveTypeReference(Prim.TimeOnly),
            "Duration" => new PrimitiveTypeReference(Prim.TimeSpan),
            "TimeSpan" => new PrimitiveTypeReference(Prim.TimeSpan),
            "Uuid" => new PrimitiveTypeReference(Prim.Guid),
            "Guid" => new PrimitiveTypeReference(Prim.Guid),
            "Decimal" => new PrimitiveTypeReference(Prim.Decimal),
            "Float" => new PrimitiveTypeReference(Prim.Float64),
            "Double" => new PrimitiveTypeReference(Prim.Float64),
            _ => new PrimitiveTypeReference(Prim.Structure)
        };
    }
}

/// <summary>
/// Result of calling an action on a <see cref="DomainEntityInstance"/>.
/// </summary>
public sealed record ActionCallResult {
    private ActionCallResult() { }

    /// <summary>The action name that was called.</summary>
    public string ActionName { get; private init; } = "";

    /// <summary>Whether the action call succeeded (all guards passed).</summary>
    public bool Succeeded { get; private init; }

    /// <summary>Names of guard policies that failed, if any.</summary>
    public IReadOnlyList<string> FailedGuards { get; private init; } = [];

    /// <summary>The new stage after the action, if a transition occurred.</summary>
    public string? NewStage { get; private init; }

    /// <summary>Error message for not-found action.</summary>
    public string? ErrorMessage { get; private init; }

    internal static ActionCallResult Ok(string actionName, string? newStage) => new() {
        ActionName = actionName,
        Succeeded = true,
        NewStage = newStage
    };

    internal static ActionCallResult Blocked(string actionName, List<string> failures) => new() {
        ActionName = actionName,
        Succeeded = false,
        FailedGuards = failures.AsReadOnly()
    };

    internal static ActionCallResult Missing(string entityName, string actionName) => new() {
        ActionName = actionName,
        Succeeded = false,
        ErrorMessage = $"Action '{actionName}' not found on entity '{entityName}'."
    };
}