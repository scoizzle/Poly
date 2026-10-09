using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
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
    private HashSet<string>? _automaticStageChain;
    private readonly HashSet<string> _exitsRunning = new(StringComparer.Ordinal);
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
            // Decision 7: simulate refuses a domain whose analysis has Errors.
            DomainModelAnalyzer.ThrowIfHasErrors(RuntimeAnalysisCache.GetOrAnalyze(domain));
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
                values[prop.Name] = CoerceBooleanBagValue(prop.Type.TypeName, v);
            }
            else if (prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault() is { } defaultValue) {
                values[prop.Name] = EvaluateDefaultValue(defaultValue.Expression, prop.Type.TypeName, domain);
            }
            else {
                values[prop.Name] = null; // default for unspecified properties
            }
        }

        var typeDefAnalyzer = BuildTypeDefAnalyzer(entity, domain: domain);

        // Domain-bound: compiled static Create Failure-return Ifs.
        // Domain-null: required/range/length/pattern in ValidateConstraints.
        var checkError = CreateCheckFailure(
            entity,
            domain is not null
                ? propertyValues ?? new Dictionary<string, object?>()
                : values,
            domain);
        if (checkError is not null)
            throw domain is not null
                ? new ConstraintFailureException(checkError)
                : new InvalidOperationException(checkError);

        // Initial stage: first declared stage name (factory shape; not a semantic rediscovery).
        var currentStage = entity.Stages.FirstOrDefault()?.Name;

        var instance = new DomainEntityInstance(entity, values, typeDefAnalyzer, currentStage, domain);
        ApplyInitialStageEntryEffects(instance);
        return instance;
    }

    /// <summary>
    /// First create-constraint Failure message, or null when the values pass.
    /// Domain-bound runs the compiled static Create prefix (stop before
    /// <c>created = new</c>). Domain-null keeps required/range/length/pattern.
    /// Unique stays at <c>TryAdd</c>.
    /// </summary>
    internal static string? CreateCheckFailure(
        Entity entity,
        IReadOnlyDictionary<string, object?> values,
        Domain? domain) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);
        return domain is not null
            ? RunCreateFactoryChecks(entity, values, domain)
            : ValidateConstraints(entity, values, domain);
    }

    /// <summary>
    /// Runs the static <c>Create</c> factory's leading Failure-return
    /// <c>IfStatement</c>s. Slot 0 is unused (checks read Parameters);
    /// slots 1+ are factory parameters from <paramref name="values"/>
    /// (ToCamelCase names), missing slots <see cref="Parameter.DefaultValue"/>.
    /// </summary>
    private static string? RunCreateFactoryChecks(
        Entity entity,
        IReadOnlyDictionary<string, object?> values,
        Domain domain) {
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var module = RuntimeAnalysisCache.GetOrLower(
            domain, RuntimeAnalysisCache.Session(domain), analysis);
        MethodDefinitionNode? create = null;
        foreach (var type in module) {
            if (!string.Equals(type.Name, entity.Name, StringComparison.Ordinal))
                continue;
            foreach (var candidate in type.Methods ?? []) {
                if (string.Equals(candidate.Name, "Create", StringComparison.Ordinal)
                    && candidate.IsStatic) {
                    create = candidate;
                    break;
                }
            }
            break;
        }
        if (create?.Body is null)
            return null;
        var (tree, rootParameters) = BindModuleMethodBody(entity.Name, create);
        tree = CreateFactoryCheckPrefix(tree);
        if (tree is Block { Nodes.Count: 0 })
            return null;
        var types = ModuleAwareTypeProvider(
            entity, domain, BuildTypeDefAnalyzer(entity, domain: domain));
        var compiled = CompileBody(entity, tree, types, rootParameters);
        var setArgs = new object?[1 + rootParameters.Count];
        for (var i = 0; i < rootParameters.Count; i++) {
            var param = rootParameters[i];
            var raw = TryFactoryValue(param.Name, values, out var supplied)
                ? supplied
                : FactoryDefault(param.DefaultValue);
            setArgs[i + 1] = CoerceActionArgForSetArgs(param, raw);
        }
        using var exec = Interpreter.Execute(compiled, s => s.SetArgs(setArgs));
        return exec.Result.Value is DomainResult { IsSuccess: false } failed
            ? failed.ErrorMessage ?? "Create failed."
            : null;
    }

    /// <summary>
    /// Leading Failure-return Ifs of static Create; stops before
    /// <c>created = new</c> so the printed ctor does not run on a
    /// <see cref="DomainEntityInstance"/>.
    /// </summary>
    private static Node CreateFactoryCheckPrefix(Node body) {
        if (body is not Block block)
            return new Block([]);
        List<Node>? checks = null;
        foreach (var node in block.Nodes) {
            if (node is Assignment { Value: New })
                break;
            checks ??= [];
            checks.Add(node);
        }
        return checks is null ? new Block([]) : new Block(checks);
    }

    private static bool TryFactoryValue(
        string paramName,
        IReadOnlyDictionary<string, object?> values,
        out object? value) {
        if (values.TryGetValue(paramName, out value))
            return true;
        foreach (var (key, v) in values) {
            if (string.Equals(
                    DomainTypeMapping.ToCamelCase(key), paramName, StringComparison.Ordinal)) {
                value = v;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static object? FactoryDefault(Node? defaultValue) => defaultValue switch {
        Constant c => c.Value,
        Member m => m.MemberName,
        _ => null
    };

    /// <summary>
    /// Domain-null create checks: required/range/length/pattern. Equality
    /// lives in the compiled factory (Domain-bound prefix above). Unique
    /// lives at <c>TryAdd</c>.
    /// </summary>
    private static string? ValidateConstraints(
        Entity entity,
        IReadOnlyDictionary<string, object?> values,
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
        instance.ClearAutomaticStageChain();
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
    /// Sets a property value. The property must exist on the entity.
    /// Unique is enforced when this instance is in a store.
    /// </summary>
    internal void SetProperty(string name, object? value) {
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
    /// Store-aware expressions lower in-tree (nav Member reads / Store jobs
    /// for exists and to-one hops; collection quantifiers foreach the nav).
    /// </summary>
    public bool EvaluatePolicy(Policy policy) {
        ArgumentNullException.ThrowIfNull(policy);

        var expr = policy.Expression;

        // Execute never lowers. Policy bodies come from session.Lower only.
        if (Domain is null)
            throw new InvalidOperationException(
                $"Cannot evaluate policy '{policy.Name}' on '{Entity.Name}' without a Domain-bound module.");

        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        RuntimeAnalysisCache.GetOrLower(Domain, RuntimeAnalysisCache.Session(Domain), analysis);
        if (!RuntimeAnalysisCache.TryGetPolicyBody(Domain, Entity.Name, policy.Name, out var cached)
            || cached is null)
            throw new InvalidOperationException(
                $"Policy body '{policy.Name}' is missing on entity '{Entity.Name}'.");
        using var execModule = Interpreter.Execute(
            CompileBody(cached, ModuleAwareTypeProvider(_typeDefAnalyzer)),
            s => s.SetArgs(new object?[] { this }));
        var boxedModule = BoxPathPrefixLeaf(expr, execModule.Result.GetValue<object>());
        return CoercePolicyBool(policy.Name, boxedModule);
    }

    /// <summary>
    /// VM boolean locals are 0/1 in the ring. Boolean properties and action
    /// parameters store <see cref="bool"/> so bag reads and the export agree.
    /// For create values and action arguments, only 0 and 1 are treated as
    /// booleans; any other value passes through unchanged. Policy results use
    /// <see cref="CoercePolicyBool"/> instead.
    /// </summary>
    internal static object? CoerceBooleanBagValue(string? typeName, object? value) {
        if (!string.Equals(typeName, "Boolean", StringComparison.Ordinal))
            return value;
        return value switch {
            long l and (0L or 1L) => l == 1L,
            int i and (0 or 1) => i == 1,
            _ => value
        };
    }

    /// <summary>
    /// SetArgs must match bag Member reads for action parameters: Text null
    /// becomes <c>""</c>, Boolean 0/1 become bool, then
    /// <see cref="DictionaryBackedValue.GuardCompatible"/> plus Convert
    /// (the same path as <see cref="DictionaryBackedValue.CoerceRead"/>).
    /// </summary>
    private static object? CoerceActionArgForSetArgs(Parameter parameter, object? value) {
        if (value is null && IsTextLikeParameter(parameter))
            value = "";
        var typeName = ParameterTypeName(parameter);
        value = CoerceBooleanBagValue(typeName, value);
        if (PrimitiveForParameter(parameter) is { } primitive)
            return DictionaryBackedValue.CoerceReadValue(value, primitive);
        return value;
    }

    private static bool IsTextLikeParameter(Parameter parameter) =>
        parameter.TypeReference switch {
            ClrTypeReference { RuntimeType: var rt } when rt == typeof(string) => true,
            PrimitiveTypeReference { PrimitiveId: Prim.String } => true,
            NamedTypeReference { TypeName: "Text" or "String" or "string" } => true,
            TypeReference { TypeName: "Text" or "String" or "string" } => true,
            _ => false
        };

    private static string? ParameterTypeName(Parameter parameter) =>
        parameter.TypeReference switch {
            ClrTypeReference { RuntimeType: var rt } when rt == typeof(string) => "Text",
            ClrTypeReference { RuntimeType: var rt } when rt == typeof(bool) => "Boolean",
            PrimitiveTypeReference { PrimitiveId: Prim.String } => "Text",
            PrimitiveTypeReference { PrimitiveId: Prim.Boolean } => "Boolean",
            NamedTypeReference n => n.TypeName,
            TypeReference t => t.TypeName,
            _ => null
        };

    private static Prim? PrimitiveForParameter(Parameter parameter) =>
        parameter.TypeReference switch {
            PrimitiveTypeReference p => p.PrimitiveId,
            ClrTypeReference { RuntimeType: var rt } => rt.GetPrimitiveType(),
            NamedTypeReference n => DomainPrimitive(n.TypeName),
            TypeReference t => DomainPrimitive(t.TypeName),
            _ => null
        };

    private static Prim? DomainPrimitive(string typeName) =>
        DomainTypeMapping.TryPrimitiveType(
            DomainTypeMapping.ToClrTypeName(typeName), out var prim)
            ? prim : null;


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
    ///       <item><b>VM-compiled</b> (<see cref="AssignEffect"/>, <see cref="CompositeEffect"/>, <see cref="ConditionalEffect"/>, <see cref="StageTransitionEffect"/>) → lowered to Syntax AST → compiled via <see cref="Interpreter.Compile"/> → executed via VM. Unique assign is <c>EnsureUnique</c> then Assignment. StageTransition assigns CurrentStage and, when the target is watched, calls <c>Notify{Stage}Subscribers</c>.</item>
    ///       <item><b>Create / create-in</b> → instance factories via InvokeNamed (guarded-probe + body for mixed if+create; not EffectExecutor). Self-invoke and singular cross-entity invoke lower to <c>Invoke(Member(…))</c>.</item>
    ///     </list>
    ///   </item>
    ///   <item>On <see cref="StageTransitionEffect"/>: lowered tree sets stage; store fan-out goes through <c>Notify{Stage}Subscribers</c> when the target is a watched stage (instance <c>Notify(string)</c> is not emitted).</item>
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

        // Outermost action trigger: reset the automatic-transition loop guard.
        if (_invokeDepth == 0)
            ClearAutomaticStageChain();
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
                var typeName = action.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, kv.Key, StringComparison.Ordinal))?.Type.TypeName;
                _values[kv.Key] = CoerceBooleanBagValue(typeName, kv.Value);
                injectedKeys.Add(kv.Key);
            }
        }

        // ── Evaluate all guard policies ─────────────────────────
        // When a Domain-bound module method owns the action, require gates
        // (path-prefix "requires a linked" Failure + policy bools) live in that
        // tree — skip the EvaluatePolicy prelude so ONE-TREE Failure runs and
        // require-not cannot invert soft-false to fail-open. ExecuteEffectList
        // still binds the module Body for named actions even when Ontology
        // effects are empty (gated no-op). Unlinked to-one path-prefix is false
        // via the lowered `rel != null && leaf` guard. Stage policies are in that
        // same tree. Without a module method (no Domain, or no body for this action)
        // both the action and the stage policies are checked here.
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
        if (!moduleOwnsRequire && stage is not null)
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
                actionName: action.Name, actionParameters: action.Parameters, actionArgs: args);
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

    private static void ThrowIfEffectListFailed(DomainResult? failed, string context) {
        if (failed is { IsSuccess: false })
            throw new ConstraintFailureException(
                failed.ErrorMessage ?? $"{context} failed.");
    }

    /// <summary>
    /// One operation AST through <see cref="Interpreter"/>. Named actions always
    /// bind <see cref="MethodDefinitionNode.Body"/> from the cached module — never
    /// <c>LowerActionBody</c> (Ontology residual: dual-path execute is a bug).
    /// Domain-bound OnEntry/OnExit batches bind the GetOrLower export-shaped
    /// body, module method, or mixed-list segment and throw on miss.
    /// Execute never lowers; Domain-null fail-closed.
    /// </summary>
    private DomainResult? ExecuteEffectList(
        IReadOnlyList<Effect> effects,
        TypeDefinitionNodeAnalyzer typeProvider,
        string? actionName = null,
        string? entryStageName = null,
        string? exitStageName = null,
        IReadOnlyList<Property>? actionParameters = null,
        int? entryExitSegmentIndex = null,
        IReadOnlyDictionary<string, object?>? actionArgs = null) {
        // Named actions always bind the module Body (require Failure + Success),
        // even when Ontology effects are empty — gated no-ops still run guards
        // (Final Boss F9: empty-effects must not skip module require).
        if (effects.Count == 0 && actionName is null
            && entryStageName is null && exitStageName is null)
            return null;

        Node? tree;
        IReadOnlyList<Parameter> rootParameters = [];
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
            (tree, rootParameters) = BindModuleMethodBody(method);
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
                tree = segmentBody;
            }
            else if (hasStageName) {
                if (exitStageName is not null
                    && RuntimeAnalysisCache.TryGetEntryExitBody(
                        Domain, Entity.Name, exitStageName, "exit", out var exitBody)
                    && exitBody is not null) {
                    tree = exitBody;
                }
                else if (entryStageName is not null
                    && RuntimeAnalysisCache.TryGetEntryExitBody(
                        Domain, Entity.Name, entryStageName, "entry", out var entryBody)
                    && entryBody is not null) {
                    tree = entryBody;
                }
                else if (exitStageName is not null
                    && RuntimeAnalysisCache.TryGetExitMethod(Domain, Entity.Name, exitStageName, out var exit)
                    && exit?.Body is not null) {
                    (tree, rootParameters) = BindModuleMethodBody(exit);
                }
                else if (entryStageName is not null
                    && RuntimeAnalysisCache.TryGetEntryMethod(Domain, Entity.Name, entryStageName, out var entry)
                    && entry?.Body is not null) {
                    (tree, rootParameters) = BindModuleMethodBody(entry);
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
        var compiled = CompileBody(
            Entity, tree, ModuleAwareTypeProvider(typeProvider, actionParameters), rootParameters);
        var setArgs = new object?[1 + rootParameters.Count];
        setArgs[0] = this;
        for (var i = 0; i < rootParameters.Count; i++) {
            var name = rootParameters[i].Name;
            if (actionArgs is not null && actionArgs.TryGetValue(name, out var arg))
                setArgs[i + 1] = CoerceActionArgForSetArgs(rootParameters[i], arg);
            else
                throw new InvalidOperationException(
                    $"Missing SetArgs value for parameter '{name}' on '{Entity.Name}'.");
        }
        try {
            using var exec = Interpreter.Execute(compiled, s => s.SetArgs(setArgs));
            if (exec.Result.Value is DomainResult { IsSuccess: false } failed)
                return failed;
            return null;
        }
        catch (ConstraintFailureException ex) when (actionName is null) {
            // Named action trees return Failure. A nested void throw
            // (subscription / leftover TransitionStage) must still escape.
            return DomainResult.Failure(ex.Message);
        }
    }

    private ITypeDefinitionProvider ModuleAwareTypeProvider(
        ITypeDefinitionProvider inner,
        IReadOnlyList<Property>? actionParameters = null) =>
        ModuleAwareTypeProvider(Entity, Domain, inner, actionParameters);

    private static ITypeDefinitionProvider ModuleAwareTypeProvider(
        Entity entity,
        Domain? domain,
        ITypeDefinitionProvider inner,
        IReadOnlyList<Property>? actionParameters = null) {
        var wrapped = DomainResultTypeProvider.Wrap(inner);
        if (domain is null)
            return wrapped;
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var module = RuntimeAnalysisCache.GetOrLower(
            domain, RuntimeAnalysisCache.Session(domain), analysis);
        var moduleTypes = new TypeDefinitionNodeAnalyzer();
        var ctx = new AnalysisContext(wrapped);
        TypeDefinitionNode? moduleEntity = null;
        foreach (var td in module) {
            // CLR DomainResult / ConstraintFailureException win via DomainResultTypeProvider.
            if (string.Equals(td.Name, "DomainResult", StringComparison.Ordinal)
                || string.Equals(td.Name, "ConstraintFailureException", StringComparison.Ordinal))
                continue;
            if (string.Equals(td.Name, entity.Name, StringComparison.Ordinal)) {
                moduleEntity = td;
                continue;
            }
            moduleTypes.Analyze(ctx, td);
        }
        // Runtime-shaped entity (string CurrentStage + bag action params) plus
        // module method stubs (Notify*Subscribers, etc.).
        var runtimeEntity = BuildTypeDefNode(entity, actionParameters, domain);
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
        return new TypeDefinitionProviderCollection(moduleTypes, wrapped);
    }

    /// <summary>
    /// Compiles a module body that still contains <see cref="ThisReference"/>.
    /// Root-program analysis does not type <c>this</c>; annotate it as this
    /// entity so member access resolves, then emit the same nodes.
    /// </summary>
    private VmProgram CompileBody(
        Node tree,
        ITypeDefinitionProvider types,
        IReadOnlyList<Parameter>? rootParameters = null) =>
        CompileBody(Entity, tree, types, rootParameters);

    private static VmProgram CompileBody(
        Entity entity,
        Node tree,
        ITypeDefinitionProvider types,
        IReadOnlyList<Parameter>? rootParameters = null) {
        var entityType = types.GetTypeDefinition(entity.Name)
            ?? throw new InvalidOperationException(
                $"Type '{entity.Name}' is missing from the type provider.");
        var analysis = Interpreter.Analyzer.Analyze(
            tree,
            typeDefinitions: types,
            setup: ctx => AnnotateThisReferences(ctx, tree, entityType));
        return rootParameters is { Count: > 0 }
            ? Interpreter.Compile(tree, analysis, rootParameters)
            : Interpreter.Compile(tree, analysis);
    }

    private static void AnnotateThisReferences(
        AnalysisContext ctx, Node node, ITypeDefinition entityType) {
        if (node is ThisReference thisRef)
            ctx.SetResolvedType(thisRef, entityType);
        foreach (var child in node.Children) {
            if (child is not null)
                AnnotateThisReferences(ctx, child, entityType);
        }
    }

    private (Node Tree, IReadOnlyList<Parameter> RootParameters) BindModuleMethodBody(
        MethodDefinitionNode method) =>
        BindModuleMethodBody(Entity.Name, method);

    private static (Node Tree, IReadOnlyList<Parameter> RootParameters) BindModuleMethodBody(
        string entityName,
        MethodDefinitionNode method) {
        var body = method.Body
            ?? throw new InvalidOperationException(
                $"Module method '{method.Name}' on '{entityName}' has no body.");
        // C1a: action parameters are real VM slots after SetArgs(this, …).
        // Body Parameter nodes are name-only; swap in the method's typed Parameters
        // so analysis resolves them. Still Parameter nodes — not Member(this, name).
        var rootParameters = method.Parameters ?? [];
        if (rootParameters.Count == 0)
            return (body, rootParameters);
        var typed = rootParameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
        Node BindTyped(Node node) {
            Node Recurse(Node n) => BindTyped(n);
            return node switch {
                Block b => new Block(
                    b.Nodes.Select(Recurse),
                    b.Variables.Select(Recurse)),
                IfStatement i => new IfStatement(
                    Recurse(i.Condition),
                    Recurse(i.ThenBranch),
                    i.ElseBranch is null ? null : Recurse(i.ElseBranch)),
                Return r => r.Value is null ? r : new Return(Recurse(r.Value)),
                Assignment a => new Assignment(Recurse(a.Destination), Recurse(a.Value)),
                Invoke inv => new Invoke(
                    Recurse(inv.Delegate),
                    [.. inv.Arguments.Select(Recurse)]) {
                    TypeArguments = inv.TypeArguments
                },
                Member m => new Member(Recurse(m.Value), m.MemberName),
                Poly.Ast.Nodes.Not n => new Poly.Ast.Nodes.Not(Recurse(n.Value)),
                Equal e => new Equal(Recurse(e.LeftHandValue), Recurse(e.RightHandValue)),
                NotEqual ne => new NotEqual(Recurse(ne.LeftHandValue), Recurse(ne.RightHandValue)),
                LessThan lt => new LessThan(Recurse(lt.LeftHandValue), Recurse(lt.RightHandValue)),
                LessThanOrEqual le => new LessThanOrEqual(Recurse(le.LeftHandValue), Recurse(le.RightHandValue)),
                GreaterThan gt => new GreaterThan(Recurse(gt.LeftHandValue), Recurse(gt.RightHandValue)),
                GreaterThanOrEqual ge => new GreaterThanOrEqual(Recurse(ge.LeftHandValue), Recurse(ge.RightHandValue)),
                Poly.Ast.Nodes.Add add => new Poly.Ast.Nodes.Add(Recurse(add.LeftHandValue), Recurse(add.RightHandValue)),
                Poly.Ast.Nodes.Subtract sub => new Poly.Ast.Nodes.Subtract(Recurse(sub.LeftHandValue), Recurse(sub.RightHandValue)),
                Poly.Ast.Nodes.Multiply mul => new Poly.Ast.Nodes.Multiply(Recurse(mul.LeftHandValue), Recurse(mul.RightHandValue)),
                Poly.Ast.Nodes.Divide div => new Poly.Ast.Nodes.Divide(Recurse(div.LeftHandValue), Recurse(div.RightHandValue)),
                Poly.Ast.Nodes.And and => new Poly.Ast.Nodes.And(Recurse(and.LeftHandValue), Recurse(and.RightHandValue)),
                Poly.Ast.Nodes.Or or => new Poly.Ast.Nodes.Or(Recurse(or.LeftHandValue), Recurse(or.RightHandValue)),
                Coalesce c => new Coalesce(Recurse(c.LeftHandValue), Recurse(c.RightHandValue)),
                TypeCast tc => new TypeCast(
                    Recurse(tc.Operand), Recurse(tc.TargetTypeReference), tc.IsChecked),
                New n => new New(Recurse(n.Type), [.. n.Arguments.Select(Recurse)]),
                ThrowStatement ts => new ThrowStatement(Recurse(ts.Exception)),
                TryCatchFinally t => new TryCatchFinally(
                    Recurse(t.TryBlock),
                    t.CatchClauses?.Select(cc => cc with {
                        ExceptionType = cc.ExceptionType is null ? null : Recurse(cc.ExceptionType),
                        Body = Recurse(cc.Body)
                    }).ToList(),
                    t.FinallyBlock is null ? null : Recurse(t.FinallyBlock)),
                ForEachLoop f => new ForEachLoop(
                    f.LoopVariable, Recurse(f.Collection), Recurse(f.Body), f.Label),
                ContinueStatement or BreakStatement => node,
                LabelDeclaration ld => new LabelDeclaration(ld.Name, Recurse(ld.Statement)),
                Conditional cond => new Conditional(
                    Recurse(cond.Condition), Recurse(cond.IfTrue), Recurse(cond.IfFalse)),
                UnaryMinus um => new UnaryMinus(Recurse(um.Operand)),
                NullForgiving nf => new NullForgiving(Recurse(nf.Operand)),
                Parameter p when typed.TryGetValue(p.Name, out var bound) => bound,
                ThisReference or Parameter or Variable or Constant
                    or NamedTypeReference or TypeReference
                    or PrimitiveTypeReference or ClrTypeReference => node,
                _ => throw new InvalidOperationException(
                    $"Cannot bind typed parameters on {node.GetType().Name}.")
            };
        }
        return (BindTyped(body), rootParameters);
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

}