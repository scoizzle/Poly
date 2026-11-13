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
            var keyName = store.KeyName;
            var keyType = store.KeyClrType;
            var pluralName = Pluralize(entity.Name);
            var parentCtx = GetStorageEntity(entity).IsRoot ? null : GetAggregateParent(entity);

            string actionRoute;
            List<Parameter> actionParams = [];
            List<Node> preActionNodes = [];

            if (parentCtx is { } ctx) {
                var (pEntity, relNameRaw, _) = ctx;
                var pStore = GetStorageEntity(pEntity);
                var pKeyName = pStore.KeyName;
                var pKeyType = pStore.KeyClrType;
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

                // Parent + entity lookup + membership check
                preActionNodes.Add(Init("parentEntity",
                    LoadByRouteKey(Pluralize(pEntity.Name), pStore, new Variable(pKeyName))));
                preActionNodes.Add(new IfStatement(new Equal(new Variable("parentEntity"), new Constant(null!)),
                    new Block(new Return(ProblemResponse(404, "Not Found", $"{pEntity.Name} not found")))));
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

            // Load collection navigations
            foreach (var nav in store.CollectionNavigations) {
                var en = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("entity"));
                var col = new Invoke(new Member(en, "Collection"), new Lambda([new Parameter("e")], new Member(new Variable("e"), nav.PropertyName)));
                preActionNodes.Add(new Await(new Invoke(new Member(col, "LoadAsync"))));
            }

            // ── Try body: entity-ref lookups + invoke + result switch ──
            var tryBody = new List<Node>();
            var invokeArgs = new List<Node>();
            foreach (var param in ia.Parameters) {
                if (param.IsEntityRef) {
                    var lv = ToCamelCase(param.DomainType);
                    tryBody.Add(Init(lv, LoadByRouteKey(
                        Pluralize(param.DomainType),
                        _storageLookup[param.DomainType],
                        new Member(new Variable("dto"), ActionDtoPropertyName(param)))));
                    tryBody.Add(new IfStatement(new Equal(new Variable(lv), new Constant(null!)),
                        new Block(new Return(ProblemResponse(404, "Not Found", $"{param.DomainType} not found")))));
                    invokeArgs.Add(new Variable(lv));
                }
                else invokeArgs.Add(new Member(new Variable("dto"), ActionDtoPropertyName(param)));
            }
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