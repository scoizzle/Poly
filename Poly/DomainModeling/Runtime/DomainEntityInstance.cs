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
        var validationError = ValidateConstraints(entity, values);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        // Initial stage: first declared stage name (factory shape; not a semantic rediscovery).
        var currentStage = entity.Stages.FirstOrDefault()?.Name;

        var instance = new DomainEntityInstance(entity, values, typeDefAnalyzer, currentStage, domain);
        ApplyInitialStageEntryEffects(instance);
        return instance;
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
        DomainInstanceStore? store = null) {
        foreach (var prop in entity.Properties) {
            values.TryGetValue(prop.Name, out var v);
            foreach (var constraint in prop.Constraints) {
                switch (constraint) {
                    case RequiredConstraint:
                        if (IsText(prop) && string.IsNullOrEmpty(v as string))
                            return $"'{prop.Name}' is required.";
                        if (v is null && IsNullableDomainTypeName(prop.Type.TypeName))
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

    private static bool IsNullableDomainTypeName(string typeName) =>
        typeName is "Text" or "String"
        || typeName is not ("Number" or "Int" or "Int64" or "Int32" or "Boolean"
            or "Bool" or "DateTime" or "Timestamp" or "Date" or "DateOnly"
            or "Time" or "TimeOnly" or "Duration" or "TimeSpan" or "Uuid" or "Guid"
            or "Decimal" or "Float" or "Double");

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

        var analysis = instance.Domain is not null
            ? RuntimeAnalysisCache.GetOrAnalyze(instance.Domain)
            : null;
        var loweringContext = new LoweringContext(
            new Parameter("entity", new TypeReference(instance.Entity.Name)),
            Analysis: analysis,
            Domain: instance.Domain);
        var entryPass = new EffectLoweringPass(instance.Entity, loweringContext);
        var entryEffects = firstStage.OnEntryEffects
            .Where(e => e is not StageTransitionEffect)
            .ToList();
        if (entryEffects.Count == 0)
            return;
        // Mixed if+create must LowerActionBody (ExecuteStructured was deleted).
        ThrowIfEffectListFailed(
            instance.ExecuteEffectList(entryEffects, entryPass, instance._typeDefAnalyzer,
                cacheKey: $"{instance.Entity.Name}\0entry\0{firstStage.Name}"),
            "first-stage OnEntry");
    }

    /// <summary>
    /// Evaluates a DSL default expression to a concrete runtime value, adapted to
    /// the target property's CLR type when known (discovery round5 F1–F3).
    /// The runtime stores enum-typed properties as strings, so an enum member name
    /// (e.g. <c>default(Active)</c>) lowers to its name string; <c>now</c>/<c>today</c>/<c>guid</c>
    /// evaluate at creation time. Matches the C# export's defaulted optional ctor params.
    /// </summary>
    private object? EvaluateDefaultValue(DomainExpression expr, string? propTypeName = null) =>
        EvaluateDefaultValue(expr, propTypeName, Domain);

    private static object? EvaluateDefaultValue(DomainExpression expr, string? propTypeName, Domain? domain) => expr switch {
        Literal lit => lit.Value,
        Now => propTypeName is "DateTime" or "Timestamp"
            ? DateTime.UtcNow
            : DateOnly.FromDateTime(DateTime.UtcNow),
        Today => propTypeName is "DateTime" or "Timestamp"
            ? DateTime.Today
            : DateOnly.FromDateTime(DateTime.Today),
        PropertyAccess pa => pa.Name switch {
            "Now" or "UtcNow" => propTypeName is "DateTime" or "Timestamp"
                ? DateTime.UtcNow
                : DateOnly.FromDateTime(DateTime.UtcNow),
            "Today" => propTypeName is "DateTime" or "Timestamp"
                ? DateTime.Today
                : DateOnly.FromDateTime(DateTime.Today),
            "Guid" => propTypeName is "Text" or "String"
                ? Guid.NewGuid().ToString()
                : Guid.NewGuid(),
            _ => pa.Name // enum member name — runtime stores enum values as strings
        },
        _ => throw new InvalidOperationException(
            $"Cannot evaluate default expression of type '{expr.GetType().Name}'.")
    };

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
        DomainInstanceStore? store = null) =>
        ValidateConstraints(entity, values, store);

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

        var entityParam = new Parameter("entity", new TypeReference(Entity.Name));
        AnalysisResult? analysis = Domain is not null
            ? RuntimeAnalysisCache.GetOrAnalyze(Domain)
            : null;
        var pass = new DomainExpressionLoweringPass(new LoweringContext(
            entityParam,
            Analysis: analysis,
            Domain: Domain,
            PropertyTypeResolver: EffectLoweringPass.BuildPropertyTypeResolver(Entity),
            NavigationNameResolver: EffectLoweringPass.BuildNavigationNameResolver(Entity, Domain, analysis),
            IsCollectionNavigation: EffectLoweringPass.BuildIsCollectionNavigation(Entity, Domain, analysis),
            IsRelationshipNavigation: EffectLoweringPass.BuildIsRelationshipNavigation(Entity, Domain, analysis),
            SourceEntityName: Entity.Name));
        var lowered = pass.Lower(expr, entityParam);

        var compiled = Interpreter.CompileChecked(lowered, _typeDefAnalyzer);
        using var exec = Interpreter.Execute(compiled,
            s => s.SetArgs(new object?[] { this }));
        var boxed = BoxPathPrefixLeaf(expr, exec.Result.GetValue<object>());
        return boxed switch {
            bool b => b,
            long l => l != 0L,
            int i => i != 0,
            null => false,
            _ => throw new InvalidOperationException(
                $"Policy '{policy.Name}' produced {boxed.GetType().Name}, not a boolean.")
        };
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
        var failures = new List<string>();
        foreach (var guard in action.Policies)
            if (!EvaluatePolicy(guard)) failures.Add(guard.Name);

        if (failures.Count > 0)
            return ActionInvocationResult.Blocked(actionName, failures);

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
        var subjectParam = new Parameter("entity", new TypeReference(Entity.Name));
        var loweringContext = new LoweringContext(
            subjectParam,
            Analysis: runtimeAnalysis,
            Domain: Domain,
            SourceStageName: CurrentStage,
            ActionParameterNames: action.Parameters.Count > 0
                ? action.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                : null);
        var effectPass = new EffectLoweringPass(Entity, loweringContext);
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
            var failed = ExecuteEffectList(action.Effects, effectPass, effectTypeProvider,
                cacheKey: $"{Entity.Name}\0action\0{action.Name}\0{CurrentStage}");
            if (failed is { IsSuccess: false }) {
                // Unique-before-mutate restore (PR 44 F2). Other constraint Failures
                // keep prior assigns — PR 43 documented miss IfOnMutatedProperty.
                if (failed.ErrorMessage is string msg && msg.Contains("Unique", StringComparison.Ordinal))
                    RestoreActionState(bagBefore, stageBefore, createdBefore);
                return ActionInvocationResult.InvalidArguments(
                    actionName, failed.ErrorMessage ?? "invoke failed.");
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
            var loweringPass = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity")));
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
    /// One operation AST: every effect list lowers and runs through <see cref="Interpreter"/>.
    /// Shared by InvokeAction, first-stage OnEntry, subscription, and TransitionStage entry/exit.
    /// </summary>
    private DomainResult? ExecuteEffectList(
        IReadOnlyList<Effect> effects,
        EffectLoweringPass effectPass,
        TypeDefinitionNodeAnalyzer typeProvider,
        string? cacheKey = null) {
        var prepared = effects.Select(PreprocessEffectExpressions).ToList();
        if (prepared.Count == 0)
            return null;

        Node? tree = Domain is not null && cacheKey is not null
            ? RuntimeAnalysisCache.GetOrLowerOperation(Domain, cacheKey,
                () => effectPass.LowerActionBody(prepared))
            : effectPass.LowerActionBody(prepared);
        if (tree is null)
            throw new InvalidOperationException(
                "Cannot lower effect list to a Syntax AST.");
        var compiled = Interpreter.CompileChecked(
            tree, DomainResultTypeProvider.Wrap(typeProvider));
        using var exec = Interpreter.Execute(compiled,
            s => s.SetArgs(new object?[] { this }));
        if (exec.Result.Value is DomainResult { IsSuccess: false } failed)
            return failed;
        return null;
    }

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

    /// <summary>
    /// Rewrites runtime keywords (<c>now</c>/<c>today</c>/<c>guid</c>) on assign
    /// values. Store-aware expressions lower in-tree (nav Member reads / Store jobs).
    /// </summary>
    private Effect PreprocessEffectExpressions(Effect effect) => effect switch {
        AssignEffect a => a with { Value = PreprocessRuntimeKeyword(a.Value, (a.Target as PropertyAccess)?.Name) },
        ConditionalEffect c => c with {
            Condition = c.Condition,
            ThenEffects = c.ThenEffects.Select(PreprocessEffectExpressions).ToList(),
            ElseEffects = c.ElseEffects?.Select(PreprocessEffectExpressions).ToList()
        },
        CompositeEffect c => c with {
            Effects = c.Effects.Select(PreprocessEffectExpressions).ToList()
        },
        CreateEntityInstance cei => cei with {
            Initializers = cei.Initializers
                .Select(i => i with { Expression = PreprocessRuntimeKeyword(i.Expression, i.PropertyName) })
                .ToList()
        },
        CreateEntityInRelationshipEffect cir => cir with {
            Initializers = cir.Initializers
                .Select(i => i with { Expression = PreprocessRuntimeKeyword(i.Expression, i.PropertyName) })
                .ToList()
        },
        InvokeActionEffect iae => iae with {
            ParameterBindings = iae.ParameterBindings
                .Select(b => b with { Expression = PreprocessRuntimeKeyword(b.Expression, null) })
                .ToList()
        },
        // `for` arguments are binder-rooted (item Qty) — PreprocessQuantifiers would treat
        // the binder root as a relationship and fail. The binder is bound per-target in
        // ExecuteForEachInvoke; args carry no store quantifiers (analysis restricts roots).
        ForEachInvokeEffect efe => efe,
        _ => effect
    };

    /// <summary>Rewrites an assign RHS runtime keyword (now/today/guid) into a literal via the
    /// type-aware <see cref="EvaluateDefaultValue"/> — the shared VM lowering emits
    /// <c>DateOnly.FromDateTime(...)</c> for such RHS, which the runtime VM cannot execute.</summary>
    private DomainExpression PreprocessRuntimeKeyword(DomainExpression value, string? targetPropName) {
        var propType = Entity.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, targetPropName, StringComparison.Ordinal))?.Type.TypeName;
        // Pack-owned clock nodes (Now/Today) resolve through the ambient default registry —
        // core never names pack IR.
        if (value is Now or Today)
            return DomainExpression.Literal(EvaluateDefaultValue(value, propType));
        if (value is PropertyAccess { Name: var name } && name is "Now" or "UtcNow" or "Today" or "Guid")
            return DomainExpression.Literal(EvaluateDefaultValue(value, propType));
        return value;
    }
}