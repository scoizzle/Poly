using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;

namespace Poly.Packs.Product;

// MinimalApiGenerator (Actions) — action endpoints, contributed arguments, result switching.
public sealed partial class MinimalApiGenerator {
    /// <summary>Builds Syntax IR for action endpoints with try/catch, result switch, and entity-ref lookups.</summary>
    private void AppendActionEndpointStatements(List<Node> statements, Entity entity, string dbContextName) {
        var emittedActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ia in GetBehaviorActions(entity)) {
            // One route per action name. Stage copies share the entity method, which
            // already rejects the wrong stage.
            if (!emittedActions.Add(ia.Name))
                continue;
            var store = GetStorageEntity(entity);
            var key = RouteKeyFor(store);
            var keyName = key.Name;
            var keyType = key.ClrType;
            var pluralName = Pluralize(entity.Name);
            var parentCtx = GetStorageEntity(entity).IsRoot ? null : GetAggregateParent(entity);
            // What the action can read, at any depth, from the lowered module. A refused action fails generation here, naming
            // the action and the reason (ActionReadsRefusedException): the host never serves an action with less than it reads.
            // With no lowered module (shape-only generation) there is nothing to read, so only the floor below loads.
            var reads = _catalog is null ? null : ReadsOf(entity, ia);
            var thisReads = reads?.Roots.FirstOrDefault(r => r.Name == "this")?.Children ?? [];
            string? backReference = null;
            IReadOnlySet<string> parentLoaded = new HashSet<string>();

            string actionRoute;
            List<Parameter> actionParams = [];
            List<Node> preActionNodes = [];

            if (parentCtx is { } ctx) {
                var (pEntity, relNameRaw, _) = ctx;
                var pStore = GetStorageEntity(pEntity);
                var pKey = RouteKeyFor(pStore);
                var pKeyName = pKey.Name;
                var pKeyType = pKey.ClrType;
                var pascalRel = Pascalize(relNameRaw);
                var childKeyParam = DistinctChildKeyParam(pKeyName, keyName, entity.Name);
                var isCollection = IsCollectionNavigation(pStore, pascalRel);
                var parentRoute = $"/api/{Pluralize(pEntity.Name).ToLowerInvariant()}/{{{pKeyName}}}/{ToCamelCase(relNameRaw).ToLowerInvariant()}";
                actionRoute = isCollection
                    ? $"{parentRoute}/{{{childKeyParam}}}/{ToCamelCase(ia.Name).ToLowerInvariant()}"
                    : $"{parentRoute}/{ToCamelCase(ia.Name).ToLowerInvariant()}";
                actionParams.Add(new Parameter(pKeyName, ClrTypeNode(pKeyType)));
                if (isCollection)
                    actionParams.Add(new Parameter(childKeyParam, ClrTypeNode(keyType)));
                if (ia.Parameters.Count > 0)
                    actionParams.Add(new Parameter("dto", new TypeReference(GetActionDtoName(entity, ia.Name))));
                actionParams.Add(new Parameter("db", new TypeReference(dbContextName)));
                backReference = ctx.BackRefName is null ? null : Pascalize(ctx.BackRefName);
                parentLoaded = pStore.ReferenceNavigations.Select(n => n.PropertyName)
                    .Concat(isCollection ? [pascalRel] : []).ToHashSet(StringComparer.Ordinal);

                // Parent + entity lookup + membership check
                preActionNodes.Add(Init("parentEntity",
                    LoadByRouteKey(Pluralize(pEntity.Name), pStore, new Variable(pKeyName))));
                preActionNodes.Add(new IfStatement(new Equal(new Variable("parentEntity"), new Constant(null!)),
                    new Block(new Return(ProblemResponse(404, "Not Found", $"{pEntity.Name} not found")))));
                foreach (var nav in pStore.ReferenceNavigations.Where(n => n.PropertyName != pascalRel))
                    preActionNodes.Add(LoadReference("parentEntity", nav.PropertyName));
                if (isCollection) {
                    preActionNodes.Add(Init("entity",
                        LoadByRouteKey(pluralName, store, new Variable(childKeyParam))));
                    preActionNodes.Add(new IfStatement(new Equal(new Variable("entity"), new Constant(null!)),
                        new Block(new Return(ProblemResponse(404, "Not Found", $"{entity.Name} not found")))));
                    var parEntry = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parentEntity"));
                    var parColl = new Invoke(new Member(parEntry, "Collection"), new Lambda([new Parameter("e")], new Member(new Variable("e"), pascalRel)));
                    preActionNodes.Add(new Await(new Invoke(new Member(parColl, "LoadAsync"))));
                    preActionNodes.Add(new IfStatement(
                        new Ast.Nodes.Not(
                            new Invoke(
                                new Member(new Member(new Variable("parentEntity"), pascalRel), "Any"),
                                new Lambda([new Parameter("e")], new Equal(new Variable("e"), new Variable("entity"))))),
                        new Block(new Return(ProblemResponse(404, "Not Found", $"{entity.Name} not found for this {pEntity.Name}")))));
                }
                else {
                    var parEntry = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parentEntity"));
                    var parRef = new Invoke(new Member(parEntry, "Reference"), new Lambda([new Parameter("e")], new Member(new Variable("e"), pascalRel)));
                    preActionNodes.Add(new Await(new Invoke(new Member(parRef, "LoadAsync"))));
                    preActionNodes.Add(Init("entity", new Member(new Variable("parentEntity"), pascalRel)));
                    preActionNodes.Add(new IfStatement(new Equal(new Variable("entity"), new Constant(null!)),
                        new Block(new Return(ProblemResponse(404, "Not Found", $"{entity.Name} not found")))));
                }
            }
            else {
                var baseRoute = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
                actionRoute = $"{baseRoute}/{{{keyName}}}/{ToCamelCase(ia.Name).ToLowerInvariant()}";
                actionParams.Add(new Parameter(keyName, ClrTypeNode(keyType)));
                if (ia.Parameters.Count > 0)
                    actionParams.Add(new Parameter("dto", new TypeReference(GetActionDtoName(entity, ia.Name))));
                actionParams.Add(new Parameter("db", new TypeReference(dbContextName)));

                preActionNodes.Add(Init("entity",
                    LoadByRouteKey(pluralName, store, new Variable(keyName))));
                preActionNodes.Add(new IfStatement(new Equal(new Variable("entity"), new Constant(null!)),
                    new Block(new Return(ProblemResponse(404, "Not Found", $"{entity.Name} not found")))));
            }

            // Every local of the action body is drawn from this one set, so names are unique by construction: the route and body
            // parameters, the fixed locals, the loads' locals and the entity-parameter locals (a clash takes a numeric suffix).
            var locals = new HashSet<string>(["entity", "parentEntity", "dto", "db", "result", "ex"], StringComparer.Ordinal);
            foreach (var routeParameter in actionParams)
                locals.Add(routeParameter.Name);

            // The one-hop floor: every collection and single link of the entity, loaded whether or not the action reads it. It is
            // a safety net for the read tree's assumptions, not the plan: the tree below is the truth about what is read.
            foreach (var nav in store.CollectionNavigations)
                preActionNodes.Add(LoadCollection("entity", nav.PropertyName));

            // The module sees a stored link only through the loaded navigation.
            foreach (var nav in store.ReferenceNavigations)
                preActionNodes.Add(LoadReference("entity", nav.PropertyName));

            // Everything the action reads beyond the floor, at any depth. The route parent is the entity's back-reference
            // node, so what is read through it loads on the parent variable.
            var entityLoaded = store.CollectionNavigations.Concat(store.ReferenceNavigations)
                .Select(n => n.PropertyName).ToHashSet(StringComparer.Ordinal);
            AppendReadLoads(preActionNodes, "entity", thisReads.Where(n => n.Navigation != backReference).ToList(), entityLoaded, locals);
            if (thisReads.FirstOrDefault(n => n.Navigation == backReference) is { } parentReads)
                AppendReadLoads(preActionNodes, "parentEntity", parentReads.Children, parentLoaded, locals);

            // ── Try body: entity-ref lookups + invoke + result switch ──
            var tryBody = new List<Node>();
            var invokeArgs = new List<Node>();
            var loadedRoots = new HashSet<string>(StringComparer.Ordinal) { "this" };
            foreach (var param in ia.Parameters) {
                if (param.IsEntityRef) {
                    // Two parameters of one entity type each get their own local: the first keeps the type's name.
                    var lv = FreshLocal(locals, ToCamelCase(param.DomainType));
                    tryBody.Add(Init(lv, LoadByRouteKey(
                        Pluralize(param.DomainType),
                        _storageLookup[param.DomainType],
                        new Member(new Variable("dto"), ActionDtoPropertyName(param)))));
                    tryBody.Add(new IfStatement(new Equal(new Variable(lv), new Constant(null!)),
                        new Block(new Return(ProblemResponse(404, "Not Found", $"{param.DomainType} not found")))));
                    if (reads?.Roots.FirstOrDefault(r => r.Name == param.Name) is { } parameterReads) {
                        loadedRoots.Add(param.Name);
                        AppendReadLoads(tryBody, lv, parameterReads.Children, new HashSet<string>(), locals);
                    }
                    invokeArgs.Add(new Variable(lv));
                }
                else invokeArgs.Add(new Member(new Variable("dto"), ActionDtoPropertyName(param)));
            }
            if (reads is not null)
                RequireEveryRootLoaded(reads, loadedRoots);
            AppendContributedArguments(entity, ia, actionParams, invokeArgs);
            tryBody.Add(Init("result",
                invokeArgs.Count > 0
                    ? new Invoke(new Member(new Variable("entity"), ia.Name), [.. invokeArgs])
                    : new Invoke(new Member(new Variable("entity"), ia.Name))));

            // Success commits; a failure returns before any save, so the context discards its pending changes.
            var commit = new Await(new Invoke(new Member(new TypeReference("db"), "SaveChangesAsync")));
            var failure = new Block(DomainFailureReturn(new Variable("result")));
            if (ia.IsVoid) {
                tryBody.Add(new IfStatement(
                    new Member(new Variable("result"), "IsSuccess"),
                    new Block(commit, new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"), new Constant("ok")))),
                    failure));
            }
            else {
                tryBody.Add(new IfStatement(
                    new Member(new Variable("result"), "IsSuccess"),
                    new Block(commit, new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"), new Member(new Variable("result"), "Value")))),
                    failure));
            }

            // Catch: logs the exception, then a ProblemDetails 500 with no exception text.
            var catchBody = new Block(
                new Invoke(new Member(new Member(new TypeReference(AppVar), "Logger"), "LogError"),
                    new Variable("ex"), new Constant("Action {Action} failed"), new Constant($"{entity.Name}.{ia.Name}")),
                new Return(ProblemResponse(500, "Internal Server Error")));

            var bodyNodes = new List<Node>(preActionNodes);
            bodyNodes.Add(new TryCatchFinally(
                Stmts(tryBody),
                CatchClauses: [new CatchClause(new NamedTypeReference("Exception"), "ex", catchBody)]));

            statements.Add(new Invoke(
                new Member(new TypeReference(AppVar), "MapPost"),
                new Constant(actionRoute),
                new Lambda([.. actionParams],
                    Stmts(bodyNodes))));
        }
    }

    /// <summary>
    /// Loads what the action reads below <paramref name="variable"/>: for each node its navigation (unless
    /// <paramref name="loaded"/> already has it), then, for what is read through it, the same for the loaded target. A single
    /// link is read into a local and skipped when empty; a collection is walked item by item over a snapshot (the loads fill
    /// other collections by fixup, never this one). Every statement is explicit: no reflection and no string path.
    /// </summary>
    private static void AppendReadLoads(
        List<Node> sink, string variable, IReadOnlyList<ReadNode> children, IReadOnlySet<string> loaded, ISet<string> locals) {
        foreach (var child in children) {
            if (!loaded.Contains(child.Navigation))
                sink.Add(child.IsCollection ? LoadCollection(variable, child.Navigation) : LoadReference(variable, child.Navigation));
            if (child.Children.Count == 0)
                continue;
            var target = FreshLocal(locals, variable + child.Navigation);
            var below = new List<Node>();
            if (child.IsCollection) {
                var item = FreshLocal(locals, target + "Item");
                AppendReadLoads(below, item, child.Children, new HashSet<string>(), locals);
                sink.Add(new ForEachLoop(new Variable(item),
                    new Invoke(new Member(new Member(new Variable(variable), child.Navigation), "ToList")), Stmts(below)));
            }
            else {
                AppendReadLoads(below, target, child.Children, new HashSet<string>(), locals);
                sink.Add(Init(target, new Member(new Variable(variable), child.Navigation)));
                sink.Add(new IfStatement(new NotEqual(new Variable(target), new Constant(null!)), Stmts(below)));
            }
        }
    }

    /// <summary><paramref name="name"/>, or the first <c>name2</c>, <c>name3</c>, … not yet drawn from <paramref name="locals"/>.</summary>
    private static string FreshLocal(ISet<string> locals, string name) {
        var local = name;
        for (var n = 2; !locals.Add(local); n++)
            local = name + n;
        return local;
    }

    /// <summary>
    /// A tree root with reads that no host parameter consumed (a renamed parameter, a mapping bug) would load nothing and the
    /// action would answer from less than it reads. That is a generation error, never a quieter host.
    /// </summary>
    internal static void RequireEveryRootLoaded(ActionReads reads, IReadOnlySet<string> loadedRoots) {
        foreach (var root in reads.Roots) {
            if (root.Children.Count > 0 && !loadedRoots.Contains(root.Name))
                throw new ActionReadsRefusedException(
                    $"Action '{reads.Entity}.{reads.Action}' cannot be served over HTTP: it reads '{root.Render()}' through "
                    + $"'{root.Name}', which matches no parameter of the route.");
        }
    }

    /// <summary><c>await db.Entry(x).Collection(e =&gt; e.Nav).LoadAsync();</c></summary>
    private static Node LoadCollection(string entityVariable, string navigation) {
        var entry = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable(entityVariable));
        var collection = new Invoke(new Member(entry, "Collection"), new Lambda([new Parameter("e")], new Member(new Variable("e"), navigation)));
        return new Await(new Invoke(new Member(collection, "LoadAsync")));
    }

    /// <summary><c>await db.Entry(x).Reference(e =&gt; e.Nav).LoadAsync();</c></summary>
    private static Node LoadReference(string entityVariable, string navigation) {
        var entry = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable(entityVariable));
        var reference = new Invoke(new Member(entry, "Reference"), new Lambda([new Parameter("e")], new Member(new Variable("e"), navigation)));
        return new Await(new Invoke(new Member(reference, "LoadAsync")));
    }

    /// <summary>
    /// When a compiled constraint injected parameters, the lowered action lists
    /// them after the DSL parameters. The lambda already has a matching name
    /// (the host's <c>db</c>) or gains one. The entity call passes the same
    /// suffix. The lowered catalog is the bind surface; the generator never
    /// asks the runtime cache to lower again.
    /// </summary>
    private void AppendContributedArguments(
        Entity entity,
        BehaviorAction ia,
        List<Parameter> actionParams,
        List<Node> invokeArgs) {
        if (RuntimeAnalysisCache.Session(_domain).ConstraintCompilers.Count == 0)
            return;
        var method = ModuleMethod(entity, ia.Name);
        if (method?.Parameters is not { Count: > 0 } parameters)
            return;
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var param in ia.Parameters)
            declared.Add(param.Name);
        var arguments = new Dictionary<string, Node>(StringComparer.Ordinal);
        for (var i = 0; i < ia.Parameters.Count && i < invokeArgs.Count; i++)
            arguments[ia.Parameters[i].Name] = invokeArgs[i];
        invokeArgs.Clear();
        foreach (var parameter in parameters) {
            if (declared.Contains(parameter.Name)) {
                if (!arguments.TryGetValue(parameter.Name, out var argument))
                    throw new InvalidOperationException(
                        $"Action '{entity.Name}.{ia.Name}' carries module parameter '{parameter.Name}' "
                        + "without a route argument.");
                invokeArgs.Add(argument);
                continue;
            }
            if (!actionParams.Exists(p => string.Equals(p.Name, parameter.Name, StringComparison.Ordinal)))
                actionParams.Add(new Parameter(parameter.Name, parameter.TypeReference));
            invokeArgs.Add(new Variable(parameter.Name));
        }
    }
}