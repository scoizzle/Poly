using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;
using Poly.Syntax;
using Poly.Syntax.Nodes;

namespace Poly.DslCompiler;

/// <summary>
/// Generates an ASP.NET Minimal API Program.cs from a <see cref="Domain"/>.
///
/// Consumes AggregateModel / StorageModel / BehaviorModel from infrastructure
/// analysis for root detection, parent routing, keys, and action shapes.
/// </summary>
public sealed class MinimalApiGenerator {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _domainName;
    private readonly Dictionary<string, StorageEntity> _storageLookup;
    private readonly Dictionary<string, BehaviorEntity> _behaviorLookup;
    private readonly Dictionary<string, AggregateEntity> _aggregateLookup;

    private readonly IStorageSyntaxEmitter? _emitter;

    public MinimalApiGenerator(Domain domain,
        StorageModel storageModel,
        BehaviorModel behaviorModel,
        AggregateModel aggregateModel,
        IStorageSyntaxEmitter? emitter = null) {
        _emitter = emitter;
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _domainName = domain.Name;
        _storageLookup = storageModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _behaviorLookup = behaviorModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _aggregateLookup = aggregateModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    private IReadOnlyList<BehaviorAction> GetBehaviorActions(Entity entity) =>
        _behaviorLookup.TryGetValue(entity.Name, out var beh) ? beh.Actions : [];

    private StorageEntity GetStorageEntity(Entity entity) => _storageLookup[entity.Name];

    private AggregateEntity GetAggregateEntity(Entity entity) => _aggregateLookup[entity.Name];

    /// <summary>
    /// Parent context for a child entity from AggregateModel.
    /// </summary>
    private (Entity Parent, string RelName, string? BackRefName)? GetAggregateParent(Entity child) {
        var agg = GetAggregateEntity(child);
        if (agg.IsRoot || agg.AggregateParentName is null || agg.ParentRelationshipName is null)
            return null;
        var parent = _entities.FirstOrDefault(e =>
            string.Equals(e.Name, agg.AggregateParentName, StringComparison.Ordinal));
        if (parent is null) return null;
        return (parent, agg.ParentRelationshipName, agg.BackReferencePropertyName);
    }

    /// <summary>Generates the Minimal API Program.cs via Syntax IR.</summary>
    public string Generate(string dbContextName) =>
        new CSharpGenerator().Generate(GenerateCompilationUnit(dbContextName));

    private readonly string BuilderVar = "builder";
    private readonly string AppVar = "app";

    private static string Pluralize(string name) => name + "s";
    private static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);
    private static string Pascalize(string name) => DomainTypeMapping.ToPascalCase(name);
    private static string GetClrTypeName(string domainType) => DomainTypeMapping.ToClrTypeName(domainType);

    /// <summary>Generates the Minimal API Program.cs as a Syntax IR compilation unit.</summary>
    public CompilationUnitNode GenerateCompilationUnit(string dbContextName) {
        var dtoTypes = new List<TypeDefinitionNode>();
        var topLevelStatements = new List<Node>();

        // ── Builder setup (Issue 9: CreateBuilder(args) not args.Clone) ──
        topLevelStatements.Add(new Variable(BuilderVar,
            new Invoke(new Member(new TypeReference("WebApplication"), "CreateBuilder"),
                new Variable("args"))));

        // ── JSON config ──
        var optionsParam = new Parameter("options");
        topLevelStatements.Add(new Invoke(
            new Member(
                new Member(new TypeReference(BuilderVar), "Services"),
                "ConfigureHttpJsonOptions"),
            new Lambda([optionsParam], new Block(
                new Assignment(
                    new Member(new Member(optionsParam, "SerializerOptions"), "ReferenceHandler"),
                    new Member(new TypeReference("ReferenceHandler"), "IgnoreCycles")),
                new Invoke(
                    new Member(
                        new Member(new Member(optionsParam, "SerializerOptions"), "Converters"),
                        "Add"),
                    new New(new TypeReference("JsonStringEnumConverter")))))));

        // ── EF Core InMemory ──
        var dbOptionsParam = new Parameter("options");
        topLevelStatements.Add(new Invoke(
            new Member(
                new Member(new TypeReference(BuilderVar), "Services"),
                "AddDbContext"),
            new Lambda([dbOptionsParam],
                new Invoke(new Member(dbOptionsParam, "UseInMemoryDatabase"),
                    new Constant(_domainName)))) {
            TypeArguments = [new TypeReference(dbContextName)]
        });

        // ── Build + seed ──
        topLevelStatements.Add(new Variable(AppVar,
            new Invoke(new Member(new TypeReference(BuilderVar), "Build"))));

        // Issue 10: using (var scope = app.Services.CreateScope()) { ... }
        var scopeVar = new Variable("scope",
            new Invoke(new Member(
                new Member(new TypeReference(AppVar), "Services"),
                "CreateScope")));
        var dbVar = new Variable("db",
            new Invoke(
                new Member(
                    new Member(new Variable("scope"), "ServiceProvider"),
                    "GetRequiredService")) {
                TypeArguments = [new TypeReference(dbContextName)]
            });
        var seedCall = new Await(new Invoke(new Variable("SeedAsync"), new Variable("db")));
        topLevelStatements.Add(new UsingStatement(scopeVar,
            new Block(new Node[] { dbVar, seedCall }, Array.Empty<Node>())));

        // ── Endpoints ──
        foreach (var entity in _entities.Where(e => GetStorageEntity(e).IsRoot))
            AppendEndpointStatements(topLevelStatements, entity, dbContextName);

        // Child list endpoints (S4)
        foreach (var entity in _entities.Where(e => !GetStorageEntity(e).IsRoot)) {
            var parentCtx = GetAggregateParent(entity);
            if (parentCtx is null) continue;
            var ctx = parentCtx.Value;
            AppendChildEndpointStatements(topLevelStatements, entity, ctx.Parent, ctx.RelName, ctx.BackRefName, dbContextName);
        }

        // Action endpoints (S4)
        foreach (var entity in _entities)
            AppendActionEndpointStatements(topLevelStatements, entity, dbContextName);

        // ── SeedAsync local function followed by DTOs ──
        AppendSeedMethodStatements(topLevelStatements, dbContextName);

        // ── DTOs ──
        foreach (var entity in _entities)
            BuildDtoTypes(dtoTypes, entity);
        // Action DTOs
        foreach (var entity in _entities)
            BuildActionDtoTypes(dtoTypes, entity);

        // ── app.Run() ──
        topLevelStatements.Add(new Invoke(new Member(new TypeReference(AppVar), "Run")));

        var unit = new CompilationUnitNode(
            Usings: ["System.Text.Json", "System.Text.Json.Serialization", "Microsoft.EntityFrameworkCore", "Poly.Generated"],
            Namespace: null,
            Types: dtoTypes,
            TopLevelStatements: topLevelStatements
        );

        // Allow storage emitter to decorate the tree (no-op when null)
        if (_emitter != null) {
            // Build a flat lookup of all storage entities for the emitter
            var storageEntities = _storageLookup.Values.ToList();
            return _emitter.EmitApi(unit, storageEntities, null);
        }

        return unit;
    }

    /// <summary>Appends Syntax IR for a root entity's CRUD endpoints (matching string path).</summary>
    private void AppendEndpointStatements(List<Node> statements, Entity entity, string dbContextName) {
        var store = GetStorageEntity(entity);
        var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
        var keyName = store.KeyName;
        var keyType = store.KeyClrType;
        var keyProp = store.KeyProperty;
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
        //     await db.Books.FindAsync(key) is Book book ? Results.Ok(book) : Results.NotFound())
        var keyParam = new Parameter(keyName, new TypeReference(keyType));
        var camelName = ToCamelCase(entity.Name);
        statements.Add(new Invoke(
            new Member(new TypeReference(AppVar), "MapGet"),
            new Constant($"{route}/{{{keyName}}}"),
            new Lambda([keyParam, dbParam],
                new Conditional(
                    new TypeIs(
                        new Await(
                            new Invoke(
                                new Member(
                                    new Member(new TypeReference("db"), Pluralize(entity.Name)),
                                    "FindAsync"),
                                new Variable(keyName))),
                        new NamedTypeReference(entity.Name)) {
                        VariableName = camelName
                    },
                    new Invoke(new Member(new TypeReference("Results"), "Ok"),
                        new Variable(camelName)),
                    new Invoke(new Member(new TypeReference("Results"), "NotFound"))))));

        // POST endpoint for root entities (S3: proper Entity.Create + result handling)
        if (store.IsRoot) {
            var dtoParam = new Parameter("dto", new TypeReference($"{entity.Name}Dto"));
            AppendCreateCallStatements(statements, entity, store, dtoParam, dbParam, route, keyProp, dbContextName);
        }
    }

    /// <summary>Appends Syntax IR for POST /api/entities endpoint — calls Entity.Create with result handling.</summary>
    private void AppendCreateCallStatements(List<Node> statements, Entity entity,
        StorageEntity store, Parameter dtoParam, Parameter dbParam,
        string route, Property? uniqueProp, string dbContextName) {
        // Check if entity references other entities (cannot create directly) — emit BadRequest endpoint
        if (entity.Properties.Any(p => _entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal)))) {
            statements.Add(new Invoke(
                new Member(new TypeReference(AppVar), "MapPost"),
                new Constant(route),
                new Lambda([dtoParam, dbParam],
                    new Block(new Return(
                        new Invoke(
                            new Member(new TypeReference("Results"), "BadRequest"),
                            new Constant($"{entity.Name} requires related entities and cannot be created directly.")))))));
            return;
        }

        // Build Entity.Create(dto.Prop1, dto.Prop2, ...) call
        var createArgs = new List<Node>();
        foreach (var prop in entity.Properties
            .Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint))
            .OrderBy(p => p.Name)) {
            if (_entities.Any(e => string.Equals(e.Name, prop.Type.TypeName, StringComparison.Ordinal)))
                break;
            createArgs.Add(new Member(new Variable("dto"), prop.Name));
        }
        foreach (var rel in _domain.Relationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
            if (rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                createArgs.Add(new Invoke(new Member(new TypeReference("Enumerable"), "Empty")) {
                    TypeArguments = [new TypeReference(rel.Target.TypeName)]
                });
        }

        var resultVarName = $"{ToCamelCase(entity.Name)}Result";
        var bodyNodes = new List<Node>();

        // var result = Entity.Create(...)
        bodyNodes.Add(new Variable(resultVarName,
            new Invoke(new Member(new TypeReference(entity.Name), "Create"), [.. createArgs])));

        // if (!result.IsSuccess) return Results.Conflict(new { error = result.ErrorMessage });
        bodyNodes.Add(new IfStatement(
            new Poly.Syntax.Nodes.Not(new Member(new Variable(resultVarName), "IsSuccess")),
            new Block(
                new Return(
                    new Invoke(
                        new Member(new TypeReference("Results"), "Conflict"),
                        new Member(new Variable(resultVarName), "ErrorMessage"))))));

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
        if (uniqueProp is not null) {
            // Build URI: route + "/" + result.Value.KeyProp.ToString()
            var keyAccess = new Invoke(
                new Member(
                    new Member(
                        new Member(new Variable(resultVarName), "Value"),
                        uniqueProp.Name),
                    "ToString"));
            var uri = new Invoke(
                new Member(new TypeReference("string"), "Concat"),
                new Constant(route + "/"),
                keyAccess);
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
                new Block(expressions: bodyNodes, variables: Array.Empty<Node>()))));
    }

    /// <summary>Builds Syntax IR for child entity list/detail endpoints (matching string oracle).</summary>
    private void AppendChildEndpointStatements(List<Node> statements, Entity entity,
        Entity parentEntity, string relNameRaw, string? backRefRaw, string dbContextName) {
        var childStore = GetStorageEntity(entity);
        var parentStore = GetStorageEntity(parentEntity);
        var relName = ToCamelCase(relNameRaw);
        var parentKey = parentStore.KeyName;
        var parentKeyType = parentStore.KeyClrType;
        var childKey = childStore.KeyName;
        var childKeyType = childStore.KeyClrType;
        var pascalRel = Pascalize(relNameRaw);
        var pluralParent = Pluralize(parentEntity.Name);
        var listRoute = $"/api/{Pluralize(parentEntity.Name).ToLowerInvariant()}/{{{parentKey}}}/{relName.ToLowerInvariant()}";
        var detailRoute = $"{listRoute}/{{{childKey}}}";
        var parentKeyP = new Parameter(parentKey, new TypeReference(parentKeyType));
        var childKeyP = new Parameter(childKey, new TypeReference(childKeyType));
        var dbP = new Parameter("db", new TypeReference(dbContextName));

        // List: db.Entry(parent).Collection(e => e.Rel).LoadAsync() — use lambda param e, not parent
        var eParam = new Parameter("e");
        var entryCall = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parent"));
        var collCall = new Invoke(
            new Member(entryCall, "Collection"),
            new Lambda([eParam], new Member(eParam, pascalRel)));

        statements.Add(new Invoke(
            new Member(new TypeReference(AppVar), "MapGet"),
            new Constant(listRoute),
            new Lambda([parentKeyP, dbP],
                new Block(expressions: new Node[] {
                    new Variable("parent",
                        new Await(new Invoke(
                            new Member(new Member(new TypeReference("db"), pluralParent), "FindAsync"),
                            new Variable(parentKey)))),
                    new IfStatement(new Equal(new Variable("parent"), new Constant(null!)),
                        new Block(new Return(
                            new Invoke(new Member(new TypeReference("Results"), "NotFound"))))),
                    new Await(new Invoke(new Member(collCall, "LoadAsync"))),
                    new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"),
                        new Member(new Variable("parent"), pascalRel)))
                }, variables: Array.Empty<Node>()))));

        // Detail: back-ref filtering matching string oracle
        var detailBody = new List<Node>();
        var backRefName = backRefRaw != null ? Pascalize(backRefRaw) : null;
        var parentKeyPropName = parentStore.KeyProperty?.Name;
        var childKeyPropName = childStore.KeyProperty?.Name;

        if (childKeyPropName != null && backRefName != null && parentKeyPropName != null) {
            var eParam2 = new Parameter("e");
            detailBody.Add(new Variable("child",
                new Await(
                    new Invoke(
                        new Member(
                            new Invoke(
                                new Member(
                                    new Member(new TypeReference("db"), Pluralize(entity.Name)),
                                    "Where"),
                                new Lambda([eParam2],
                                    new Equal(
                                        new Member(new Member(eParam2, backRefName), parentKeyPropName),
                                        new Variable(parentKey)))),
                            "FirstOrDefaultAsync"),
                        new Lambda([eParam2],
                            new Equal(
                                new Member(eParam2, childKeyPropName),
                                new Variable(childKey)))))));
        }
        else {
            detailBody.Add(new Variable("parent",
                new Await(new Invoke(
                    new Member(new Member(new TypeReference("db"), pluralParent), "FindAsync"),
                    new Variable(parentKey)))));
            detailBody.Add(new IfStatement(new Equal(new Variable("parent"), new Constant(null!)),
                new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"))))));
            var colParam = new Parameter("e");
            var detailCollCall = new Invoke(
                new Member(
                    new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parent")),
                    "Collection"),
                new Lambda([colParam], new Member(colParam, pascalRel)));
            detailBody.Add(new Await(new Invoke(new Member(detailCollCall, "LoadAsync"))));
            detailBody.Add(new Variable("child",
                new Invoke(new Member(new Member(new Variable("parent"), pascalRel), "FirstOrDefault"))));
        }
        detailBody.Add(new IfStatement(new Equal(new Variable("child"), new Constant(null!)),
            new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"))))));
        detailBody.Add(new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"),
            new Variable("child"))));

        statements.Add(new Invoke(
            new Member(new TypeReference(AppVar), "MapGet"),
            new Constant(detailRoute),
            new Lambda([parentKeyP, childKeyP, dbP],
                new Block(expressions: detailBody, variables: Array.Empty<Node>()))));
    }

    /// <summary>Builds Syntax IR for action endpoints with try/catch, result switch, and entity-ref lookups.</summary>
    private void AppendActionEndpointStatements(List<Node> statements, Entity entity, string dbContextName) {
        foreach (var ia in GetBehaviorActions(entity)) {
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
                var parentRoute = $"/api/{Pluralize(pEntity.Name).ToLowerInvariant()}/{{{pKeyName}}}/{ToCamelCase(relNameRaw).ToLowerInvariant()}";
                actionRoute = $"{parentRoute}/{{{keyName}}}/{ToCamelCase(ia.Name).ToLowerInvariant()}";
                actionParams.Add(new Parameter(pKeyName, new TypeReference(pKeyType)));
                actionParams.Add(new Parameter(keyName, new TypeReference(keyType)));
                actionParams.Add(new Parameter("db", new TypeReference(dbContextName)));

                // Parent + entity lookup + membership check
                preActionNodes.Add(new Variable("parentEntity",
                    new Await(new Invoke(new Member(new Member(new TypeReference("db"), Pluralize(pEntity.Name)), "FindAsync"), new Variable(pKeyName)))));
                preActionNodes.Add(new IfStatement(new Equal(new Variable("parentEntity"), new Constant(null!)),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{pEntity.Name} not found"))))));
                preActionNodes.Add(new Variable("entity",
                    new Await(new Invoke(new Member(new Member(new TypeReference("db"), pluralName), "FindAsync"), new Variable(keyName)))));
                preActionNodes.Add(new IfStatement(new Equal(new Variable("entity"), new Constant(null!)),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{entity.Name} not found"))))));
                var parEntry = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parentEntity"));
                var parColl = new Invoke(new Member(parEntry, "Collection"), new Lambda([new Parameter("e")], new Member(new Variable("e"), pascalRel)));
                preActionNodes.Add(new Await(new Invoke(new Member(parColl, "LoadAsync"))));
                preActionNodes.Add(new IfStatement(
                    new Poly.Syntax.Nodes.Not(
                        new Invoke(
                            new Member(new Member(new Variable("parentEntity"), pascalRel), "Any"),
                            new Lambda([new Parameter("e")], new Equal(new Variable("e"), new Variable("entity"))))),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{entity.Name} not found for this {pEntity.Name}"))))));
            }
            else {
                var baseRoute = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
                actionRoute = $"{baseRoute}/{{{keyName}}}/{ToCamelCase(ia.Name).ToLowerInvariant()}";
                actionParams.Add(new Parameter(keyName, new TypeReference(keyType)));
                if (ia.Parameters.Count > 0)
                    actionParams.Add(new Parameter("dto", new TypeReference($"{Pascalize(ia.Name)}Dto")));
                actionParams.Add(new Parameter("db", new TypeReference(dbContextName)));

                preActionNodes.Add(new Variable("entity",
                    new Await(new Invoke(new Member(new Member(new TypeReference("db"), pluralName), "FindAsync"), new Variable(keyName)))));
                preActionNodes.Add(new IfStatement(new Equal(new Variable("entity"), new Constant(null!)),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{entity.Name} not found"))))));
            }

            // Load collection navigations
            foreach (var rel in _domain.Relationships) {
                if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
                if (rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany) {
                    var en = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("entity"));
                    var col = new Invoke(new Member(en, "Collection"), new Lambda([new Parameter("e")], new Member(new Variable("e"), Pascalize(rel.Name))));
                    preActionNodes.Add(new Await(new Invoke(new Member(col, "LoadAsync"))));
                }
            }

            // ── Try body: entity-ref lookups + invoke + result switch ──
            var tryBody = new List<Node>();
            var invokeArgs = new List<Node>();
            foreach (var param in ia.Parameters) {
                if (param.IsEntityRef) {
                    var lv = ToCamelCase(param.DomainType);
                    tryBody.Add(new Variable(lv, new Await(new Invoke(
                        new Member(new Member(new TypeReference("db"), Pluralize(param.DomainType)), "FindAsync"),
                        new Member(new Variable("dto"), $"{param.Name}Id")))));
                    tryBody.Add(new IfStatement(new Equal(new Variable(lv), new Constant(null!)),
                        new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{param.DomainType} not found"))))));
                    invokeArgs.Add(new Variable(lv));
                }
                else invokeArgs.Add(new Member(new Variable("dto"), param.Name));
            }
            tryBody.Add(new Variable("result",
                invokeArgs.Count > 0
                    ? new Invoke(new Member(new Variable("entity"), ia.Name), [.. invokeArgs])
                    : new Invoke(new Member(new Variable("entity"), ia.Name))));
            tryBody.Add(new Await(new Invoke(new Member(new TypeReference("db"), "SaveChangesAsync"))));

            // Result switch: IsSuccess → Ok, else → Conflict
            if (ia.IsVoid) {
                tryBody.Add(new IfStatement(
                    new Member(new Variable("result"), "IsSuccess"),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"), new Constant("ok")))),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "Conflict"), new Member(new Variable("result"), "ErrorMessage"))))));
            }
            else {
                tryBody.Add(new IfStatement(
                    new Member(new Variable("result"), "IsSuccess"),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"), new Member(new Variable("result"), "Value")))),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "Conflict"), new Member(new Variable("result"), "ErrorMessage"))))));
            }

            // Catch: returns Results.StatusCode(500)
            var catchBody = new Return(
                new Invoke(new Member(new TypeReference("Results"), "StatusCode"), new Constant(500)));

            var bodyNodes = new List<Node>(preActionNodes);
            bodyNodes.Add(new TryCatchFinally(
                new Block(expressions: tryBody, variables: Array.Empty<Node>()),
                CatchClauses: [new CatchClause(new NamedTypeReference("Exception"), "ex", catchBody)]));

            statements.Add(new Invoke(
                new Member(new TypeReference(AppVar), "MapPost"),
                new Constant(actionRoute),
                new Lambda([.. actionParams],
                    new Block(expressions: bodyNodes, variables: Array.Empty<Node>()))));
        }
    }

    /// <summary>Builds Syntax IR for action DTO types.</summary>
    private void BuildActionDtoTypes(List<TypeDefinitionNode> dtoTypes, Entity entity) {
        foreach (var ia in GetBehaviorActions(entity)) {
            if (ia.Parameters.Count == 0) continue;
            var fields = new List<Parameter>();
            foreach (var param in ia.Parameters) {
                if (param.IsEntityRef)
                    fields.Add(new Parameter($"{param.Name}Id", new TypeReference("string")));
                else
                    fields.Add(new Parameter(param.Name, new TypeReference(GetClrTypeName(param.DomainType))));
            }
            dtoTypes.Add(new TypeDefinitionNode(
                $"{Pascalize(ia.Name)}Dto",
                PrimaryConstructorParameters: fields,
                Semantics: new TypeDefinitionSemantics(
                    TypeDefinitionMutability.Immutable,
                    TypeDefinitionEqualitySemantics.Value
                )
            ));
        }
    }

    /// <summary>Builds Syntax IR for a DTO type definition.</summary>
    private void BuildDtoTypes(List<TypeDefinitionNode> dtoTypes, Entity entity) {
        var scalarProps = entity.Properties
            .Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint))
            .Where(p => !_entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal)))
            .OrderBy(p => p.Name)
            .ToList();

        if (scalarProps.Count == 0) return;
        if (!GetStorageEntity(entity).IsRoot) return;

        // Emit as positional record (primary constructor params), matching string path — uses PascalCase
        var primaryParams = scalarProps.Select(prop =>
            new Parameter(prop.Name, new TypeReference(GetClrTypeName(prop.Type.TypeName)))
        ).ToList();

        dtoTypes.Add(new TypeDefinitionNode(
            $"{entity.Name}Dto",
            PrimaryConstructorParameters: primaryParams,
            Semantics: new TypeDefinitionSemantics(
                TypeDefinitionMutability.Immutable,
                TypeDefinitionEqualitySemantics.Value
            )
        ));
    }

    /// <summary>Appends the SeedAsync local function (Issue 11b).</summary>
    private void AppendSeedMethodStatements(List<Node> statements, string dbContextName) {
        var seedableEntities = _entities.Where(e => GetStorageEntity(e).IsRoot).ToList();
        var bodyNodes = new List<Node>();

        // if (db is null) return;
        bodyNodes.Add(new IfStatement(
            new Equal(new Variable("db"), new Constant(null!)),
            new Block(new Return(null))));

        if (seedableEntities.Count > 0) {
            var firstSet = Pluralize(seedableEntities[0].Name);

            // if (await db.{FirstSet}.AnyAsync()) return;
            bodyNodes.Add(new IfStatement(
                new Await(
                    new Invoke(
                        new Member(
                            new Member(new Variable("db"), firstSet),
                            "AnyAsync"))),
                new Block(new Return(null))));

            foreach (var entity in seedableEntities) {
                var scalarProps = entity.Properties
                    .Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint))
                    .Where(p => !_entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal)))
                    .OrderBy(p => p.Name)
                    .ToList();

                var createArgs = new List<Node>();
                foreach (var prop in scalarProps)
                    createArgs.Add(MakeSampleValue(prop));

                foreach (var rel in _domain.Relationships) {
                    if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
                    if (rel.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)) continue;
                    createArgs.Add(new Invoke(new Member(new TypeReference("Enumerable"), "Empty")) {
                        TypeArguments = [new TypeReference(rel.Target.TypeName)]
                    });
                }

                var dtoVar = ToCamelCase(entity.Name);
                bodyNodes.Add(new Variable($"{dtoVar}Result",
                    new Invoke(new Member(new TypeReference(entity.Name), "Create"), [.. createArgs])));
                bodyNodes.Add(new IfStatement(
                    new Member(new Variable($"{dtoVar}Result"), "IsSuccess"),
                    new Block(
                        new Invoke(new Member(new Variable("db"), "Add"),
                            new Member(new Variable($"{dtoVar}Result"), "Value")))));
            }

            bodyNodes.Add(new Await(
                new Invoke(new Member(new Variable("db"), "SaveChangesAsync"))));
        }

        statements.Add(new MethodDefinitionNode(
            "SeedAsync",
            new TypeReference("Task"),
            Parameters: [new Parameter("db", new TypeReference(dbContextName))],
            Body: new Block(bodyNodes, []),
            IsStatic: true,
            IsAsync: true
        ));
    }

    /// <summary>Creates a Syntax node representing a sample value for a property.</summary>
    private Node MakeSampleValue(Property prop) {
        var typeName = prop.Type.TypeName;
        var propNameLower = prop.Name.ToLowerInvariant();

        // Email pattern
        if (propNameLower is "email" or "emailaddress")
            return new Constant("user@test.com");

        // Range-constrained properties
        var range = prop.Constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (range is not null) {
            if (range.Minimum is long l && l > 0) return new Constant((int)l);
            if (range.Maximum is long l2 && l2 > 0) return new Constant((int)(l2 / 2));
            return new Constant(1);
        }

        // Text/String with min length
        if (typeName is "Text" or "String") {
            var length = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault();
            if (length is not null && length.MinLength > 0)
                return new Constant(new string('X', Math.Max((int)length.MinLength, 8)));
            return new Constant("Sample");
        }

        return typeName switch {
            "Number" or "Int" or "Int64" => new Constant(1),
            "Int32" => new Constant(1),
            "Boolean" or "Bool" => new Constant(false),
            "DateTime" or "Timestamp" => new Member(new TypeReference("DateTime"), "UtcNow"),
            "Date" or "DateOnly" => new Invoke(
                new Member(new TypeReference("DateOnly"), "FromDateTime"),
                new Member(new TypeReference("DateTime"), "UtcNow")),
            "Decimal" => new Constant(1m),
            "Float" or "Double" => new Constant(1.0),
            "Guid" or "Uuid" => new Invoke(new Member(new TypeReference("Guid"), "NewGuid")),
            _ when _domain.Types.OfType<EnumType>().Any(e => string.Equals(e.Name, typeName, StringComparison.Ordinal))
                => new Member(new TypeReference(typeName), _domain.Types.OfType<EnumType>()
                    .First(e => string.Equals(e.Name, typeName, StringComparison.Ordinal)).MemberNames[0]),
            _ => new Constant("Sample"),
        };
    }
}