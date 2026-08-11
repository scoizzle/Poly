using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;

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
///
/// <para><b>Domain-bound vs standalone:</b></para>
/// <list type="bullet">
///   <item><b>Domain non-null:</b> semantic dispatch uses analysis catalog/helpers only
///     (<see cref="RuntimeAnalysisCache"/> → catalog / structure / subscription plans).
///     Missing required bags fail closed (throw). No structural tree-scan fallback.</item>
///   <item><b>Standalone (<see cref="Domain"/> null):</b> reduced contract only —
///     structural <see cref="InvokeAction"/> / OnEntry-OnExit on the entity definition
///     (including SA fallthrough empty stage-copy → entity action); no subscriptions
///     (<see cref="DomainInstanceStore.NotifyTransition"/> no-ops), no relationship
///     semantic resolve, no <c>create in</c>. Not a second full SA/catalog implementation.</item>
/// </list>
/// </summary>
public sealed record DomainEntityInstance {
    private readonly Dictionary<string, object?> _values;
    private readonly TypeDefinitionNodeAnalyzer _typeDefAnalyzer;
    private readonly List<DomainEntityInstance> _createdChildren = [];
    private bool _isExecutingSubscription;
    private int _invokeDepth;
    private int _transitionDepth;
    /// <summary>Max nested <see cref="InvokeAction"/> depth (self-invoke / re-entrancy).</summary>
    public const int MaxInvokeDepth = 16;
    /// <summary>Max nested <see cref="TransitionStage"/> depth (OnEntry/OnExit re-entrancy).</summary>
    public const int MaxTransitionDepth = 16;
    internal DomainInstanceStore? Store { get; set; }

    private QuantifierPreprocessRewrite? _quantifierRewrite;
    private QuantifierPreprocessRewrite QuantifierRewrite =>
        _quantifierRewrite ??= new(this);

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

        // Relationships are entity-owned navigations. When a domain is provided, resolve
        // the canonical entity instance from the domain so the instance always carries the
        // same node identity analysis ran on (the legacy 3-arg Domain ctor redistributes
        // relationships onto entity copies). Falls back to the passed entity when the name
        // is not present — preserves standalone semantics.
        if (domain is not null) {
            var canonical = domain.Types.OfType<Entity>().FirstOrDefault(e =>
                string.Equals(e.Name, entity.Name, StringComparison.Ordinal));
            if (canonical is not null)
                entity = canonical;
        }

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
            else if (prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault() is { } defaultValue) {
                values[prop.Name] = EvaluateDefaultValue(defaultValue.Expression);
            }
            else {
                values[prop.Name] = null; // default for unspecified properties
            }
        }

        var typeDefAnalyzer = BuildTypeDefAnalyzer(entity.Name, entity.Properties);

        // Initial stage: first declared stage name (factory shape; not a semantic rediscovery).
        var currentStage = entity.Stages.FirstOrDefault()?.Name;

        return new DomainEntityInstance(entity, values, typeDefAnalyzer, currentStage, domain);
    }

    /// <summary>
    /// Evaluates a DSL default expression to a concrete runtime value. The runtime
    /// stores enum-typed properties as strings, so an enum member name (e.g.
    /// <c>default(Active)</c>) lowers to its name string; <c>now</c>/<c>today</c>/<c>guid</c>
    /// evaluate at creation time. Matches the C# export's defaulted optional ctor params.
    /// </summary>
    private static object? EvaluateDefaultValue(DomainExpression expr) => expr switch {
        Literal lit => lit.Value,
        PropertyAccess pa => pa.Name switch {
            "now" or "utcnow" => DateTime.UtcNow,
            "today" => DateOnly.FromDateTime(DateTime.UtcNow),
            "guid" => Guid.NewGuid(),
            _ => pa.Name // enum member name — runtime stores enum values as strings
        },
        _ => null
    };

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
    ///
    /// <para>Collection quantifiers (any/all/none/count) are preprocessed before
    /// lowering — evaluated against the current store's linked instances
    /// and replaced with literal results. This keeps the VM lowering path
    /// quantifier-free while enabling store-aware policy evaluation.</para>
    /// </summary>
    public bool EvaluatePolicy(Policy policy) {
        ArgumentNullException.ThrowIfNull(policy);

        // Preprocess quantifiers: resolve against store, replace with literals.
        var expr = PreprocessQuantifiers(policy.Expression);

        var entityParam = new Parameter("entity", new TypeReference(Entity.Name));
        var pass = new DomainExpressionLoweringPass();
        var lowered = pass.Lower(expr, entityParam);

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
    ///       <item><b>Direct-execution</b> (<see cref="StageTransitionEffect"/>, <see cref="CreateEntityInstance"/>, <see cref="InvokeActionEffect"/>) → mutates instance state directly.</item>
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
    /// <param name="actionName">Name of the action to invoke.</param>
    /// <param name="args">Optional parameter values injected into the property
    /// bag during execution. Each key-value pair is available as a property
    /// in policy guards and assign RHS expressions. Values are cleaned up
    /// after the action completes.</param>
    public ActionInvocationResult InvokeAction(string actionName,
        IReadOnlyDictionary<string, object?>? args = null) {
        // E6.2: Depth-limited re-entrancy for nested invoke (self-call / OnEntry cycles).
        if (_invokeDepth >= MaxInvokeDepth)
            return ActionInvocationResult.InvokeDepthExceeded(actionName, MaxInvokeDepth);

        // Inject action args into the property bag for the duration of execution.
        var argKeys = new List<string>();
        if (args is { Count: > 0 }) {
            foreach (var kv in args) {
                _values[kv.Key] = kv.Value;
                argKeys.Add(kv.Key);
            }
        }

        _invokeDepth++;
        try {
            return InvokeActionInternal(actionName);
        }
        finally {
            _invokeDepth--;
            // Clean up injected args so subsequent calls don't see stale values.
            foreach (var key in argKeys)
                _values.Remove(key);
        }
    }

    /// <summary>
    /// Core action execution after args have been injected into <see cref="_values"/>.
    /// Domain-bound: catalog/helpers only; missing action map or stage structure throws.
    /// Standalone: structural entity/stage lookup only (reduced contract).
    /// </summary>
    private ActionInvocationResult InvokeActionInternal(string actionName) {
        AnalysisResult? runtimeAnalysis = null;
        Action? action;

        if (Domain is not null) {
            runtimeAnalysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
            // Fail closed: domain-bound dispatch requires catalog action map (no scan).
            if (runtimeAnalysis.GetActionResolution(Domain, Entity) is null)
                throw new InvalidOperationException(
                    $"Runtime dispatch requires {nameof(DomainCatalogMetadata)} action map for entity '{Entity.Name}' in domain '{Domain.Name}'.");
            runtimeAnalysis.TryResolveAction(Domain, Entity, CurrentStage, actionName, out action);
        }
        else {
            // Standalone reduced contract — structural SA only (see type remarks).
            action = ResolveStandaloneAction(actionName);
        }

        if (action is null)
            return ActionInvocationResult.Missing(Entity.Name, actionName);

        // ── Evaluate all guard policies ─────────────────────────
        var failures = new List<string>();
        foreach (var guard in action.Policies)
            if (!EvaluatePolicy(guard)) failures.Add(guard.Name);

        if (failures.Count > 0)
            return ActionInvocationResult.Blocked(actionName, failures);

        Stage? stage = null;
        if (runtimeAnalysis is not null && CurrentStage is not null) {
            // Fail closed: when analysis ran, a stage-guard lookup miss must not
            // silently skip the stage's policy guards. Unreachable for a
            // consistently-analyzed domain (CurrentStage is drawn from
            // Entity.Stages, which is the same source as ESM.StageByName), so this
            // only fires when analysis and instance disagree.
            var esm = runtimeAnalysis.GetMetadata<EntityStructureMetadata>(Entity);
            if (esm is null)
                throw new InvalidOperationException(
                    $"Runtime dispatch requires {nameof(EntityStructureMetadata)} for entity '{Entity.Name}' during action dispatch.");
            if (esm.StageByName is null || !esm.StageByName.TryGetValue(CurrentStage, out stage))
                throw new InvalidOperationException(
                    $"Stage '{CurrentStage}' not resolvable for entity '{Entity.Name}' during action dispatch.");
        }
        else if (Domain is null && CurrentStage is not null) {
            // Standalone reduced contract: stage policies from Entity.Stages only.
            stage = Entity.Stages.FirstOrDefault(
                s => string.Equals(s.Name, CurrentStage, StringComparison.Ordinal));
        }
        if (stage is not null)
            foreach (var guard in stage.Policies) {
                if (action.Policies.Any(p => string.Equals(p.Name, $"not_{guard.Name}", StringComparison.Ordinal)))
                    continue;
                if (!EvaluatePolicy(guard)) failures.Add(guard.Name);
            }

        if (failures.Count > 0)
            return ActionInvocationResult.Blocked(actionName, failures);

        foreach (var guard in Entity.Policies) {
            // Skip entity-level policies that are inverted by an action-level
            // "require not PolicyName" guard (synthetic not_PolicyName).
            // Otherwise the entity-level guard would redundantly block the action
            // even though the action explicitly opted out via "require not".
            if (action.Policies.Any(p => string.Equals(p.Name, $"not_{guard.Name}", StringComparison.Ordinal)))
                continue;
            if (!EvaluatePolicy(guard)) failures.Add(guard.Name);
        }

        if (failures.Count > 0)
            return ActionInvocationResult.Blocked(actionName, failures);

        // ── Execute effects ─────────────────────────────────────
        var subjectParam = new Parameter("entity", new TypeReference(Entity.Name));
        var loweringContext = new LoweringContext(
            subjectParam,
            Analysis: runtimeAnalysis,
            Domain: Domain);
        var effectPass = new EffectLoweringPass(Entity, loweringContext);
        // Action parameters are injected into _values for the call duration, but are not
        // entity schema properties. Compile with an action-scoped type def so PropertyAccess
        // to parameter names resolves (otherwise Member passthrough assigns the whole bag).
        var effectTypeProvider = action.Parameters.Count > 0
            ? BuildActionScopedTypeDefAnalyzer(action)
            : _typeDefAnalyzer;

        var createdBefore = _createdChildren.Count;
        foreach (var effect in action.Effects) {
            ExecuteEffect(effect, effectPass, effectTypeProvider);
        }

        // P3: declared -> Entity return = last child created this invoke of that type.
        DomainEntityInstance? resultInstance = null;
        string? resultTypeName = null;
        if (action.Result.Members.Count > 0) {
            resultTypeName = action.Result.Members[0].Type.TypeName;
            for (var i = _createdChildren.Count - 1; i >= createdBefore; i--) {
                if (string.Equals(_createdChildren[i].Entity.Name, resultTypeName, StringComparison.Ordinal)) {
                    resultInstance = _createdChildren[i];
                    break;
                }
            }
            if (resultInstance is null) {
                return ActionInvocationResult.MissingReturn(actionName, resultTypeName);
            }
        }

        return ActionInvocationResult.Ok(actionName, CurrentStage, resultInstance, resultTypeName);
    }

    /// <summary>
    /// Evaluates a list of <see cref="PropertyBinding"/> expressions against the
    /// current instance's property bag and returns the results as a dictionary.
    /// Each binding's expression is lowered, compiled, and executed via the VM.
    /// Returns null when <paramref name="bindings"/> is empty.
    /// </summary>
    private IReadOnlyDictionary<string, object?>? EvaluateParameterBindings(
        IReadOnlyList<PropertyBinding> bindings) {
        if (bindings is null || bindings.Count == 0) return null;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var subjectParam = new Parameter("entity", new TypeReference(Entity.Name));

        foreach (var binding in bindings) {
            var loweringPass = new DomainExpressionLoweringPass();
            var lowered = loweringPass.Lower(binding.Expression, subjectParam);
            var compiled = Interpreter.Compile(lowered, _typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { _values }));
            result[binding.PropertyName] = exec.Result.GetValue<object>();
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Executes a single effect. VM-executable effects go through
    /// lowering → compile → execute; direct-execution effects mutate
    /// the instance in place.
    /// </summary>
    private void ExecuteEffect(
        Effect effect,
        EffectLoweringPass effectPass,
        TypeDefinitionNodeAnalyzer typeProvider) {
        // Store-aware quantifiers / path-prefix / Rel exists must resolve before
        // Syntax lowering — same honesty as EvaluatePolicy (no bag pass-through).
        var prepared = PreprocessEffectExpressions(effect);
        var lowered = effectPass.TryLowerVmNode(prepared);
        if (lowered is not null) {
            var compiled = Interpreter.Compile(lowered, typeProvider);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { _values }));
            return;
        }

        EffectExecutor.Run(this, effectPass, typeProvider, prepared);
    }

    /// <summary>
    /// Rewrites effect expression trees so store-dependent forms become literals
    /// (or fail closed) before VM lowering.
    /// </summary>
    private Effect PreprocessEffectExpressions(Effect effect) => effect switch {
        AssignEffect a => a with { Value = PreprocessQuantifiers(a.Value) },
        ConditionalEffect c => c with {
            Condition = PreprocessQuantifiers(c.Condition),
            ThenEffects = c.ThenEffects.Select(PreprocessEffectExpressions).ToList(),
            ElseEffects = c.ElseEffects?.Select(PreprocessEffectExpressions).ToList()
        },
        CompositeEffect c => c with {
            Effects = c.Effects.Select(PreprocessEffectExpressions).ToList()
        },
        CreateEntityInstance cei => cei with {
            Initializers = cei.Initializers
                .Select(i => i with { Expression = PreprocessQuantifiers(i.Expression) })
                .ToList()
        },
        CreateEntityInRelationshipEffect cir => cir with {
            Initializers = cir.Initializers
                .Select(i => i with { Expression = PreprocessQuantifiers(i.Expression) })
                .ToList()
        },
        InvokeActionEffect iae => iae with {
            ParameterBindings = iae.ParameterBindings
                .Select(b => b with { Expression = PreprocessQuantifiers(b.Expression) })
                .ToList(),
            Filter = iae.Filter is null ? null : PreprocessQuantifiers(iae.Filter)
        },
        _ => effect
    };

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

        protected override object? StageTransition(StageTransitionEffect transition) {
            _instance.TransitionStage(transition.TargetStage.StageName, notifyStore: true);
            return null;
        }

        protected override object? CreateEntityInstance(CreateEntityInstance create) {
            return _instance.CreateChildInstance(create);
        }

        protected override object? CreateEntityInRelationship(CreateEntityInRelationshipEffect createIn) {
            return _instance.ExecuteCreateInRelationship(createIn);
        }

        protected override object? InvokeAction(InvokeActionEffect invoke) {
            _instance.ExecuteInvokeEffect(invoke);
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
        var hasCollectionQuantifier = invoke.Quantifier is StageSubscriptionQuantifier.Any
            or StageSubscriptionQuantifier.All;
        if (invoke.Quantifier is not null && !hasCollectionQuantifier) {
            throw new InvalidOperationException(
                "invoke does not support quantifier 'Each' (or unknown). Use any/all or omit.");
        }
        if (hasCollectionQuantifier && invoke.TargetRelationship is null) {
            throw new InvalidOperationException(
                $"invoke '{invoke.Quantifier}' requires a relationship target " +
                $"(e.g. invoke {invoke.Quantifier.ToString()!.ToLowerInvariant()} Rel.{invoke.ActionName}).");
        }
        if (invoke.Filter is not null &&
            (invoke.TargetRelationship is null || !hasCollectionQuantifier)) {
            throw new InvalidOperationException(
                "invoke 'where' requires any/all on a OneToMany relationship from the source.");
        }

        ActionInvocationResult nestedResult;
        if (invoke.TargetRelationship is not null && hasCollectionQuantifier) {
            var targets = GetRelatedTargets(invoke.TargetRelationship, invoke.Filter);
            if (targets.Count == 0) {
                throw new InvalidOperationException(
                    $"invoke {invoke.Quantifier.ToString()!.ToLowerInvariant()} " +
                    $"'{invoke.TargetRelationship}.{invoke.ActionName}' matched zero targets" +
                    (invoke.Filter is not null ? " after where filter" : "") + ".");
            }
            if (invoke.Quantifier == StageSubscriptionQuantifier.Any) {
                nestedResult = ActionInvocationResult.Missing(Entity.Name, invoke.ActionName);
                foreach (var t in targets) {
                    var r = t.InvokeAction(invoke.ActionName, chainedArgs);
                    if (r.Succeeded) { nestedResult = r; break; }
                }
            }
            else {
                nestedResult = ActionInvocationResult.Ok(invoke.ActionName, CurrentStage, null, null);
                foreach (var t in targets) {
                    var r = t.InvokeAction(invoke.ActionName, chainedArgs);
                    if (!r.Succeeded) {
                        nestedResult = r;
                        break;
                    }
                }
            }
        }
        else if (invoke.TargetRelationship is not null) {
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
                        ExecuteEffect(effect, exitPass, _typeDefAnalyzer);
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
                        ExecuteEffect(effect, entryPass, _typeDefAnalyzer);
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
                    .ToList(),
                Filter = iae.Filter is null ? null : BindPeerInExpression(iae.Filter, peerBinding, peer)
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
        var pass = new DomainExpressionLoweringPass();
        var lowered = pass.Lower(expr,
            new Parameter("entity", new TypeReference(peer.Entity.Name)));
        var compiled = Interpreter.Compile(lowered, peer._typeDefAnalyzer);
        using var exec = Interpreter.Execute(compiled,
            s => s.SetArgs(new object?[] { peer._values }));
        return exec.Result.GetValue<object>();
    }

    /// <summary>
    /// Creates a child entity instance from a <see cref="CreateEntityInstance"/>
    /// effect. Looks up the target entity by type name — first from the parent
    /// <see cref="Domain"/> if available, otherwise falls back to the current
    /// entity (same-type creation). Initializer expressions are evaluated
    /// against the <em>parent</em> instance and bound to the child's properties.
    /// </summary>
    private DomainEntityInstance CreateChildInstance(CreateEntityInstance createEffect) {
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
    private DomainEntityInstance ExecuteCreateInRelationship(CreateEntityInRelationshipEffect effect) {
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

        return CreateChildInstance(createEffect);
    }

    /// <summary>
    /// Returns all property names, values, and the current stage for debugging.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Snapshot() => _values.AsReadOnly();

    // ── Private helpers ─────────────────────────────────────────

    /// <summary>
    /// Standalone (<see cref="Domain"/> null) action resolve: structural stage then
    /// entity actions with SA fallthrough (empty stage-copy → entity action).
    /// Parameters are ignored in the empty-copy predicate (same as catalog path).
    /// </summary>
    private Action? ResolveStandaloneAction(string actionName) {
        Action? stageAction = null;
        if (CurrentStage is not null) {
            var currentStageRef = Entity.Stages
                .FirstOrDefault(s => string.Equals(s.Name, CurrentStage, StringComparison.Ordinal));
            stageAction = currentStageRef?.Actions
                .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));
        }

        var entityAction = Entity.Actions
            .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));

        if (stageAction is not null
            && stageAction.Effects.Count == 0
            && stageAction.Policies.Count == 0
            && entityAction is not null)
            return entityAction;

        return stageAction ?? entityAction;
    }

    /// <summary>
    /// Builds a dictionary-backed type definition for <paramref name="entityName"/>
    /// with the given schema properties (and optional extra action parameters).
    /// </summary>
    private static TypeDefinitionNodeAnalyzer BuildTypeDefAnalyzer(
        string entityName,
        IEnumerable<Property> properties,
        IEnumerable<Property>? extraProperties = null) {
        var propDefs = new List<PropertyDefinitionNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddProps(IEnumerable<Property> source) {
            foreach (var ep in source) {
                if (!seen.Add(ep.Name))
                    continue;
                var typeRef = MapDomainTypeToAstNode(ep.Type);
                propDefs.Add(new PropertyDefinitionNode(ep.Name, typeRef,
                    Getter: new PropertyGetterDefinitionNode()));
            }
        }

        AddProps(properties);
        if (extraProperties is not null)
            AddProps(extraProperties);

        var typeDefNode = new TypeDefinitionNode(
            Name: entityName,
            Properties: [.. propDefs],
            Namespace: null);

        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeDefNode);
        return analyzer;
    }

    /// <summary>
    /// Type provider that includes entity properties plus the current action's
    /// parameters so bag-injected args resolve as members during effect compile.
    /// </summary>
    private TypeDefinitionNodeAnalyzer BuildActionScopedTypeDefAnalyzer(
        Action action) =>
        BuildTypeDefAnalyzer(Entity.Name, Entity.Properties, action.Parameters);

    /// <summary>
    /// Resolves an outbound relationship by (this entity, name). Relationship identity
    /// is (source entity, name). Defense-in-depth for a source-scoped miss: when the
    /// name exists on a different source entity, report the precise cause instead of
    /// a generic not-found.
    /// </summary>
    private Relationship ResolveSourceRelationshipOrThrow(string relationshipName, string notFoundMessage) {
        if (Domain is null)
            throw new InvalidOperationException(notFoundMessage);

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        var rlm = analysis.GetRelationshipLookup(Domain);
        if (rlm is null)
            throw new InvalidOperationException(notFoundMessage);

        if (rlm.TryGetRelationship(Entity.Name, relationshipName, out var relationship))
            return relationship;

        var elsewhere = rlm.FindByNameAcrossSources(relationshipName).ToList();
        if (elsewhere.Count > 0)
            throw new InvalidOperationException(
                $"Entity '{Entity.Name}' is not the source of relationship '{relationshipName}'. " +
                $"Declared on: {string.Join(", ", elsewhere.Select(r => r.Source.TypeName))}.");
        throw new InvalidOperationException(notFoundMessage);
    }

    /// <summary>
    /// Resolves the singular target for a cross-entity invoke (E3b).
    /// Fail-closed: caller must be relationship source; exactly one outbound link.
    /// </summary>
    private DomainEntityInstance ResolveRelationshipTarget(string relationshipName) {
        var related = GetOutboundRelatedInstances(relationshipName);
        if (related.Count == 0)
            throw new InvalidOperationException(
                $"No linked instances found for relationship '{relationshipName}' on entity '{Entity.Name}'.");

        if (related.Count > 1)
            throw new InvalidOperationException(
                $"Relationship '{relationshipName}' has {related.Count} linked instances; " +
                "singular cross-entity invoke requires exactly one target.");

        return related[0];
    }

    /// <summary>
    /// Outbound links only (this instance as relationship source → targets).
    /// Reverse-side navigate is rejected (matches DMEFF007).
    /// </summary>
    private IReadOnlyList<DomainEntityInstance> GetOutboundRelatedInstances(string relationshipName) {
        if (Domain is null)
            throw new InvalidOperationException(
                $"Cannot resolve relationship '{relationshipName}' without a domain.");

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        // Catalog/RLM miss with analysis present is a genuine not-found — fail closed.
        // ResolveSourceRelationshipOrThrow also reports the precise cause when the
        // relationship exists on a different source entity (reverse-side invoke).
        var relationship = ResolveSourceRelationshipOrThrow(relationshipName,
            $"Relationship '{relationshipName}' not found in domain '{Domain.Name}'.");

        if (relationship.Cardinality is not (RelationshipCardinality.OneToOne or RelationshipCardinality.OneToMany))
            throw new InvalidOperationException(
                $"Cross-entity invoke on '{relationshipName}' ({relationship.Cardinality}) is not supported yet. " +
                "Use OneToOne or OneToMany from the source.");

        if (Store is null)
            throw new InvalidOperationException(
                "Cannot resolve relationship target without a DomainInstanceStore. " +
                "Call store.Add(instance) first.");

        // Source → target only (do not walk reverse links).
        return Store.GetRelatedInstances(relationshipName, this)
            .Where(t => string.Equals(t.Entity.Name, relationship.Target.TypeName, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Returns linked target instances for a cross-entity invoke, optionally
    /// filtered by a predicate expression evaluated against each target's bag.
    /// </summary>
    private IReadOnlyList<DomainEntityInstance> GetRelatedTargets(
        string relationshipName, DomainExpression? filter) {
        var all = GetOutboundRelatedInstances(relationshipName);
        if (filter is null || all.Count == 0) return all;

        var result = new List<DomainEntityInstance>();
        foreach (var t in all) {
            var loweringPass = new DomainExpressionLoweringPass();
            var lowered = loweringPass.Lower(filter,
                new Parameter("entity", new TypeReference(t.Entity.Name)));
            var compiled = Interpreter.Compile(lowered, t._typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { t._values }));
            if (exec.Result.GetValue<bool>())
                result.Add(t);
        }
        return result;
    }

    // ── Collection quantifier preprocessing ──────────────────────────

    /// <summary>
    /// Walks an expression tree and evaluates collection quantifier nodes
    /// (AnyExpr/AllExpr/NoneExpr/CountExpr) and to-one
    /// <see cref="RelationshipNavigation"/> against the current store,
    /// replacing them with literal results. Non-store nodes are
    /// returned unchanged (or with preprocessed children for composites).
    ///
    /// <para>Fail-closed: missing <see cref="Store"/>, missing relationship
    /// metadata, or missing outbound link throws
    /// <see cref="InvalidOperationException"/> — no soft pass-through to bag
    /// <c>Member</c> chains (no vacuous true/false).</para>
    /// </summary>
    private DomainExpression PreprocessQuantifiers(DomainExpression expr) =>
        QuantifierRewrite.Route(expr);

    /// <summary>
    /// Store-aware quantifier / path-prefix / Rel-exists preprocessing (coh-d1 —
    /// leaf override on the shared <see cref="DomainExpressionRewriteBase"/>;
    /// composites recurse in the base). Fail-closed: missing store/relationship
    /// metadata or missing outbound links throw (no vacuous true/false).
    /// </summary>
    private sealed class QuantifierPreprocessRewrite(DomainEntityInstance instance)
        : DomainExpressionRewriteBase {
        private readonly DomainEntityInstance _instance = instance;

        protected override DomainExpression AnyExpr(AnyExpr e) =>
            DomainExpression.Literal(_instance.EvaluateAnyExpr(e));
        protected override DomainExpression AllExpr(AllExpr e) =>
            DomainExpression.Literal(_instance.EvaluateAllExpr(e));
        protected override DomainExpression NoneExpr(NoneExpr e) =>
            DomainExpression.Literal(_instance.EvaluateNoneExpr(e));
        protected override DomainExpression CountExpr(CountExpr e) =>
            DomainExpression.Literal(_instance.EvaluateCountExpr(e));

        protected override DomainExpression RelationshipNavigation(RelationshipNavigation r) {
            // Singular path-prefix (to-one hops). Nested navs (loan book Title) hop
            // on the linked target — not re-routed on the original instance (P2).
            // Use quantifiers (any/all) for collections — never pick targets[0] silently.
            var result = EvaluatePathPrefixChain(r, _instance);
            return DomainExpression.Literal(result);
        }

        /// <summary>
        /// Walk to-one path-prefix hops; evaluate the leaf expression on the final bag.
        /// </summary>
        private static object? EvaluatePathPrefixChain(
            RelationshipNavigation r, DomainEntityInstance source) {
            var targets = source.GetOutboundRelatedInstances(r.RelationshipName);
            if (targets.Count == 0)
                throw new InvalidOperationException(
                    $"No linked instances found for relationship '{r.RelationshipName}' on entity '{source.Entity.Name}'.");
            if (targets.Count > 1)
                throw new InvalidOperationException(
                    $"Path-prefix on relationship '{r.RelationshipName}' requires exactly one linked target " +
                    $"(found {targets.Count} on entity '{source.Entity.Name}'). Use any/all quantifiers for collections.");

            var hop = targets[0];
            if (r.TargetProperty is RelationshipNavigation nested)
                return EvaluatePathPrefixChain(nested, hop);

            // Leaf (comparison, property, etc.) on the final hop instance.
            var pass = new DomainExpressionLoweringPass();
            var lowered = pass.Lower(r.TargetProperty,
                new Parameter("entity", new TypeReference(hop.Entity.Name)));
            var compiled = Interpreter.Compile(lowered, hop._typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { hop._values }));
            return exec.Result.GetValue<object>();
        }

        protected override DomainExpression Exists(Exists e) {
            // Rel exists: store outbound-link presence when Target is a relationship name.
            // Fail closed without store (GetOutboundRelatedInstances). Empty links → false.
            // Non-relationship targets keep bag-null lowering.
            if (_instance.TryEvaluateRelationshipPresence(e.Target, out var present))
                return DomainExpression.Literal(present);
            return base.Exists(e);
        }

        protected override DomainExpression NotExists(NotExists e) {
            if (_instance.TryEvaluateRelationshipPresence(e.Target, out var present))
                return DomainExpression.Literal(!present);
            return base.NotExists(e);
        }
    }

    /// <summary>
    /// When <paramref name="target"/> is a bare relationship name on this entity as source,
    /// evaluates store-linked presence (count &gt; 0). Returns false from <c>out present</c>
    /// with return false when the target is not an outbound relationship (caller uses bag path).
    /// </summary>
    private bool TryEvaluateRelationshipPresence(DomainExpression target, out bool present) {
        present = false;
        if (target is not PropertyAccess pa)
            return false;
        if (Domain is null)
            return false;

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        if (!analysis.TryGetRelationship(Domain, Entity.Name, pa.Name, out var relationship) || relationship is null)
            return false;
        if (!string.Equals(relationship.Source.TypeName, Entity.Name, StringComparison.Ordinal))
            return false;

        // Outbound relationship: require store; empty links are false (not throw).
        var targets = GetOutboundRelatedInstances(pa.Name);
        present = targets.Count > 0;
        return true;
    }

    private bool EvaluateAnyExpr(AnyExpr a) {
        var targets = GetOutboundRelatedInstances(a.RelationshipName);
        foreach (var t in targets) {
            if (EvaluateBodyOnTarget(a.Body, t))
                return true;
        }
        return false;
    }

    private bool EvaluateAllExpr(AllExpr a) {
        var targets = GetOutboundRelatedInstances(a.RelationshipName);
        if (targets.Count == 0) return false; // no vacuous all
        foreach (var t in targets) {
            if (!EvaluateBodyOnTarget(a.Body, t))
                return false;
        }
        return true;
    }

    private bool EvaluateNoneExpr(NoneExpr n) {
        return !EvaluateAnyExpr(new AnyExpr(n.RelationshipName, n.Body));
    }

    private long EvaluateCountExpr(CountExpr c) {
        var targets = GetOutboundRelatedInstances(c.RelationshipName);
        if (c.Body is null) return targets.Count;

        long count = 0;
        foreach (var t in targets) {
            if (EvaluateBodyOnTarget(c.Body, t))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Lowers, compiles, and executes a body expression against a target
    /// instance's property bag and type definition. Returns the boolean
    /// result. This is the same pattern used by <see cref="GetRelatedTargets"/>.
    /// </summary>
    private static bool EvaluateBodyOnTarget(DomainExpression body, DomainEntityInstance target) {
        var pass = new DomainExpressionLoweringPass();
        var lowered = pass.Lower(body,
            new Parameter("entity", new TypeReference(target.Entity.Name)));
        var compiled = Interpreter.Compile(lowered, target._typeDefAnalyzer);
        using var exec = Interpreter.Execute(compiled,
            s => s.SetArgs(new object?[] { target._values }));
        return exec.Result.GetValue<bool>();
    }

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
public sealed record ActionInvocationResult {
    private ActionInvocationResult() { }

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

    /// <summary>
    /// When the action declared <c>-&gt; EntityType</c> and succeeded, the created
    /// instance of that type from this invoke (product vertical). Null for void actions.
    /// </summary>
    public DomainEntityInstance? ResultInstance { get; private init; }

    /// <summary>Declared return type name when a result instance is present (or missing-return error).</summary>
    public string? ResultTypeName { get; private init; }

    internal static ActionInvocationResult Ok(
        string actionName,
        string? newStage,
        DomainEntityInstance? resultInstance = null,
        string? resultTypeName = null) => new() {
            ActionName = actionName,
            Succeeded = true,
            NewStage = newStage,
            ResultInstance = resultInstance,
            ResultTypeName = resultTypeName ?? resultInstance?.Entity.Name
        };

    internal static ActionInvocationResult MissingReturn(string actionName, string expectedType) => new() {
        ActionName = actionName,
        Succeeded = false,
        ResultTypeName = expectedType,
        ErrorMessage =
            $"Action '{actionName}' declared return type '{expectedType}' but no create/create-in " +
            "produced an instance of that type during this invoke."
    };

    internal static ActionInvocationResult Blocked(string actionName, List<string> failures) => new() {
        ActionName = actionName,
        Succeeded = false,
        FailedGuards = failures.AsReadOnly()
    };

    internal static ActionInvocationResult Missing(string entityName, string actionName) => new() {
        ActionName = actionName,
        Succeeded = false,
        ErrorMessage = $"Action '{actionName}' not found on entity '{entityName}'."
    };

    internal static ActionInvocationResult InvokeDepthExceeded(string actionName, int maxDepth) => new() {
        ActionName = actionName,
        Succeeded = false,
        ErrorMessage =
            $"Action invoke depth exceeded (max {maxDepth}) while calling '{actionName}'. " +
            "Possible recursive invoke cycle (e.g. action → invoke self, or OnEntry → invoke → transition loops)."
    };
}