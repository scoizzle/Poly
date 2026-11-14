using Poly.Ast;
using Poly.Ast.Nodes;

namespace Poly.Packs.Product;

// MinimalApiGenerator (Endpoints) — CRUD and child-entity endpoint statements.
public sealed partial class MinimalApiGenerator {
    /// <summary>Appends Syntax IR for a root entity's CRUD endpoints (matching string path).</summary>
    private void AppendEndpointStatements(List<Node> statements, Entity entity, string dbContextName) {
        var store = GetStorageEntity(entity);
        var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
        var key = RouteKeyFor(store);
        var keyName = key.Name;
        var keyType = key.ClrType;
        var dbParam = new Parameter("db", new TypeReference(dbContextName));

        // app.MapGet("/api/books", async (dbContextName db) => await db.Books.ToListAsync())
        statements.Add(new Invoke(
            new Member(new TypeReference(AppVar), "MapGet"),
            new Constant(route),
            new Lambda([dbParam],
                new Await(
                    new Invoke(
                        new Member(
                            new Member(new TypeReference("db"), Pluralize(entity.Name)),
                            "ToListAsync"))))));

        // app.MapGet("/api/books/{key}", async (keyType key, dbContextName db) =>
        //     await db.Books.FindAsync(key) is Book book ? Results.Ok(book) : a 404 ProblemDetails)
        // A natural key compares the unique property; FindAsync is the shadow key.
        var keyParam = new Parameter(keyName, ClrTypeNode(keyType));
        var camelName = ToCamelCase(entity.Name);
        statements.Add(new Invoke(
            new Member(new TypeReference(AppVar), "MapGet"),
            new Constant($"{route}/{{{keyName}}}"),
            new Lambda([keyParam, dbParam],
                new Conditional(
                    new TypeIs(
                        LoadByRouteKey(Pluralize(entity.Name), store, new Variable(keyName)),
                        new NamedTypeReference(VisibleType(entity.Name))) {
                        VariableName = camelName
                    },
                    new Invoke(new Member(new TypeReference("Results"), "Ok"),
                        new Variable(camelName)),
                    ProblemResponse(404, "Not Found")))));

        // POST endpoint for root entities (S3: proper Entity.Create + result handling)
        if (store.IsRoot && CreatesDirectly(_entities, entity)) {
            var dtoParam = new Parameter("dto", new TypeReference($"{entity.Name}Dto"));
            AppendCreateCallStatements(statements, entity, dtoParam, dbParam, route, key);
        }
    }

    /// <summary>
    /// False for an entity with an entity-typed property: such an entity has no POST route (and no sample
    /// request), never a route that always answers 400. The DSL cannot declare that property; only an
    /// API-built model reaches it.
    /// </summary>
    internal static bool CreatesDirectly(IEnumerable<Entity> entities, Entity entity) =>
        !entity.Properties.Any(p => entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal)));

    /// <summary>Appends Syntax IR for POST /api/entities endpoint — calls Entity.Create with result handling.</summary>
    private void AppendCreateCallStatements(List<Node> statements, Entity entity,
        Parameter dtoParam, Parameter dbParam,
        string route, RouteKey key) {
        var createArgs = BuildCreateCallArgs(entity,
            prop => new Member(new Variable("dto"), prop.Name));

        var resultVarName = $"{ToCamelCase(entity.Name)}Result";
        var bodyNodes = new List<Node>();

        // var result = Entity.Create(...)
        bodyNodes.Add(Init(resultVarName,
            new Invoke(new Member(new TypeReference(VisibleType(entity.Name)), "Create"), [.. createArgs])));

        // The failure's typed kind picks the status (DomainFailureStatus.Http).
        bodyNodes.Add(new IfStatement(
            new Ast.Nodes.Not(new Member(new Variable(resultVarName), "IsSuccess")),
            new Block(DomainFailureReturn(new Variable(resultVarName)))));

        // db.Set.Add(result.Value)
        bodyNodes.Add(new Invoke(
            new Member(
                new Member(new TypeReference("db"), Pluralize(entity.Name)),
                "Add"),
            new Member(new Variable(resultVarName), "Value")));

        // await db.SaveChangesAsync()
        bodyNodes.Add(new Await(
            new Invoke(new Member(new TypeReference("db"), "SaveChangesAsync"))));

        // return Results.Ok(result.Value) or Results.Created(uri, result.Value)
        if (key.Property is not null) {
            var uri = new Invoke(
                new Member(new TypeReference("string"), "Concat"),
                new Constant(route + "/"),
                RouteKeySegment(key, new Member(
                    new Member(new Variable(resultVarName), "Value"),
                    key.Property.Name)));
            bodyNodes.Add(new Return(
                new Invoke(
                    new Member(new TypeReference("Results"), "Created"),
                    uri,
                    new Member(new Variable(resultVarName), "Value"))));
        }
        else {
            bodyNodes.Add(new Return(
                new Invoke(
                    new Member(new TypeReference("Results"), "Ok"),
                    new Member(new Variable(resultVarName), "Value"))));
        }

        statements.Add(new Invoke(
            new Member(new TypeReference(AppVar), "MapPost"),
            new Constant(route),
            new Lambda([dtoParam, dbParam],
                Stmts(bodyNodes))));
    }

    /// <summary>Builds Syntax IR for child entity list/detail endpoints (matching string oracle).</summary>
    private void AppendChildEndpointStatements(List<Node> statements, Entity entity,
        Entity parentEntity, string relNameRaw, string? backRefRaw, string dbContextName) {
        var childStore = GetStorageEntity(entity);
        var parentStore = GetStorageEntity(parentEntity);
        var relName = ToCamelCase(relNameRaw);
        var parentRouteKey = RouteKeyFor(parentStore);
        var childRouteKey = RouteKeyFor(childStore);
        var parentKey = parentRouteKey.Name;
        var parentKeyType = parentRouteKey.ClrType;
        var childKey = childRouteKey.Name;
        var childKeyType = childRouteKey.ClrType;
        var childKeyParam = DistinctChildKeyParam(parentKey, childKey, entity.Name);
        var pascalRel = Pascalize(relNameRaw);
        var isCollection = IsCollectionNavigation(parentStore, pascalRel);
        var pluralParent = Pluralize(parentEntity.Name);
        var listRoute = $"/api/{Pluralize(parentEntity.Name).ToLowerInvariant()}/{{{parentKey}}}/{relName.ToLowerInvariant()}";
        var detailRoute = $"{listRoute}/{{{childKeyParam}}}";
        var parentKeyP = new Parameter(parentKey, ClrTypeNode(parentKeyType));
        var childKeyP = new Parameter(childKeyParam, ClrTypeNode(childKeyType));
        var dbP = new Parameter("db", new TypeReference(dbContextName));

        // List: Collection for many, Reference for to-one — use lambda param e, not parent
        var eParam = new Parameter("e");
        var entryCall = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parent"));
        var loadMember = isCollection ? "Collection" : "Reference";
        var collCall = new Invoke(
            new Member(entryCall, loadMember),
            new Lambda([eParam], new Member(eParam, pascalRel)));

        statements.Add(new Invoke(
            new Member(new TypeReference(AppVar), "MapGet"),
            new Constant(listRoute),
            new Lambda([parentKeyP, dbP],
                Stmts([
                    Init("parent",
                        LoadByRouteKey(pluralParent, parentStore, new Variable(parentKey))),
                    new IfStatement(new Equal(new Variable("parent"), new Constant(null!)),
                        new Block(new Return(
                            ProblemResponse(404, "Not Found")))),
                    new Await(new Invoke(new Member(collCall, "LoadAsync"))),
                    new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"),
                        new Member(new Variable("parent"), pascalRel)))
                ]))));

        if (!isCollection)
            return;

        // Detail: back-ref filtering matching string oracle
        var detailBody = new List<Node>();
        var backRefName = backRefRaw != null ? Pascalize(backRefRaw) : null;
        var parentKeyPropName = parentRouteKey.Property?.Name;
        var childKeyPropName = childRouteKey.Property?.Name;

        if (childKeyPropName != null && backRefName != null && parentKeyPropName != null) {
            var eParam2 = new Parameter("e");
            detailBody.Add(Init("child",
                new Await(
                    new Invoke(
                        new Member(
                            new Invoke(
                                new Member(
                                    new Member(new TypeReference("db"), Pluralize(entity.Name)),
                                    "Where"),
                                new Lambda([eParam2],
                                    new Ast.Nodes.And(
                                        new Ast.Nodes.Not(new Equal(
                                            new Member(eParam2, backRefName),
                                            new Constant(null!))),
                                        new Equal(
                                            new Member(new Member(eParam2, backRefName), parentKeyPropName),
                                            new Variable(parentKey))))),
                            "FirstOrDefaultAsync"),
                        new Lambda([eParam2],
                            new Equal(
                                new Member(eParam2, childKeyPropName),
                                new Variable(childKeyParam)))))));
            detailBody.Add(new IfStatement(new Equal(new Variable("child"), new Constant(null!)),
                new Block(new Return(ProblemResponse(404, "Not Found")))));
        }
        else {
            detailBody.Add(Init("parent",
                LoadByRouteKey(pluralParent, parentStore, new Variable(parentKey))));
            detailBody.Add(new IfStatement(new Equal(new Variable("parent"), new Constant(null!)),
                new Block(new Return(ProblemResponse(404, "Not Found")))));
            var colParam = new Parameter("e");
            var detailCollCall = new Invoke(
                new Member(
                    new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parent")),
                    "Collection"),
                new Lambda([colParam], new Member(colParam, pascalRel)));
            detailBody.Add(new Await(new Invoke(new Member(detailCollCall, "LoadAsync"))));
            if (childKeyPropName != null) {
                var match = new Parameter("e");
                detailBody.Add(Init("child",
                    new Invoke(
                        new Member(new Member(new Variable("parent"), pascalRel), "FirstOrDefault"),
                        new Lambda([match],
                            new Equal(
                                new Member(match, childKeyPropName),
                                new Variable(childKeyParam))))));
                detailBody.Add(new IfStatement(new Equal(new Variable("child"), new Constant(null!)),
                    new Block(new Return(ProblemResponse(404, "Not Found")))));
            }
            else {
                // Shadow key: no CLR property to compare. The route id is that key.
                detailBody.Add(Init("child",
                    LoadByRouteKey(Pluralize(entity.Name), childStore, new Variable(childKeyParam))));
                var match = new Parameter("e");
                detailBody.Add(new IfStatement(
                    new Ast.Nodes.Or(
                        new Equal(new Variable("child"), new Constant(null!)),
                        new Ast.Nodes.Not(
                            new Invoke(
                                new Member(new Member(new Variable("parent"), pascalRel), "Any"),
                                new Lambda([match],
                                    new Equal(new Variable("e"), new Variable("child")))))),
                    new Block(new Return(
                        ProblemResponse(404, "Not Found")))));
            }
        }
        detailBody.Add(new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"),
            new Variable("child"))));

        statements.Add(new Invoke(
            new Member(new TypeReference(AppVar), "MapGet"),
            new Constant(detailRoute),
            new Lambda([parentKeyP, childKeyP, dbP],
                Stmts(detailBody))));
    }
}