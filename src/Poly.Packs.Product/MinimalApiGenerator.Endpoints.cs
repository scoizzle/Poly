using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Libraries.Storage;

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
        if (store.IsRoot && CreatesDirectly(_entities, entity, GetCreateSlots(entity), _storageLookup)) {
            var dtoParam = new Parameter("dto", new TypeReference($"{entity.Name}Dto"));
            AppendCreateCallStatements(statements, entity, dtoParam, dbParam, route, key);
        }
    }

    /// <summary>
    /// True when a POST body can carry everything the entity's <c>Create</c> requires: every Required slot is a
    /// scalar, or a link whose target has a natural route key (the body carries the target's key; a target with a
    /// shadow key has no value a client could send). Otherwise the entity has no POST route, no create DTO, no
    /// sample request and no seed row, never a route that always answers 400; it is created through a parent
    /// action's <c>create in</c>. An entity-typed property or a Required collection also has no route; the DSL
    /// cannot declare either, only an API-built model reaches them. A link member that would share a name with a
    /// scalar member refuses the route too (two DTO members of one name).
    /// </summary>
    internal static bool CreatesDirectly(IEnumerable<Entity> entities, Entity entity, IReadOnlyList<CreateSlot> slots,
        IReadOnlyDictionary<string, StorageEntity> storage) {
        if (entity.Properties.Any(p => entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal))))
            return false;
        var scalarNames = slots.Where(s => s is { Shape: CreateSlotShape.Scalar, Kind: CreateSlotKind.Required })
            .Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in slots.Where(s => s is { Kind: CreateSlotKind.Required, Shape: not CreateSlotShape.Scalar })) {
            if (!IsRequiredLink(slot) || slot.IsSelfNavigation
                || !storage.TryGetValue(slot.Type.TypeName, out var target) || RouteKeyFor(target).Property is null
                || scalarNames.Contains(CreateLinkMemberName(slot.Name)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Appends Syntax IR for POST /api/entities endpoint — calls Entity.Create with result handling. Each Required
    /// link arrives as the target's route key and is looked up exactly as an action's entity argument is (404 when
    /// it does not exist); a missing or null key passes a null link, and the lowered <c>Create</c> answers Required.
    /// What the lowered create reads or writes on each loaded target (<see cref="ReadsOfCreate"/>) is loaded before the call.
    /// </summary>
    private void AppendCreateCallStatements(List<Node> statements, Entity entity,
        Parameter dtoParam, Parameter dbParam,
        string route, RouteKey key) {
        var resultVarName = $"{ToCamelCase(entity.Name)}Result";
        var locals = new HashSet<string>(["dto", "db", resultVarName], StringComparer.Ordinal);
        var bodyNodes = new List<Node>();

        var reads = _catalog is null ? null : ReadsOfCreate(entity);
        var loadedRoots = new HashSet<string>(StringComparer.Ordinal) { "this" };
        var linkLocals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in GetRequiredLinkSlots(entity)) {
            var target = slot.Type.TypeName;
            var local = FreshLocal(locals, ToCamelCase(target));
            linkLocals[slot.Name] = local;
            Node Given() => new Member(new Variable("dto"), CreateLinkMemberName(slot.Name));
            bodyNodes.Add(Init(local, new Conditional(
                new Equal(Given(), new Constant(null!)),
                new Constant(null!),
                LoadByRouteKey(Pluralize(target), _storageLookup[target], Given()))));
            bodyNodes.Add(new IfStatement(
                new Ast.Nodes.And(
                    new NotEqual(Given(), new Constant(null!)),
                    new Equal(new Variable(local), new Constant(null!))),
                new Block(new Return(ProblemResponse(404, "Not Found", $"{target} not found")))));
            var below = new List<Node>();
            if (reads is null) {
                // Shape-only generation has no lowered module to read: the one-hop floor of the target, the same floor an
                // action route loads. Production always passes the catalog (MinimalApiHostArtifactContributor).
                var targetStore = _storageLookup[target];
                below.AddRange(targetStore.CollectionNavigations.Select(n => LoadCollection(local, n.PropertyName)));
                below.AddRange(targetStore.ReferenceNavigations.Select(n => LoadReference(local, n.PropertyName)));
            }
            else if (reads.Roots.FirstOrDefault(r => r.Name == ToCamelCase(slot.Name)) is { } linkReads) {
                loadedRoots.Add(linkReads.Name);
                AppendReadLoads(below, local, linkReads.Children, new HashSet<string>(), locals);
            }
            if (below.Count > 0)
                bodyNodes.Add(new IfStatement(new NotEqual(new Variable(local), new Constant(null!)), Stmts(below)));
        }
        if (reads is not null)
            RequireEveryRootLoaded(reads, loadedRoots);

        var createArgs = BuildCreateCallArgs(entity,
            prop => new Member(new Variable("dto"), prop.Name),
            slot => new Variable(linkLocals[slot.Name]));

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

        // await db.SaveChangesAsync(), and on a host with a unique index, that index's refusal mapped to
        // the same 409 the pre-check gives (#562).
        bodyNodes.Add(SaveChangesWithUniqueConflict(entity, resultVarName, locals));

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

    /// <summary>
    /// <c>await db.SaveChangesAsync()</c>, and on a host whose provider enforces the unique index, the
    /// mapping of that index's refusal to the same 409 body the create's own pre-check gives (#562). The
    /// pre-check is check-then-act: two concurrent creates of one key both pass it, so the index is the
    /// authority. The catch re-reads the entity's unique columns against the value the create settled on
    /// (never the DTO's, which an applied default can differ from): the winning row is committed by the
    /// time the loser's save returns, so a property that now holds the value is the one that collided. A
    /// save failure no unique property accounts for keeps the 500 — the provider is never named, because
    /// naming its exception type forces its assembly into the host's reference closure (CS1701).
    /// </summary>
    private Node SaveChangesWithUniqueConflict(Entity entity, string resultVarName, ISet<string> locals) {
        var save = new Await(new Invoke(new Member(new TypeReference("db"), "SaveChangesAsync")));
        var uniqueColumns = GetStorageEntity(entity).Columns.Where(static column => column.IsUnique).ToList();
        if (!EnforcesUniqueIndex || uniqueColumns.Count == 0)
            return save;

        var created = new Member(new Variable(resultVarName), "Value");
        var row = new Parameter(FreshLocal(locals, "row"));
        var conflictBody = new List<Node>();
        foreach (var column in uniqueColumns) {
            var current = new Member(created, column.Name);
            // The port's own key encoding (DbContextGenerator.Keys): a string compares directly, any other
            // column through its ToString, so a numeric or temporal key is named as the guard names it.
            Node comparison = string.Equals(column.ClrTypeName, "string", StringComparison.Ordinal)
                ? new Equal(new Member(row, column.Name), current)
                : new Equal(
                    new Invoke(new Member(new Member(row, column.Name), "ToString")),
                    new Invoke(new Member(current, "ToString")));
            conflictBody.Add(new IfStatement(
                new Await(new Invoke(
                    new Member(new Member(new TypeReference("db"), Pluralize(entity.Name)), "AnyAsync"),
                    new Lambda([row], comparison))),
                new Block(DomainFailureProblem(
                    "Unique",
                    UniqueConstraintCompiler.Violation(entity.Name, column.Name),
                    $"{entity.Name}.{column.Name}"))));
        }

        return new TryCatchFinally(
            Stmts([save]),
            CatchClauses: [new CatchClause(new NamedTypeReference("DbUpdateException"), null, new Block([
                .. conflictBody,
                new Return(ProblemResponse(500, "Internal Server Error"))]))]);
    }

    /// <summary><c>return Problem(Poly.Runtime.DomainFailure.&lt;kind&gt;(…));</c> for a failure with no module behind it.</summary>
    private static Node DomainFailureProblem(string kind, string message, string? elementPath) {
        var failure = new Invoke(new Member(new NamedTypeReference("Poly.Runtime.DomainFailure"), kind),
            [new Constant(message), .. elementPath is null ? Array.Empty<Node>() : [new Constant(elementPath)]]);
        return new Return(new Invoke(new Variable("Problem"), failure));
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