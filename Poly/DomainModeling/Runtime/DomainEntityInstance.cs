using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

using Action = Poly.DomainModeling.Ontology.Action;
using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Runtime;

public sealed partial record DomainEntityInstance {
    private readonly Dictionary<string, object?> _values;
    private readonly TypeDefinitionNodeAnalyzer _typeDefAnalyzer;
    private readonly List<DomainEntityInstance> _createdChildren = [];
    private bool _isExecutingSubscription;
    private int _invokeDepth;
    private int _transitionDepth;
    private TypeDefinitionNodeAnalyzer? _bindingTypeProvider;
    /// <summary>Max nested <see cref="InvokeAction"/> depth (self-invoke / re-entrancy).</summary>
    public const int MaxInvokeDepth = 16;
    /// <summary>Max nested <see cref="TransitionStage"/> depth (OnEntry/OnExit re-entrancy).</summary>
    public const int MaxTransitionDepth = 16;
    public DomainInstanceStore? Store { get; internal set; }

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

    private const string CurrentStageBagKey = "CurrentStage";

    /// <summary>The current lifecycle stage, if the entity defines stages.
    /// Backed by the property bag so VM Assignment of CurrentStage (the same
    /// tree emit consumes) updates the field the rest of the runtime reads.</summary>
    public string? CurrentStage {
        get => _values.TryGetValue(CurrentStageBagKey, out var v) ? v as string : null;
        private set {
            if (value is null) _values.Remove(CurrentStageBagKey);
            else _values[CurrentStageBagKey] = value;
        }
    }

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
                values[prop.Name] = EvaluateDefaultValue(defaultValue.Expression, prop.Type.TypeName, domain);
            }
            else {
                values[prop.Name] = null; // default for unspecified properties
            }
        }

        var typeDefAnalyzer = BuildTypeDefAnalyzer(entity, domain: domain);

        // Enforce constraints at creation, matching the C# export's Create factory guards.
        // The runtime previously accepted out-of-range/pattern-violating/empty-required
        // values silently while the export rejected them — a divergence (round-1 C-F3).
        var validationError = ValidateConstraints(entity, values, domain: domain);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        // Initial stage: first declared stage name (factory shape; not a semantic rediscovery).
        var currentStage = entity.Stages.FirstOrDefault()?.Name;

        var instance = new DomainEntityInstance(entity, values, typeDefAnalyzer, currentStage, domain);
        ApplyInitialStageEntryEffects(instance);
        return instance;
    }

    /// <summary>
    /// Fills missing/null scalar slots from <c>default(...)</c> before create
    /// probe/validate. Store jobs see the same bag <see cref="Create"/> will store
    /// — unique-from-default and required-default must not skip the probe.
    /// </summary>
    internal static Dictionary<string, object?> FillCreateDefaults(
        Entity entity,
        IReadOnlyDictionary<string, object?> values,
        Domain? domain) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        var filled = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        foreach (var prop in entity.Properties) {
            filled.TryGetValue(prop.Name, out var existing);
            if (existing is not null)
                continue;
            if (prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault() is not { } def)
                continue;
            filled[prop.Name] = EvaluateDefaultValue(def.Expression, prop.Type.TypeName, domain);
        }
        return filled;
    }

    /// <summary>
    /// Validates required/range/length/pattern/unique constraints against the to-be-stored
    /// values, mirroring the C# export's <c>Create</c> factory guards. Returns the first
    /// violation message, or null when the values are valid. Unique is checked only when
    /// <paramref name="store"/> is set — before any mutate, not after <c>TryAdd</c>.
    /// </summary>
    private static string? ValidateConstraints(
        Entity entity,
        IReadOnlyDictionary<string, object?> values,
        DomainInstanceStore? store = null,
        Domain? domain = null) {
        foreach (var prop in entity.Properties) {
            values.TryGetValue(prop.Name, out var v);
            foreach (var constraint in prop.Constraints) {
                switch (constraint) {
                    case RequiredConstraint:
                        if (IsText(prop) && string.IsNullOrEmpty(v as string))
                            return $"'{prop.Name}' is required.";
                        if (v is null && IsNullableDomainTypeName(prop.Type.TypeName, domain))
                            return $"'{prop.Name}' is required.";
                        break;
                    case RangeConstraint r:
                        if (v is IConvertible num && v is not bool and not string && v is not Guid and not DateTime and not DateOnly) {
                            var dv = Convert.ToDecimal(num);
                            if (r.Minimum is not null && dv < Convert.ToDecimal(r.Minimum))
                                return $"'{prop.Name}' must be >= {r.Minimum}.";
                            if (r.Maximum is not null && dv > Convert.ToDecimal(r.Maximum))
                                return $"'{prop.Name}' must be <= {r.Maximum}.";
                        }
                        break;
                    case LengthConstraint lc:
                        if (v is string s) {
                            if (s.Length < lc.MinLength)
                                return $"'{prop.Name}' must be at least {lc.MinLength} characters.";
                            if (lc.MaxLength < int.MaxValue && s.Length > lc.MaxLength)
                                return $"'{prop.Name}' must be at most {lc.MaxLength} characters.";
                        }
                        break;
                    case PatternConstraint pc:
                        if (v is string ps && !Regex.IsMatch(ps, pc.Pattern))
                            return $"'{prop.Name}' does not match the required pattern.";
                        break;
                    case UniqueConstraint:
                        if (store is not null && v is not null) {
                            var unique = store.UniqueCollisionMessage(
                                entity, values, except: null, candidate: null);
                            if (unique is not null)
                                return unique;
                        }
                        break;
                }
            }
        }
        return null;
    }

    private static bool IsText(Property prop) =>
        prop.Type.TypeName is "Text" or "String";

    private static bool IsNullableDomainTypeName(string typeName, Domain? domain) {
        if (domain?.Types.OfType<EnumType>().Any(e =>
                string.Equals(e.Name, typeName, StringComparison.Ordinal)) == true)
            return false;
        var mapping = RuntimeAnalysisCache.FindMapping(domain, typeName);
        if (mapping is not null)
            return !mapping.IsNonNullableValueType;
        return !DomainTypeMapping.IsNonNullableClrValueType(
            RuntimeAnalysisCache.ClrTypeName(domain, typeName));
    }

    /// <summary>
    /// Applies the first stage's entry effects at creation time, matching the export's
    /// constructor (DomainToCSharpExporter applies the initial stage's entry effects in
    /// the ctor). Without this, a property initialized by the first stage's <c>entry</c>
    /// block (status stamps, IsOpen flags, timestamps) is null at runtime but set in the
    /// export — divergent initial state.
    /// </summary>
    private static void ApplyInitialStageEntryEffects(DomainEntityInstance instance) {
        var firstStage = instance.Entity.Stages.FirstOrDefault();
        if (firstStage?.OnEntryEffects is not { Count: > 0 })
            return;

        var entryEffects = firstStage.OnEntryEffects
            .Where(e => e is not StageTransitionEffect)
            .ToList();
        if (entryEffects.Count == 0)
            return;
        ThrowIfEffectListFailed(
            instance.ExecuteEffectList(entryEffects, instance._typeDefAnalyzer,
                entryStageName: firstStage.Name),
            "first-stage OnEntry");
    }

    /// <summary>
    /// Evaluates a DSL default expression to a concrete runtime value, adapted to
    /// the target property's CLR type when known (discovery round5 F1–F3).
    /// The runtime stores enum-typed properties as strings, so an enum member name
    /// (e.g. <c>default(Active)</c>) lowers to its name string; clocks evaluate via
    /// session ident rewrite then Meaning.Defaults; <c>guid</c> is core.
    /// Matches the C# export's defaulted optional ctor params.
    /// </summary>
    private object? EvaluateDefaultValue(DomainExpression expr, string? propTypeName = null) =>
        EvaluateDefaultValue(expr, propTypeName, Domain);

    private static object? EvaluateDefaultValue(DomainExpression expr, string? propTypeName, Domain? domain) {
        var meaning = RuntimeAnalysisCache.MeaningFor(domain);
        var forms = RuntimeAnalysisCache.FormsFor(domain);
        if (expr is PropertyAccess ident && forms.TryRewriteIdent(ident.Name, out var claimed))
            expr = claimed;
        if (meaning.Defaults.TryResolve(expr, propTypeName, out var runtime, out _))
            return runtime;
        return expr switch {
            Literal lit => lit.Value,
            PropertyAccess pa => pa.Name switch {
                "Guid" => propTypeName is "Text" or "String"
                    ? Guid.NewGuid().ToString()
                    : Guid.NewGuid(),
                _ => pa.Name
            },
            _ => throw new InvalidOperationException(
                $"Cannot evaluate default expression of type '{expr.GetType().Name}'.")
        };
    }

    /// <summary>
    /// Action resolution missed. Distinguish a genuinely-unknown action from one
    /// that exists but is stage-scoped to a different stage than the current one —
    /// the latter is reported precisely ("only available in stage 'X'") instead of
    /// the misleading "not found on entity", matching the export's guard message.
    /// </summary>
    private ActionInvocationResult ReportUnresolvedAction(string actionName, AnalysisResult? runtimeAnalysis) {
        string? stageName = null;
        if (Domain is not null && runtimeAnalysis is not null) {
            var arm = runtimeAnalysis.GetActionResolution(Domain, Entity);
            if (arm is not null) {
                foreach (var (stage, actions) in arm.StageActions) {
                    if (actions.ContainsKey(actionName)) {
                        stageName = stage;
                        break;
                    }
                }
            }
        }
        else {
            stageName = Entity.Stages
                .FirstOrDefault(s => s.Actions.Any(a =>
                    string.Equals(a.Name, actionName, StringComparison.Ordinal)))
                ?.Name;
        }
        return stageName is not null
            ? ActionInvocationResult.StageRequired(Entity.Name, actionName, stageName)
            : ActionInvocationResult.Missing(Entity.Name, actionName);
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
        if (Store is not null) {
            var unique = Store.EnsureUnique(this, name, value);
            if (!unique.IsSuccess)
                throw new InvalidOperationException(
                    unique.ErrorMessage ?? "Unique constraint violated.");
        }
        _values[name] = value;
    }

    internal bool TryGetRaw(string name, out object? value) =>
        _values.TryGetValue(name, out value);

    internal static string? ValidateCreateConstraints(
        Entity entity,
        IReadOnlyDictionary<string, object?> values,
        DomainInstanceStore? store = null,
        Domain? domain = null) =>
        ValidateConstraints(entity, values, store, domain);

    internal void TrackCreatedChild(DomainEntityInstance child) =>
        _createdChildren.Add(child);

    internal void UntrackCreatedChild(DomainEntityInstance child) =>
        _createdChildren.Remove(child);

    internal Relationship ResolveCreateInRelationship(string relationshipName) =>
        ResolveSourceRelationshipOrThrow(relationshipName,
            Domain is null
                ? $"Relationship '{relationshipName}' not found."
                : $"Relationship '{relationshipName}' not found in domain '{Domain.Name}'.");

    /// <summary>
    /// Evaluates <paramref name="policy"/> against this instance using the
    /// VM (direct AST lowering — canonical path). Returns <c>true</c> if the
    /// policy's guard expression is satisfied.
    /// Store-aware expressions lower in-tree (nav Member reads / Store jobs).
    /// </summary>
    public bool EvaluatePolicy(Policy policy) {
        ArgumentNullException.ThrowIfNull(policy);

        var expr = policy.Expression;

        // Slice B: execute never lowers. Policy bodies come from GetOrLower only.
        if (Domain is null)
            throw new InvalidOperationException(
                $"Cannot evaluate policy '{policy.Name}' on '{Entity.Name}' without a Domain-bound module.");

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        RuntimeAnalysisCache.GetOrLower(Domain, RuntimeAnalysisCache.Session(Domain), analysis);
        if (!RuntimeAnalysisCache.TryGetPolicyBody(Domain, Entity.Name, policy.Name, out var cached)
            || cached is null)
            throw new InvalidOperationException(
                $"Policy body '{policy.Name}' is missing on entity '{Entity.Name}'.");
        // BindExportBody is identity for Parameter-shaped policy trees.
        var boundPolicy = BindExportBody(cached);
        var compiledModule = Interpreter.CompileChecked(boundPolicy, _typeDefAnalyzer);
        using var execModule = Interpreter.Execute(compiledModule,
            s => s.SetArgs(new object?[] { this }));
        var boxedModule = BoxPathPrefixLeaf(expr, execModule.Result.GetValue<object>());
        return CoercePolicyBool(policy.Name, boxedModule);
    }

    private static bool CoercePolicyBool(string policyName, object? boxed) => boxed switch {
        bool b => b,
        long l => l != 0L,
        int i => i != 0,
        null => false,
        _ => throw new InvalidOperationException(
            $"Policy '{policyName}' produced {boxed.GetType().Name}, not a boolean.")
    };


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
    ///       <item><b>VM-compiled</b> (<see cref="AssignEffect"/>, <see cref="CompositeEffect"/>, <see cref="ConditionalEffect"/>, <see cref="StageTransitionEffect"/>) → lowered to Syntax AST → compiled via <see cref="Interpreter.Compile"/> → executed via VM. Unique assign is <c>EnsureUnique</c> then Assignment. StageTransition is Assignment of CurrentStage + Invoke Notify on This.</item>
    ///       <item><b>Create / create-in</b> → instance factories via InvokeNamed (guarded-probe + body for mixed if+create; not EffectExecutor). Self-invoke and singular cross-entity invoke lower to <c>Invoke(Member(…))</c>.</item>
    ///     </list>
    ///   </item>
    ///   <item>On <see cref="StageTransitionEffect"/>: lowered tree sets stage then <c>Invoke(Member(This, "Notify"))</c> (store fan-out in finally).</item>
    /// </list>
    ///
    /// <para><b>VM-executable effects</b> (<see cref="AssignEffect"/>,
    /// <see cref="CompositeEffect"/>, <see cref="ConditionalEffect"/>) are
    /// lowered to Syntax AST, compiled, and executed via the VM.</para>
    ///
    /// <para>Create / create-in share a lowered tree with emit (Store jobs
    /// <c>Create</c> / <c>CreateIn</c> / <c>ProbeCreate</c>). <see cref="StageTransitionEffect"/> and invoke (self,
    /// cross-entity, for-each) also share the lowered tree with emit.</para>
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

        var injectedKeys = new List<string>();
        _invokeDepth++;
        try {
            return InvokeActionInternal(actionName, args, injectedKeys);
        }
        finally {
            _invokeDepth--;
            foreach (var key in injectedKeys)
                _values.Remove(key);
        }
    }

    /// <summary>
    /// Core action execution after args have been injected into <see cref="_values"/>.
    /// Domain-bound: catalog/helpers only; missing action map or stage structure throws.
    /// Standalone: structural entity/stage lookup only (reduced contract).
    /// </summary>
    private ActionInvocationResult InvokeActionInternal(
        string actionName,
        IReadOnlyDictionary<string, object?>? args,
        List<string> injectedKeys) {
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
            return ReportUnresolvedAction(actionName, runtimeAnalysis);

        var declared = action.Parameters
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (args is { Count: > 0 }) {
            foreach (var key in args.Keys) {
                if (!declared.Contains(key))
                    return ActionInvocationResult.InvalidArguments(actionName,
                        $"Unknown argument '{key}' for action '{actionName}'.");
            }
        }
        foreach (var param in action.Parameters) {
            if (args is null || !args.ContainsKey(param.Name))
                return ActionInvocationResult.InvalidArguments(actionName,
                    $"Missing argument '{param.Name}' for action '{actionName}'.");
        }
        if (args is { Count: > 0 }) {
            foreach (var kv in args) {
                _values[kv.Key] = kv.Value;
                injectedKeys.Add(kv.Key);
            }
        }

        // ── Evaluate all guard policies ─────────────────────────
        // When a Domain-bound module method owns the action, require gates
        // (path-prefix "requires a linked" Failure + policy bools) live in that
        // tree — skip the EvaluatePolicy prelude so ONE-TREE Failure runs and
        // require-not cannot invert soft-false to fail-open. ExecuteEffectList
        // still binds the module Body for named actions even when Ontology
        // effects are empty (gated no-op). Bare evaluate_policy still soft-fails
        // unlinked via ExistsRelated. Stage policies stay here.
        var failures = new List<string>();
        if (Domain is not null) {
            var ensureAnalysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
            RuntimeAnalysisCache.GetOrLower(Domain, RuntimeAnalysisCache.Session(Domain), ensureAnalysis);
        }
        var moduleOwnsRequire = Domain is not null
            && RuntimeAnalysisCache.TryGetModuleMethod(Domain, Entity.Name, actionName, out var moduleMethod)
            && moduleMethod?.Body is not null;
        if (!moduleOwnsRequire) {
            foreach (var guard in action.Policies)
                if (!EvaluatePolicy(guard)) failures.Add(guard.Name);

            if (failures.Count > 0)
                return ActionInvocationResult.Blocked(actionName, failures);
        }

        Stage? stage = null;
        if (runtimeAnalysis is not null && CurrentStage is not null) {
            if (!runtimeAnalysis.TryGetStage(Entity, CurrentStage, out stage) || stage is null)
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

        // ── Execute effects ─────────────────────────────────────
        // Action parameters are injected into _values for the call duration, but are not
        // entity schema properties. Compile with an action-scoped type def so PropertyAccess
        // to parameter names resolves (otherwise Member passthrough assigns the whole bag).
        var effectTypeProvider = action.Parameters.Count > 0
            ? BuildActionScopedTypeDefAnalyzer(action)
            : _typeDefAnalyzer;
        var previousBindingProvider = _bindingTypeProvider;
        _bindingTypeProvider = effectTypeProvider;
        try {
            var createdBefore = _createdChildren.Count;
            var bagBefore = new Dictionary<string, object?>(_values, StringComparer.Ordinal);
            var stageBefore = CurrentStage;
            var failed = ExecuteEffectList(action.Effects, effectTypeProvider,
                actionName: action.Name, args: args, actionParameters: action.Parameters);
            if (failed is { IsSuccess: false }) {
                // Unique-before-mutate restore (PR 44 F2). Other constraint Failures
                // keep prior assigns — PR 43 documented miss IfOnMutatedProperty.
                if (failed.ErrorMessage is string msg && msg.Contains("Unique", StringComparison.Ordinal))
                    RestoreActionState(bagBefore, stageBefore, createdBefore);
                return MapModuleRequireFailure(actionName, action, failed.ErrorMessage);
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
        finally {
            _bindingTypeProvider = previousBindingProvider;
        }
    }


    /// <summary>
    /// Maps module <c>DomainResult.Failure</c> require messages to
    /// <see cref="ActionInvocationResult"/> — blocked-by-policy → FailedGuards;
    /// requires-a-linked → ErrorMessage + FailedGuards (ONE-TREE + harness).
    /// </summary>
    private static ActionInvocationResult MapModuleRequireFailure(
        string actionName, Action action, string? message) {
        message ??= "invoke failed.";
        const string blockedPrefix = "blocked by policy '";
        var blockedIdx = message.IndexOf(blockedPrefix, StringComparison.Ordinal);
        if (blockedIdx >= 0) {
            var start = blockedIdx + blockedPrefix.Length;
            var end = message.IndexOf('\'', start);
            if (end > start) {
                var policyLeaf = message[start..end];
                var guardName = action.Policies
                    .Select(p => p.Name)
                    .FirstOrDefault(n =>
                        string.Equals(n, policyLeaf, StringComparison.Ordinal)
                        || string.Equals(n, "not_" + policyLeaf, StringComparison.Ordinal))
                    ?? policyLeaf;
                return ActionInvocationResult.Blocked(actionName, [guardName]);
            }
        }

        if (message.Contains("requires a linked", StringComparison.Ordinal)) {
            var guards = action.Policies.Select(p => p.Name).ToList();
            return ActionInvocationResult.RequireFailure(actionName, message, guards);
        }

        return ActionInvocationResult.InvalidArguments(actionName, message);
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
            if (TryEvalActionParamPath(binding.Expression, out var fromParam)) {
                result[binding.PropertyName] = fromParam;
                continue;
            }
            var loweringPass = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity"), Domain: Domain));
            var lowered = loweringPass.Lower(binding.Expression, subjectParam);
            var compiled = Interpreter.Compile(lowered, _bindingTypeProvider ?? _typeDefAnalyzer);
            using var exec = Interpreter.Execute(compiled,
                s => s.SetArgs(new object?[] { this }));
            result[binding.PropertyName] = BoxPathPrefixLeaf(
                binding.Expression, exec.Result.GetValue<object>());
        }

        return result.Count > 0 ? result : null;
    }

    private static void ThrowIfEffectListFailed(DomainResult? failed, string context) {
        if (failed is { IsSuccess: false })
            throw new InvalidOperationException(
                failed.ErrorMessage ?? $"{context} failed.");
    }

    /// <summary>
    /// One operation AST through <see cref="Interpreter"/>. Named actions always
    /// bind <see cref="MethodDefinitionNode.Body"/> from the cached module — never
    /// <c>LowerActionBody</c> (Ontology residual: dual-path execute is a bug).
    /// Domain-bound OnEntry/OnExit batches bind GetOrLower export-shaped bodies
    /// (BindThis) / module methods / mixed-list segment bodies and throw on miss.
    /// Slice B: execute never lowers; Domain-null fail-closed.
    /// </summary>
    private DomainResult? ExecuteEffectList(
        IReadOnlyList<Effect> effects,
        TypeDefinitionNodeAnalyzer typeProvider,
        string? actionName = null,
        string? entryStageName = null,
        string? exitStageName = null,
        IReadOnlyDictionary<string, object?>? args = null,
        IReadOnlyList<Property>? actionParameters = null,
        object? peerArg = null,
        int? entryExitSegmentIndex = null) {
        // Named actions always bind the module Body (require Failure + Success),
        // even when Ontology effects are empty — gated no-ops still run guards
        // (Final Boss F9: empty-effects must not skip module require).
        if (effects.Count == 0 && actionName is null
            && entryStageName is null && exitStageName is null)
            return null;

        Node? tree;
        if (actionName is not null) {
            // Named action: bind module or throw. Never fall back to Effect IR lower.
            if (Domain is null)
                throw new InvalidOperationException(
                    $"Cannot invoke '{actionName}' on '{Entity.Name}' without a Domain-bound module.");
            var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
            RuntimeAnalysisCache.GetOrLower(Domain, RuntimeAnalysisCache.Session(Domain), analysis);
            if (!RuntimeAnalysisCache.TryGetModuleMethod(Domain, Entity.Name, actionName, out var method)
                || method?.Body is null)
                throw new InvalidOperationException(
                    $"Module method '{actionName}' is missing on type '{Entity.Name}'.");
            tree = BindModuleMethodBody(method);
        }
        else if (Domain is not null) {
            var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
            RuntimeAnalysisCache.GetOrLower(Domain, RuntimeAnalysisCache.Session(Domain), analysis);
            var hasStageName = entryStageName is not null || exitStageName is not null;
            if (hasStageName && entryExitSegmentIndex is int segmentIndex) {
                var kind = exitStageName is not null ? "exit" : "entry";
                var stageName = exitStageName ?? entryStageName
                    ?? throw new InvalidOperationException("Segment flush requires a stage name.");
                if (!RuntimeAnalysisCache.TryGetEntryExitSegmentBody(
                        Domain, Entity.Name, stageName, kind, segmentIndex, out var segmentBody)
                    || segmentBody is null) {
                    throw new InvalidOperationException(
                        $"Entry/exit segment body {kind} '{stageName}'[{segmentIndex}] is missing on entity '{Entity.Name}'.");
                }
                tree = BindExportBody(segmentBody);
            }
            else if (hasStageName) {
                if (exitStageName is not null
                    && RuntimeAnalysisCache.TryGetEntryExitBody(
                        Domain, Entity.Name, exitStageName, "exit", out var exitBody)
                    && exitBody is not null) {
                    // EntryExitBodies are export-shaped (UseThis); bind for VM SetArgs.
                    tree = BindExportBody(exitBody);
                }
                else if (entryStageName is not null
                    && RuntimeAnalysisCache.TryGetEntryExitBody(
                        Domain, Entity.Name, entryStageName, "entry", out var entryBody)
                    && entryBody is not null) {
                    tree = BindExportBody(entryBody);
                }
                else if (exitStageName is not null
                    && RuntimeAnalysisCache.TryGetExitMethod(Domain, Entity.Name, exitStageName, out var exit)
                    && exit?.Body is not null) {
                    tree = BindModuleMethodBody(exit);
                }
                else if (entryStageName is not null
                    && RuntimeAnalysisCache.TryGetEntryMethod(Domain, Entity.Name, entryStageName, out var entry)
                    && entry?.Body is not null) {
                    tree = BindModuleMethodBody(entry);
                }
                else {
                    var kind = exitStageName is not null ? $"OnExit '{exitStageName}'" : $"OnEntry '{entryStageName}'";
                    throw new InvalidOperationException(
                        $"Entry/exit body {kind} is missing on entity '{Entity.Name}'.");
                }
            }
            else {
                throw new InvalidOperationException(
                    $"Domain-bound effect list on '{Entity.Name}' requires a cached entry/exit body " +
                    "or mixed-list segment from GetOrLower.");
            }
        }
        else {
            throw new InvalidOperationException(
                $"Cannot execute effect list on '{Entity.Name}' without a Domain-bound module.");
        }
        if (tree is null)
            throw new InvalidOperationException(
                "Cannot lower effect list to a Syntax AST.");
        var compiled = Interpreter.CompileChecked(
            tree, ModuleAwareTypeProvider(typeProvider, actionParameters));
        using var exec = Interpreter.Execute(compiled,
            s => s.SetArgs(peerArg is null
                ? new object?[] { this }
                : new object?[] { this, peerArg }));
        if (exec.Result.Value is DomainResult { IsSuccess: false } failed)
            return failed;
        return null;
    }

    /// <summary>
    /// Consumer bind of dictionary <c>this</c>: module bodies keep
    /// <see cref="ThisReference"/> in the cached TypeDef; execute rewrites
    /// them to a typed <c>entity</c> parameter so the VM ABI (slot 0 =
    /// instance, action args at 1+) matches SetArgs(this, ...args). Not a
    /// second lower — same method body nodes, rebound for the scratch store.
    /// </summary>

    private ITypeDefinitionProvider ModuleAwareTypeProvider(
        ITypeDefinitionProvider inner,
        IReadOnlyList<Property>? actionParameters = null) {
        var wrapped = DomainResultTypeProvider.Wrap(inner);
        if (Domain is null)
            return wrapped;
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        var module = RuntimeAnalysisCache.GetOrLower(
            Domain, RuntimeAnalysisCache.Session(Domain), analysis);
        var moduleTypes = new TypeDefinitionNodeAnalyzer();
        var ctx = new AnalysisContext(wrapped);
        TypeDefinitionNode? moduleEntity = null;
        foreach (var td in module) {
            // CLR DomainResult wins via DomainResultTypeProvider.
            if (string.Equals(td.Name, "DomainResult", StringComparison.Ordinal))
                continue;
            if (string.Equals(td.Name, Entity.Name, StringComparison.Ordinal)) {
                moduleEntity = td;
                continue;
            }
            moduleTypes.Analyze(ctx, td);
        }
        // Runtime-shaped entity (string CurrentStage + bag action params) plus
        // module method stubs (Notify*Subscribers, etc.) — consumer bind, not a second lower.
        var runtimeEntity = BuildTypeDefNode(Entity, actionParameters, Domain);
        if (moduleEntity?.Methods is { Count: > 0 } moduleMethods) {
            var names = new HashSet<string>(
                (runtimeEntity.Methods ?? []).Select(m => m.Name), StringComparer.Ordinal);
            List<MethodDefinitionNode>? extras = null;
            foreach (var m in moduleMethods) {
                if (!names.Add(m.Name))
                    continue;
                extras ??= [];
                extras.Add(m with { Body = new Block([]) });
            }
            if (extras is not null)
                runtimeEntity = runtimeEntity with {
                    Methods = [.. runtimeEntity.Methods ?? [], .. extras]
                };
        }
        moduleTypes.Analyze(ctx, runtimeEntity);
        // Module/stage enums + merged entity first; wrapped as fallback.
        return new TypeDefinitionProviderCollection(moduleTypes, wrapped);
    }

    /// <summary>
    /// Consumer bind for export-shaped (UseThis) trees from session.Lower —
    /// rewrites <see cref="ThisReference"/> to <c>Parameter("entity")</c> for VM SetArgs,
    /// and void fail-closed <c>throw new InvalidOperationException(…)</c> to
    /// <c>return DomainResult.Failure(…)</c> (VM has no 1-arg IOE ctor). Not a second lower.
    /// </summary>
    private Node BindExportBody(Node body) {
        var entity = new Parameter("entity", new TypeReference(Entity.Name));
        var bound = BindThis(body, entity, null, EnumTypeNames());
        return RewriteVoidFailClosedThrow(bound);
    }



    /// <summary>
    /// Export void bodies fail closed with throw; Domain-bound execute needs DomainResult.Failure.
    /// Same tree as print — consumer bind only (not a second lower).
    /// </summary>
    private static Node RewriteVoidFailClosedThrow(Node node) => node switch {
        ThrowStatement {
            Exception: New {
                Type: NamedTypeReference { TypeName: "InvalidOperationException" },
                Arguments: var args
            }
        } => new Return(new Invoke(
            new Member(new NamedTypeReference("DomainResult"), "Failure"),
            args.Length > 0 ? RewriteVoidFailClosedThrow(args[0]) : new Constant(""))),
        Block b => new Block(
            b.Nodes.Select(RewriteVoidFailClosedThrow),
            b.Variables.Select(RewriteVoidFailClosedThrow)),
        IfStatement i => new IfStatement(
            RewriteVoidFailClosedThrow(i.Condition),
            RewriteVoidFailClosedThrow(i.ThenBranch),
            i.ElseBranch is null ? null : RewriteVoidFailClosedThrow(i.ElseBranch)),
        Return r => r.Value is null ? r : new Return(RewriteVoidFailClosedThrow(r.Value)),
        Assignment a => new Assignment(
            RewriteVoidFailClosedThrow(a.Destination),
            RewriteVoidFailClosedThrow(a.Value)),
        Invoke inv => new Invoke(
            RewriteVoidFailClosedThrow(inv.Delegate),
            [.. inv.Arguments.Select(RewriteVoidFailClosedThrow)]) {
            TypeArguments = inv.TypeArguments
        },
        Member m => new Member(RewriteVoidFailClosedThrow(m.Value), m.MemberName),
        Poly.Ast.Nodes.Not n => new Poly.Ast.Nodes.Not(RewriteVoidFailClosedThrow(n.Value)),
        Equal e => new Equal(
            RewriteVoidFailClosedThrow(e.LeftHandValue), RewriteVoidFailClosedThrow(e.RightHandValue)),
        NotEqual ne => new NotEqual(
            RewriteVoidFailClosedThrow(ne.LeftHandValue), RewriteVoidFailClosedThrow(ne.RightHandValue)),
        LessThan lt => new LessThan(
            RewriteVoidFailClosedThrow(lt.LeftHandValue), RewriteVoidFailClosedThrow(lt.RightHandValue)),
        LessThanOrEqual le => new LessThanOrEqual(
            RewriteVoidFailClosedThrow(le.LeftHandValue), RewriteVoidFailClosedThrow(le.RightHandValue)),
        GreaterThan gt => new GreaterThan(
            RewriteVoidFailClosedThrow(gt.LeftHandValue), RewriteVoidFailClosedThrow(gt.RightHandValue)),
        GreaterThanOrEqual ge => new GreaterThanOrEqual(
            RewriteVoidFailClosedThrow(ge.LeftHandValue), RewriteVoidFailClosedThrow(ge.RightHandValue)),
        Poly.Ast.Nodes.Add add => new Poly.Ast.Nodes.Add(
            RewriteVoidFailClosedThrow(add.LeftHandValue), RewriteVoidFailClosedThrow(add.RightHandValue)),
        Poly.Ast.Nodes.Subtract sub => new Poly.Ast.Nodes.Subtract(
            RewriteVoidFailClosedThrow(sub.LeftHandValue), RewriteVoidFailClosedThrow(sub.RightHandValue)),
        Poly.Ast.Nodes.Multiply mul => new Poly.Ast.Nodes.Multiply(
            RewriteVoidFailClosedThrow(mul.LeftHandValue), RewriteVoidFailClosedThrow(mul.RightHandValue)),
        Poly.Ast.Nodes.Divide div => new Poly.Ast.Nodes.Divide(
            RewriteVoidFailClosedThrow(div.LeftHandValue), RewriteVoidFailClosedThrow(div.RightHandValue)),
        Poly.Ast.Nodes.And and => new Poly.Ast.Nodes.And(
            RewriteVoidFailClosedThrow(and.LeftHandValue), RewriteVoidFailClosedThrow(and.RightHandValue)),
        Poly.Ast.Nodes.Or or => new Poly.Ast.Nodes.Or(
            RewriteVoidFailClosedThrow(or.LeftHandValue), RewriteVoidFailClosedThrow(or.RightHandValue)),
        Coalesce c => new Coalesce(
            RewriteVoidFailClosedThrow(c.LeftHandValue), RewriteVoidFailClosedThrow(c.RightHandValue)),
        TypeCast tc => new TypeCast(
            RewriteVoidFailClosedThrow(tc.Operand),
            RewriteVoidFailClosedThrow(tc.TargetTypeReference)),
        New n => new New(
            RewriteVoidFailClosedThrow(n.Type),
            [.. n.Arguments.Select(RewriteVoidFailClosedThrow)]),
        ThrowStatement ts => new ThrowStatement(RewriteVoidFailClosedThrow(ts.Exception)),
        TryCatchFinally t => new TryCatchFinally(
            RewriteVoidFailClosedThrow(t.TryBlock),
            t.CatchClauses is null ? null : [.. t.CatchClauses.Select(cc => cc with {
                ExceptionType = cc.ExceptionType is null
                    ? null
                    : RewriteVoidFailClosedThrow(cc.ExceptionType),
                Body = RewriteVoidFailClosedThrow(cc.Body)
            })],
            t.FinallyBlock is null ? null : RewriteVoidFailClosedThrow(t.FinallyBlock)),
        ForEachLoop f => new ForEachLoop(
            f.LoopVariable,
            RewriteVoidFailClosedThrow(f.Collection),
            RewriteVoidFailClosedThrow(f.Body),
            f.Label),
        LabelDeclaration ld => new LabelDeclaration(
            ld.Name, RewriteVoidFailClosedThrow(ld.Statement)),
        Conditional cond => new Conditional(
            RewriteVoidFailClosedThrow(cond.Condition),
            RewriteVoidFailClosedThrow(cond.IfTrue),
            RewriteVoidFailClosedThrow(cond.IfFalse)),
        UnaryMinus um => new UnaryMinus(RewriteVoidFailClosedThrow(um.Operand)),
        NullForgiving nf => new NullForgiving(RewriteVoidFailClosedThrow(nf.Operand)),
        _ => node
    };

    private Node BindModuleMethodBody(MethodDefinitionNode method, bool keepParametersAsSlots = false) {
        var body = method.Body
            ?? throw new InvalidOperationException(
                $"Module method '{method.Name}' on '{Entity.Name}' has no body.");
        var entity = new Parameter("entity", new TypeReference(Entity.Name));
        Dictionary<string, Parameter>? paramMap = null;
        // Action params are injected into the instance bag and rewritten to
        // Member(entity, name). Peer subscription params stay as Parameter slots
        // for SetArgs(this, peer).
        if (!keepParametersAsSlots && method.Parameters is { Count: > 0 } methodParams) {
            paramMap = new Dictionary<string, Parameter>(StringComparer.Ordinal);
            foreach (var p in methodParams)
                paramMap[p.Name] = p;
        }
        return BindThis(body, entity, paramMap, EnumTypeNames());
    }

    /// <summary>
    /// Emit module uses stage / domain enums; dictionary This stores those as strings.
    /// Consumer bind rewrites <c>Enum.Name</c> to string constants — not a second lower.
    /// </summary>
    private IReadOnlySet<string> EnumTypeNames() {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (Domain is null)
            return names;
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        foreach (var e in Domain.Types.OfType<Entity>()) {
            if (e.Stages.Count == 0)
                continue;
            var meta = analysis.GetStructure(e);
            names.Add(meta?.StageEnumTypeName ?? $"{e.Name}Stage");
        }
        foreach (var enumType in Domain.Types.OfType<EnumType>())
            names.Add(enumType.Name);
        return names;
    }

    private static Node BindThis(
        Node node,
        Parameter entity,
        IReadOnlyDictionary<string, Parameter>? parameters,
        IReadOnlySet<string> stageEnums) => node switch {
            ThisReference => entity,
            Parameter p when parameters is not null
                && parameters.ContainsKey(p.Name) => new Member(entity, p.Name),
            Variable v when parameters is not null
                && parameters.ContainsKey(v.Name) => new Member(entity, v.Name),
            // Emit stage enum member → runtime string (CurrentStage is string on This).
            Member { Value: NamedTypeReference ntr } m
                when stageEnums.Contains(ntr.TypeName) => new Constant(m.MemberName),
            Block b => new Block(
                b.Nodes.Select(n => BindThis(n, entity, parameters, stageEnums)),
                b.Variables.Select(n => BindThis(n, entity, parameters, stageEnums))),
            IfStatement i => new IfStatement(
                BindThis(i.Condition, entity, parameters, stageEnums),
                BindThis(i.ThenBranch, entity, parameters, stageEnums),
                i.ElseBranch is null ? null : BindThis(i.ElseBranch, entity, parameters, stageEnums)),
            Return r => r.Value is null ? r : new Return(BindThis(r.Value, entity, parameters, stageEnums)),
            Assignment a => new Assignment(
                BindThis(a.Destination, entity, parameters, stageEnums), BindThis(a.Value, entity, parameters, stageEnums)),
            Invoke { Delegate: Member { MemberName: { } notifyName } } inv
                when inv.Arguments.Length == 0
                    && notifyName.StartsWith("Notify", StringComparison.Ordinal)
                    && notifyName.EndsWith("Subscribers", StringComparison.Ordinal)
                    && notifyName.Length > "NotifySubscribers".Length
                => new Invoke(
                    new Member(BindThis(((Member)inv.Delegate).Value, entity, parameters, stageEnums), "Notify"),
                    new Constant(notifyName["Notify".Length..^"Subscribers".Length])),
            // Module emit uses DomainResult<T>.Success(value). VM CLR DomainResult is
            // non-generic; entity TypeDefs are not assignable-to object under PR53
            // overload scoring. Typed return still comes from CreatedChildren.
            // Success(value) → Success(); Failure(msg) keeps the string arg.
            Invoke {
                Delegate: Member {
                    Value: NamedTypeReference { TypeName: "DomainResult" },
                    MemberName: "Success"
                }
            } inv
                => new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success")),
            Invoke {
                Delegate: Member {
                    Value: NamedTypeReference { TypeName: "DomainResult" },
                    MemberName: "Failure"
                }
            } inv
                => new Invoke(
                    new Member(new NamedTypeReference("DomainResult"), "Failure"),
                    [.. inv.Arguments.Select(a => BindThis(a, entity, parameters, stageEnums))]),
            // Fail closed: export adapter throws; simulate must not silent-success.
            Invoke { Delegate: Member { Value: TypeReference or NamedTypeReference, MemberName: { } endpoint } } inv
                when AdapterTypeName(inv) is { } adapter
                => new Return(new Invoke(
                    new Member(new NamedTypeReference("DomainResult"), "Failure"),
                    new Constant(
                        $"Contract endpoint '{ContractNameFromAdapter(adapter)}.{endpoint}' has no in-process adapter on simulate."))),
            Invoke inv => new Invoke(
                BindThis(inv.Delegate, entity, parameters, stageEnums),
                [.. inv.Arguments.Select(a => BindThis(a, entity, parameters, stageEnums))]) {
                TypeArguments = inv.TypeArguments
            },
            Member m => new Member(BindThis(m.Value, entity, parameters, stageEnums), m.MemberName),
            Poly.Ast.Nodes.Not n => new Poly.Ast.Nodes.Not(BindThis(n.Value, entity, parameters, stageEnums)),
            Equal e => new Equal(
                BindThis(e.LeftHandValue, entity, parameters, stageEnums), BindThis(e.RightHandValue, entity, parameters, stageEnums)),
            NotEqual ne => new NotEqual(
                BindThis(ne.LeftHandValue, entity, parameters, stageEnums), BindThis(ne.RightHandValue, entity, parameters, stageEnums)),
            LessThan lt => new LessThan(
                BindThis(lt.LeftHandValue, entity, parameters, stageEnums), BindThis(lt.RightHandValue, entity, parameters, stageEnums)),
            LessThanOrEqual le => new LessThanOrEqual(
                BindThis(le.LeftHandValue, entity, parameters, stageEnums), BindThis(le.RightHandValue, entity, parameters, stageEnums)),
            GreaterThan gt => new GreaterThan(
                BindThis(gt.LeftHandValue, entity, parameters, stageEnums), BindThis(gt.RightHandValue, entity, parameters, stageEnums)),
            GreaterThanOrEqual ge => new GreaterThanOrEqual(
                BindThis(ge.LeftHandValue, entity, parameters, stageEnums), BindThis(ge.RightHandValue, entity, parameters, stageEnums)),
            Poly.Ast.Nodes.Add add => new Poly.Ast.Nodes.Add(
                BindThis(add.LeftHandValue, entity, parameters, stageEnums), BindThis(add.RightHandValue, entity, parameters, stageEnums)),
            Poly.Ast.Nodes.Subtract sub => new Poly.Ast.Nodes.Subtract(
                BindThis(sub.LeftHandValue, entity, parameters, stageEnums), BindThis(sub.RightHandValue, entity, parameters, stageEnums)),
            Poly.Ast.Nodes.Multiply mul => new Poly.Ast.Nodes.Multiply(
                BindThis(mul.LeftHandValue, entity, parameters, stageEnums), BindThis(mul.RightHandValue, entity, parameters, stageEnums)),
            Poly.Ast.Nodes.Divide div => new Poly.Ast.Nodes.Divide(
                BindThis(div.LeftHandValue, entity, parameters, stageEnums), BindThis(div.RightHandValue, entity, parameters, stageEnums)),
            Poly.Ast.Nodes.And and => new Poly.Ast.Nodes.And(
                BindThis(and.LeftHandValue, entity, parameters, stageEnums), BindThis(and.RightHandValue, entity, parameters, stageEnums)),
            Poly.Ast.Nodes.Or or => new Poly.Ast.Nodes.Or(
                BindThis(or.LeftHandValue, entity, parameters, stageEnums), BindThis(or.RightHandValue, entity, parameters, stageEnums)),
            Coalesce c => new Coalesce(
                BindThis(c.LeftHandValue, entity, parameters, stageEnums), BindThis(c.RightHandValue, entity, parameters, stageEnums)),
            TypeCast tc => new TypeCast(
                BindThis(tc.Operand, entity, parameters, stageEnums),
                BindThis(tc.TargetTypeReference, entity, parameters, stageEnums),
                tc.IsChecked),
            New n => new New(
                BindThis(n.Type, entity, parameters, stageEnums),
                [.. n.Arguments.Select(a => BindThis(a, entity, parameters, stageEnums))]),
            ThrowStatement ts => new ThrowStatement(BindThis(ts.Exception, entity, parameters, stageEnums)),
            TryCatchFinally t => new TryCatchFinally(
                BindThis(t.TryBlock, entity, parameters, stageEnums),
                t.CatchClauses?.Select(cc => cc with {
                    ExceptionType = cc.ExceptionType is null
                        ? null
                        : BindThis(cc.ExceptionType, entity, parameters, stageEnums),
                    Body = BindThis(cc.Body, entity, parameters, stageEnums)
                }).ToList(),
                t.FinallyBlock is null ? null : BindThis(t.FinallyBlock, entity, parameters, stageEnums)),
            ForEachLoop f => new ForEachLoop(
                f.LoopVariable,
                BindThis(f.Collection, entity, parameters, stageEnums),
                BindThis(f.Body, entity, parameters, stageEnums),
                f.Label),
            ContinueStatement or BreakStatement => node,
            LabelDeclaration ld => new LabelDeclaration(
                ld.Name, BindThis(ld.Statement, entity, parameters, stageEnums)),
            Conditional cond => new Conditional(
                BindThis(cond.Condition, entity, parameters, stageEnums),
                BindThis(cond.IfTrue, entity, parameters, stageEnums),
                BindThis(cond.IfFalse, entity, parameters, stageEnums)),
            UnaryMinus um => new UnaryMinus(BindThis(um.Operand, entity, parameters, stageEnums)),
            NullForgiving nf => new NullForgiving(BindThis(nf.Operand, entity, parameters, stageEnums)),
            Parameter or Variable or Constant or NamedTypeReference or TypeReference
                or PrimitiveTypeReference or ClrTypeReference => node,
            _ => throw new InvalidOperationException(
                $"Cannot bind module method this on {node.GetType().Name}.")
        };

    private static string? AdapterTypeName(Invoke inv) =>
        inv.Delegate is Member { Value: Node type } && TypeNameOf(type) is { } name
            && name.EndsWith("Adapters", StringComparison.Ordinal)
            ? name
            : null;

    private static string ContractNameFromAdapter(string adapterTypeName) =>
        adapterTypeName.EndsWith("Adapters", StringComparison.Ordinal)
            ? adapterTypeName[..^"Adapters".Length]
            : adapterTypeName;

    private static string? TypeNameOf(Node type) => type switch {
        TypeReference tr => tr.TypeName,
        NamedTypeReference ntr => ntr.TypeName,
        _ => null
    };


    private void RestoreActionState(
        Dictionary<string, object?> bagBefore, string? stageBefore, int createdBefore) {
        _values.Clear();
        foreach (var (k, v) in bagBefore)
            _values[k] = v;
        CurrentStage = stageBefore;
        while (_createdChildren.Count > createdBefore) {
            var child = _createdChildren[^1];
            _createdChildren.RemoveAt(_createdChildren.Count - 1);
            Store?.Remove(child);
        }
    }

}