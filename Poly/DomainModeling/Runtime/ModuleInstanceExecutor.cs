using System.Collections;

using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Host seam that runs the lowered operation module on dictionary instances, with no
/// scratch-bind instance wrapper and no instance store. The
/// instance <i>is</i> the module's dictionary; the executor only tracks which bag
/// belongs to which entity and id. Meaning stays in the module: Create, actions and
/// policies are the bodies <see cref="RuntimeAnalysisCache"/> cached at lowering.
/// The executor adds what a host owns: argument coercion, a session
/// <see cref="IStoragePort"/>, snapshot/rollback around every operation, discovery of
/// children an action created, and link/unlink over the relationship fields.
/// Bind the domain with <see cref="RuntimeAnalysisCache.GetOrLower(Domain, DomainSession, AnalysisResult)"/>
/// (the lowering session that carries the storage library) before constructing.
/// </summary>
public sealed partial class ModuleInstanceExecutor {
    private readonly object _gate = new();
    private readonly Domain _domain;
    private readonly AnalysisResult _analysis;
    private readonly IReadOnlyList<TypeDefinitionNode> _module;
    private readonly ITypeDefinitionProvider _types;
    private readonly ModuleStoragePort _port = new();
    private readonly Dictionary<string, ModuleInstance> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<object, ModuleInstance> _byBag = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    private readonly List<ModuleInstance> _order = [];
    private readonly Dictionary<(string Entity, string Name), CompiledBody> _compiled = new();

    private sealed record CompiledBody(VmProgram Program, IReadOnlyList<Parameter> Parameters);

    public ModuleInstanceExecutor(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        _domain = domain;
        _module = [.. RuntimeAnalysisCache.GetOrLower(domain).Select(BindParameterBodies)];
        _analysis = RuntimeAnalysisCache.Analysis(domain);

        // The AST definitions capture this context's provider at construction, so the
        // CLR DomainResult pair must be in the context before the module types are
        // registered.
        var ctx = new AnalysisContext(
            DomainResultTypeProvider.Wrap(AnalysisContext.CreateDefault().TypeDefinitions));
        var analyzer = new TypeDefinitionNodeAnalyzer();
        foreach (var type in _module) {
            if (string.Equals(type.Name, "DomainResult", StringComparison.Ordinal)
                || string.Equals(type.Name, "ConstraintFailureException", StringComparison.Ordinal))
                continue;
            analyzer.Analyze(ctx, type);
        }
        _types = ctx.TypeDefinitions;
    }

    /// <summary>
    /// Swaps each parameterised method's name-only <see cref="Parameter"/> uses for the
    /// declared, typed ones. The bound body is both the type definition's body and the
    /// root the VM runs, so one analysis sees one set of nodes. A body the binder cannot
    /// rewrite stays as lowered and fails closed when it is run.
    /// </summary>
    private static TypeDefinitionNode BindParameterBodies(TypeDefinitionNode type) {
        if (type.Methods is null)
            return type;
        var methods = new List<MethodDefinitionNode>(type.Methods.Count);
        foreach (var method in type.Methods) {
            if (method.Body is null || (method.Parameters?.Count ?? 0) == 0) {
                methods.Add(method);
                continue;
            }
            try {
                methods.Add(method with {
                    Body = ModuleMethodBinding.BindTypedParameters(type.Name, method).Tree
                });
            }
            catch (InvalidOperationException) {
                methods.Add(method);
            }
        }
        return type with { Methods = methods };
    }

    /// <summary>The session's storage port: the one every module call receives.</summary>
    public IStoragePort Storage => _port;

    /// <summary>Registered instances in creation order.</summary>
    public IReadOnlyList<ModuleInstance> Instances {
        get { lock (_gate) return [.. _order]; }
    }

    public bool TryGet(string id, out ModuleInstance? instance) {
        lock (_gate) return _byId.TryGetValue(id, out instance);
    }

    /// <summary>The registered instance whose dictionary is <paramref name="bag"/>, if any.</summary>
    public bool TryFind(object bag, out ModuleInstance? instance) {
        lock (_gate) return _byBag.TryGetValue(bag, out instance);
    }

    /// <summary>
    /// Runs the entity's cached static <c>Create</c> on a dictionary <c>This</c>.
    /// <paramref name="values"/> are property values by name (case-insensitive) from the
    /// tool layer: JSON-ish scalars, instance ids for entity-typed values.
    /// </summary>
    public ModuleOutcome Create(string entityName, IReadOnlyDictionary<string, object?> values) {
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate) {
            return Transact(() => {
                var entity = RequireEntity(entityName);
                var create = FindMethod(entityName, "Create", isStatic: true)
                    ?? throw new HostException($"Entity '{entityName}' has no Create factory.");
                var parameters = create.Parameters ?? [];
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var args = new object?[parameters.Count];
                for (var i = 0; i < parameters.Count; i++) {
                    var parameter = parameters[i];
                    if (IsStoragePort(parameter)) {
                        args[i] = _port;
                        continue;
                    }
                    known.Add(parameter.Name);
                    var property = entity.Properties.FirstOrDefault(p =>
                        string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                    var supplied = TryValue(values, parameter.Name, out var raw);
                    var label = $"Property '{property?.Name ?? parameter.Name}'";
                    args[i] = supplied
                        ? CoerceValue(parameter, property?.Type.TypeName, raw, label)
                        : CoerceValue(parameter, null, DefaultOf(parameter), label);
                }
                foreach (var key in values.Keys) {
                    if (!known.Contains(key))
                        throw new HostException($"Unknown property '{key}' for entity '{entityName}'.");
                }

                if (Run(entityName, create, self: null, args) is not DomainResult result)
                    throw new InvalidOperationException($"Create on '{entityName}' did not return a DomainResult.");
                if (!result.IsSuccess)
                    return ModuleOutcome.Fail(result.ErrorMessage ?? "Create failed.");
                if (result.Value is not IDictionary<string, object?> bag)
                    throw new InvalidOperationException($"Create on '{entityName}' did not return a dictionary instance.");

                var instance = Register(entityName, bag);
                return ModuleOutcome.Ok(instance, AdoptCreatedChildren());
            });
        }
    }

    /// <summary>
    /// Invokes an action on <paramref name="target"/>. <paramref name="args"/> are action
    /// parameter values by name; entity-typed parameters take instance ids. Guards,
    /// stage checks and effects are the module body's own. A failure rolls everything back.
    /// </summary>
    public ModuleOutcome Invoke(
        ModuleInstance target, string actionName, IReadOnlyDictionary<string, object?>? args = null) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(actionName);
        lock (_gate) {
            return Transact(() => {
                RequireRegistered(target);
                var action = FindAction(target.EntityName, actionName);
                var method = action is null ? null : FindMethod(target.EntityName, actionName, isStatic: false);
                if (action is null || method is null)
                    return ModuleOutcome.Fail($"Action '{actionName}' not found on entity '{target.EntityName}'.");

                var parameters = method.Parameters ?? [];
                var supplied = args ?? new Dictionary<string, object?>();
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var callArgs = new object?[parameters.Count];
                for (var i = 0; i < parameters.Count; i++) {
                    var parameter = parameters[i];
                    if (IsStoragePort(parameter)) {
                        callArgs[i] = _port;
                        continue;
                    }
                    known.Add(parameter.Name);
                    if (!TryValue(supplied, parameter.Name, out var raw))
                        throw new HostException($"Missing argument '{parameter.Name}' for action '{actionName}'.");
                    var declared = action.Parameters.FirstOrDefault(p =>
                        string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                    callArgs[i] = CoerceValue(parameter, declared?.Type.TypeName, raw, $"Argument '{parameter.Name}'");
                }
                foreach (var key in supplied.Keys) {
                    if (!known.Contains(key))
                        throw new HostException($"Unknown argument '{key}' for action '{actionName}'.");
                }

                object? ran;
                try {
                    ran = Run(target.EntityName, method, target.Bag, callArgs);
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("value stack overflow", StringComparison.Ordinal)) {
                    throw new HostException(
                        $"Action invoke depth exceeded while calling '{actionName}'. "
                        + "Possible recursive invoke cycle (e.g. action → invoke self, or OnEntry → invoke → transition loops).");
                }
                if (ran is not DomainResult result)
                    throw new InvalidOperationException(
                        $"Action '{actionName}' on '{target.EntityName}' did not return a DomainResult.");
                if (!result.IsSuccess)
                    return ModuleOutcome.Fail(result.ErrorMessage ?? $"invoke '{actionName}' failed.");

                var created = AdoptCreatedChildren();
                ModuleInstance? returned = null;
                if (result.Value is not null && !_byBag.TryGetValue(result.Value, out returned)
                    && result.Value is IDictionary<string, object?> returnedBag
                    && action.Result.Members.Count > 0) {
                    // A bare create is returned but linked nowhere: the declared return type
                    // names the entity, and the executor starts tracking the instance.
                    returned = Register(action.Result.Members[0].Type.TypeName, returnedBag);
                    created.Add(returned);
                    created.AddRange(AdoptCreatedChildren());
                }
                return ModuleOutcome.Ok(returned, created);
            });
        }
    }

    /// <summary>
    /// Evaluates a named policy: the policy body the lowering cached, run on the
    /// instance's dictionary. Links are read from the bag's relationship fields.
    /// Fail closed when the policy has no cached body.
    /// </summary>
    public bool EvaluatePolicy(ModuleInstance instance, string policyName) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrEmpty(policyName);
        lock (_gate) {
            RequireRegistered(instance);
            if (!RuntimeAnalysisCache.TryGetPolicyBody(_domain, instance.EntityName, policyName, out var body)
                || body is null)
                throw new InvalidOperationException(
                    $"Policy body '{policyName}' is missing on entity '{instance.EntityName}'.");
            var compiled = Compile(instance.EntityName, $"policy:{policyName}", body, []);
            using var exec = Interpreter.Execute(compiled.Program, s => s.SetArgs([instance.Bag]));
            return exec.Result.GetValue<object>() switch {
                bool b => b,
                long l => l != 0L,
                int i => i != 0,
                null => false,
                var other => throw new InvalidOperationException(
                    $"Policy '{policyName}' produced {other.GetType().Name}, not a boolean.")
            };
        }
    }

    // ── transactions ──────────────────────────────────────────────────────────

    private sealed class HostException(string message) : Exception(message);

    private ModuleOutcome Transact(Func<ModuleOutcome> operation) {
        var state = Capture();
        try {
            var outcome = operation();
            if (!outcome.Succeeded)
                Restore(state);
            return outcome;
        }
        catch (HostException ex) {
            Restore(state);
            return ModuleOutcome.Fail(ex.Message);
        }
        catch (ConstraintFailureException ex) {
            Restore(state);
            return ModuleOutcome.Fail(ex.Message);
        }
        catch {
            Restore(state);
            throw;
        }
    }

    private sealed record BagState(
        IDictionary<string, object?> Bag,
        KeyValuePair<string, object?>[] Entries,
        (IList List, object?[] Items)[] Lists);

    private sealed record ExecutorState(BagState[] Bags, int Registered, int PortMark);

    private ExecutorState Capture() {
        var bags = new BagState[_order.Count];
        for (var i = 0; i < bags.Length; i++) {
            var bag = _order[i].Bag;
            var entries = bag.ToArray();
            var lists = new List<(IList, object?[])>();
            foreach (var (_, value) in entries) {
                if (value is IList { IsFixedSize: false, IsReadOnly: false } list)
                    lists.Add((list, ToArray(list)));
            }
            bags[i] = new BagState(bag, entries, [.. lists]);
        }
        return new ExecutorState(bags, _order.Count, _port.Mark());
    }

    private void Restore(ExecutorState state) {
        _port.RollbackTo(state.PortMark);
        for (var i = _order.Count - 1; i >= state.Registered; i--) {
            var dropped = _order[i];
            _byId.Remove(dropped.Id);
            _byBag.Remove(dropped.Bag);
            _order.RemoveAt(i);
        }
        foreach (var saved in state.Bags) {
            saved.Bag.Clear();
            foreach (var (key, value) in saved.Entries)
                saved.Bag[key] = value;
            foreach (var (list, items) in saved.Lists) {
                list.Clear();
                foreach (var item in items)
                    list.Add(item);
            }
        }
    }

    private static object?[] ToArray(IList list) {
        var items = new object?[list.Count];
        list.CopyTo(items, 0);
        return items;
    }

    // ── registration and created-child detection ─────────────────────────────

    private ModuleInstance Register(string entityName, IDictionary<string, object?> bag) {
        var instance = new ModuleInstance(Guid.NewGuid().ToString("N"), entityName, bag);
        _byId[instance.Id] = instance;
        _byBag[bag] = instance;
        _order.Add(instance);
        return instance;
    }

    private void RequireRegistered(ModuleInstance instance) {
        if (!_byId.TryGetValue(instance.Id, out var known) || !ReferenceEquals(known.Bag, instance.Bag))
            throw new HostException($"Instance '{instance.Id}' is not registered with this executor.");
    }

    /// <summary>
    /// Diffs the relationship fields against the registered set: any dictionary a
    /// relationship field holds that no instance owns yet was created by the module
    /// (create-in, nested create). Registers it, then looks inside it in turn.
    /// </summary>
    private List<ModuleInstance> AdoptCreatedChildren() {
        var adopted = new List<ModuleInstance>();
        for (var i = 0; i < _order.Count; i++) {
            var owner = _order[i];
            foreach (var contract in ContractsFrom(owner.EntityName)) {
                foreach (var item in ReadLinked(owner.Bag, contract)) {
                    if (item is not IDictionary<string, object?> bag || _byBag.ContainsKey(bag))
                        continue;
                    adopted.Add(Register(contract.TargetEntityName, bag));
                }
            }
        }
        return adopted;
    }

    // ── module access ─────────────────────────────────────────────────────────

    private Entity RequireEntity(string entityName) =>
        _domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal))
        ?? throw new HostException($"Entity '{entityName}' not found in domain '{_domain.Name}'.");

    private Action? FindAction(string entityName, string actionName) {
        var entity = RequireEntity(entityName);
        return entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions))
            .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));
    }

    private MethodDefinitionNode? FindMethod(string entityName, string methodName, bool isStatic) {
        foreach (var type in _module) {
            if (!string.Equals(type.Name, entityName, StringComparison.Ordinal))
                continue;
            foreach (var method in type.Methods ?? []) {
                if (string.Equals(method.Name, methodName, StringComparison.Ordinal)
                    && method.IsStatic == isStatic)
                    return method;
            }
            break;
        }
        return null;
    }

    private object? Run(string entityName, MethodDefinitionNode method, object? self, object?[] args) {
        var body = method.Body
            ?? throw new InvalidOperationException(
                $"Module method '{method.Name}' on '{entityName}' has no body.");
        var compiled = Compile(entityName, method.Name, body, method.Parameters ?? []);
        var setArgs = new object?[1 + args.Length];
        setArgs[0] = self;
        args.CopyTo(setArgs, 1);
        using var exec = Interpreter.Execute(compiled.Program, s => s.SetArgs(setArgs));
        return exec.Result.Value;
    }

    private CompiledBody Compile(
        string entityName, string name, Node body, IReadOnlyList<Parameter> parameters) {
        if (_compiled.TryGetValue((entityName, name), out var cached))
            return cached;
        var entityType = _types.GetTypeDefinition(entityName)
            ?? throw new InvalidOperationException(
                $"Type '{entityName}' is missing from the module provider.");
        var analysis = Interpreter.Analyze(body, _types, setup: ctx => {
            AnnotateThis(ctx, body, entityType);
            if (parameters.Count > 0)
                BindRootParameterUses(ctx, body, parameters);
        });
        var program = parameters.Count == 0
            ? Interpreter.Compile(body, analysis)
            : Interpreter.Compile(body, analysis, parameters);
        return _compiled[(entityName, name)] = new CompiledBody(program, parameters);
    }

    /// <summary>
    /// Root-program analysis does not type <c>this</c>; the module body holds a
    /// standalone <see cref="ThisReference"/>. Point it at the module entity type.
    /// </summary>
    private static void AnnotateThis(AnalysisContext ctx, Node node, ITypeDefinition entityType) {
        if (node is ThisReference self)
            ctx.SetResolvedType(self, entityType);
        foreach (var child in node.Children) {
            if (child is not null)
                AnnotateThis(ctx, child, entityType);
        }
    }

    /// <summary>
    /// A root parameter is declared for SetArgs, and the body may hold another
    /// <see cref="Parameter"/> instance of that name. Point the use at the declared one.
    /// </summary>
    private static void BindRootParameterUses(
        AnalysisContext ctx, Node node, IReadOnlyList<Parameter> rootParameters) {
        if (node is Parameter parameter) {
            foreach (var declared in rootParameters) {
                if (ReferenceEquals(declared, parameter))
                    break;
                if (string.Equals(declared.Name, parameter.Name, StringComparison.Ordinal)) {
                    ctx.SetMetadata(parameter, new ParameterBindingMetadata(declared));
                    break;
                }
            }
        }
        foreach (var child in node.Children) {
            if (child is not null)
                BindRootParameterUses(ctx, child, rootParameters);
        }
    }

    private static bool IsStoragePort(Parameter parameter) =>
        parameter.TypeReference switch {
            ClrTypeReference reference => typeof(IStoragePort).IsAssignableFrom(reference.RuntimeType),
            TypeReference type => string.Equals(
                type.TypeName, typeof(IStoragePort).FullName, StringComparison.Ordinal),
            _ => false
        };

    private static bool TryValue(
        IReadOnlyDictionary<string, object?> values, string parameterName, out object? value) {
        foreach (var (key, candidate) in values) {
            if (string.Equals(key, parameterName, StringComparison.OrdinalIgnoreCase)) {
                value = candidate;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static object? DefaultOf(Parameter parameter) => parameter.DefaultValue switch {
        Constant constant => constant.Value,
        Member member => member.MemberName,
        _ => null
    };
}