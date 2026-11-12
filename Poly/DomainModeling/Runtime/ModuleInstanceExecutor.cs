using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Runtime;

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
    private IReadOnlyList<TypeDefinitionNode> _module;
    private ITypeDefinitionProvider _types;
    private readonly ModuleStoragePort _port = new();
    private readonly Dictionary<string, ModuleInstance> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<object, ModuleInstance> _byBag = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    private readonly List<ModuleInstance> _order = [];
    private readonly Dictionary<(string Entity, string Name), CompiledBody> _compiled = new();
    private readonly List<IDictionary<string, object?>> _born = [];

    private sealed record CompiledBody(Node Body, VmProgram Program, IReadOnlyList<Parameter> Parameters);

    public ModuleInstanceExecutor(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        _domain = domain;
        _module = [.. RuntimeAnalysisCache.GetOrLower(domain).Select(BindParameterBodies)];
        _analysis = RuntimeAnalysisCache.Analysis(domain);
        _types = BuildTypes(_module);
    }

    private static ITypeDefinitionProvider BuildTypes(IReadOnlyList<TypeDefinitionNode> module) {
        // The AST definitions capture this context's provider at construction, so the
        // CLR DomainResult pair (Poly.Runtime) must be in the context before the module types are
        // registered.
        var ctx = new AnalysisContext(
            DomainResultTypeProvider.Wrap(AnalysisContext.CreateDefault().TypeDefinitions));
        var analyzer = new TypeDefinitionNodeAnalyzer();
        foreach (var type in module) {
            if (string.Equals(type.Name, "DomainResult", StringComparison.Ordinal)
                || string.Equals(type.Name, "ConstraintFailureException", StringComparison.Ordinal))
                continue;
            analyzer.Analyze(ctx, type);
        }
        return ctx.TypeDefinitions;
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

    /// <summary>
    /// The body this executor compiled for a module method, or null when it has not run
    /// that method. Lets a test hold the executed tree against the lowered artifact.
    /// </summary>
    internal Node? CompiledBodyFor(string entityName, string methodName) {
        lock (_gate)
            return _compiled.TryGetValue((entityName, methodName), out var compiled) ? compiled.Body : null;
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

    /// <summary>
    /// Host write that enforces <c>unique</c> before the bag changes. The module's
    /// assign path checks the port itself; a direct write has no body to do it.
    /// </summary>
    public void SetProperty(ModuleInstance instance, string name, object? value) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_gate) {
            RequireRegistered(instance);
            var entity = RequireEntity(instance.EntityName);
            var property = entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.Ordinal));
            if (property is null)
                throw new ArgumentException(
                    $"Property '{name}' does not exist on entity '{instance.EntityName}'.");
            if (property.Constraints.Any(c => c is UniqueConstraint)) {
                var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                if (instance.Bag.TryGetValue(name, out var current)
                    && string.Equals(Convert.ToString(current, System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal)) {
                    instance.Bag[name] = value;
                    return;
                }
                if (!_port.TryReserve(instance.EntityName, name, text))
                    throw new InvalidOperationException(
                        $"Unique constraint violated: '{name}' value is already used on another '{instance.EntityName}'.");
                if (current is not null)
                    _port.Release(instance.EntityName, name,
                        Convert.ToString(current, System.Globalization.CultureInfo.InvariantCulture) ?? "");
            }
            instance.Bag[name] = value;
        }
    }

    // ── transactions ──────────────────────────────────────────────────────────

    private sealed class HostException(string message) : Exception(message);

    /// <param name="constraintThrows">
    /// Void contexts throw <see cref="ConstraintFailureException"/> instead of returning
    /// a failed <see cref="Poly.Runtime.DomainResult"/>. Create rolls the partial factory back and
    /// rethrows. Invoke from a subscription keeps the transition that fired it.
    /// </param>
    private ModuleOutcome Transact(
        Func<ModuleOutcome> operation, bool constraintThrows = false, bool keepConstraintMutations = false) {
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
        catch (ConstraintFailureException) when (constraintThrows) {
            if (!keepConstraintMutations)
                Restore(state);
            throw;
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
        foreach (var bag in _born) {
            if (_byBag.ContainsKey(bag))
                continue;
            var entityName = InferEntity(bag);
            if (entityName is null)
                continue;
            adopted.Add(Register(entityName, bag));
        }
        return adopted;
    }

    private string? InferEntity(IDictionary<string, object?> bag) {
        string? best = null;
        var bestHits = 0;
        var tied = false;
        foreach (var entity in _domain.Types.OfType<Entity>()) {
            var hits = 0;
            var foreign = false;
            foreach (var key in bag.Keys) {
                if (string.Equals(key, "CurrentStage", StringComparison.Ordinal) || key.StartsWith('_'))
                    continue;
                if (entity.Properties.Any(p => string.Equals(p.Name, key, StringComparison.Ordinal))
                    || entity.Navigations.Any(n => string.Equals(n.Name, key, StringComparison.Ordinal)))
                    hits++;
                else
                    foreign = true;
            }
            if (foreign || hits == 0)
                continue;
            if (hits > bestHits) {
                best = entity.Name;
                bestHits = hits;
                tied = false;
            }
            else if (hits == bestHits) {
                tied = true;
            }
        }
        return tied ? null : best;
    }

    private void CaptureBorn(Poly.Interpretation.Vm.VmManagedObjectHeap heap) {
        for (var i = 1; i < heap.Count; i++) {
            switch (heap.Get(i)) {
                case IDictionary<string, object?> dict:
                    _born.Add(dict);
                    break;
            }
        }
    }

    /// <summary>
    /// The When handler the module runs is the body cached at lower. A test that
    /// replaces or clears that cache must be what the next invoke executes.
    /// </summary>
    private void SyncSubscriptionBodies() {
        var module = RuntimeAnalysisCache.GetOrLower(_domain);
        var names = HandlerNames();
        if (names.Count == 0)
            return;
        var changed = false;
        foreach (var (info, handlerName) in names) {
            var type = module.FirstOrDefault(t =>
                string.Equals(t.Name, info.SourceEntity.Name, StringComparison.Ordinal));
            if (type?.Methods is not IList<MethodDefinitionNode> methods)
                continue;
            var index = -1;
            for (var i = 0; i < methods.Count; i++) {
                if (string.Equals(methods[i].Name, handlerName, StringComparison.Ordinal)) {
                    index = i;
                    break;
                }
            }
            if (index < 0)
                continue;
            if (!RuntimeAnalysisCache.TryGetSubscriptionBody(
                    _domain, info.Subscription, info.StageName, out var body) || body is null) {
                methods[index] = methods[index] with {
                    Body = new ThrowStatement(new New(
                        new NamedTypeReference("System.InvalidOperationException"),
                        new Constant($"Subscription body is missing on entity '{info.SourceEntity.Name}'.")))
                };
                changed = true;
                continue;
            }
            if (!ReferenceEquals(methods[index].Body, body) && ContainsEntityParameter(body)) {
                // A replaced sentinel assigns through Parameter("entity"). A real
                // handler does not, and must keep the body the module lowered.
                methods[index] = methods[index] with { Body = RewriteEntityParameter(body) };
                changed = true;
            }
        }
        if (!changed)
            return;
        // Nested When calls compile against the type provider, not the cache list.
        // Rebuild so the next action sees the replaced or cleared handler.
        _module = [.. module.Select(BindParameterBodies)];
        _types = BuildTypes(_module);
        _compiled.Clear();
    }

    private List<KeyValuePair<DomainToCSharpExporter.SubscriptionInfo, string>> HandlerNames() {
        var entityLookup = _domain.Types.OfType<Entity>()
            .ToDictionary(e => e.Name, StringComparer.Ordinal);
        var bySubscriber = new Dictionary<string, List<DomainToCSharpExporter.SubscriptionInfo>>(
            StringComparer.Ordinal);
        foreach (var entity in _domain.Types.OfType<Entity>()) {
            void Collect(SubscriptionDispatchPlanMetadata? plan, string? stageName) {
                if (plan is null)
                    return;
                var list = new List<DomainToCSharpExporter.SubscriptionInfo>();
                var byTarget = new Dictionary<string, List<DomainToCSharpExporter.SubscriptionInfo>>(
                    StringComparer.Ordinal);
                DomainToCSharpExporter.CollectSubscriptionInfo(
                    plan, entity, stageName, entityLookup, list, byTarget);
                if (list.Count == 0)
                    return;
                if (!bySubscriber.TryGetValue(entity.Name, out var existing))
                    bySubscriber[entity.Name] = existing = [];
                existing.AddRange(list);
            }
            var analysis = RuntimeAnalysisCache.Analysis(_domain);
            Collect(analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(entity), null);
            foreach (var stage in entity.Stages)
                Collect(analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(stage), stage.Name);
        }
        if (bySubscriber.Count == 0)
            return [];
        return [.. DomainToCSharpExporter.BuildHandlerNames(bySubscriber)];
    }

    private static bool ContainsEntityParameter(Node node) {
        if (node is Parameter { Name: "entity" })
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ContainsEntityParameter(child))
                return true;
        }
        return false;
    }

    private static Node RewriteEntityParameter(Node node) {
        if (node is Parameter { Name: "entity" })
            return new ThisReference();
        return node switch {
            Block b => new Block(b.Nodes.Select(RewriteEntityParameter), b.Variables.Select(RewriteEntityParameter)),
            Assignment a => new Assignment(RewriteEntityParameter(a.Destination), RewriteEntityParameter(a.Value)),
            Member m => new Member(RewriteEntityParameter(m.Value), m.MemberName),
            _ => node
        };
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
        var module = RuntimeAnalysisCache.GetOrLower(_domain);
        foreach (var type in module) {
            if (!string.Equals(type.Name, entityName, StringComparison.Ordinal))
                continue;
            foreach (var method in type.Methods ?? []) {
                if (string.Equals(method.Name, methodName, StringComparison.Ordinal)
                    && method.IsStatic == isStatic)
                    return BindOne(entityName, method);
            }
            break;
        }
        return null;
    }

    private static MethodDefinitionNode BindOne(string entityName, MethodDefinitionNode method) {
        if (method.Body is null || (method.Parameters?.Count ?? 0) == 0)
            return method;
        try {
            return method with { Body = ModuleMethodBinding.BindTypedParameters(entityName, method).Tree };
        }
        catch (InvalidOperationException) {
            return method;
        }
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
        CaptureBorn(exec.State.Heap);
        return exec.Result.Value;
    }

    private CompiledBody Compile(
        string entityName, string name, Node body, IReadOnlyList<Parameter> parameters) {
        if (_compiled.TryGetValue((entityName, name), out var cached) && ReferenceEquals(cached.Body, body))
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
        return _compiled[(entityName, name)] = new CompiledBody(body, program, parameters);
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