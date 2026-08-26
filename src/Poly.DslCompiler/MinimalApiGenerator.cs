using Poly.Analysis;
using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.Interpretation.CSharp;

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
    private readonly Dictionary<string, EnumType> _enumLookup;
    private readonly Dictionary<string, EntityStructureMetadata> _entityStructureLookup;

    private readonly IStorageSyntaxEmitter? _emitter;
    private readonly DbmsPack _dbms;

    public MinimalApiGenerator(Domain domain,
        AnalysisResult analysis,
        StorageModel storageModel,
        BehaviorModel behaviorModel,
        AggregateModel aggregateModel,
        IStorageSyntaxEmitter? emitter = null,
        DbmsPack dbms = DbmsPack.Generic) {
        ArgumentNullException.ThrowIfNull(analysis);
        _emitter = emitter;
        _dbms = dbms;
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _domainName = domain.Name;
        _storageLookup = storageModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _behaviorLookup = behaviorModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _aggregateLookup = aggregateModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _enumLookup = domain.Types.OfType<EnumType>().ToDictionary(e => e.Name, StringComparer.Ordinal);
        _entityStructureLookup = _entities
            .Select(entity => new { entity.Name, Metadata = analysis.GetStructure(entity) })
            .Where(x => x.Metadata is not null)
            .ToDictionary(x => x.Name, x => x.Metadata!, StringComparer.Ordinal);
    }

    private IReadOnlyList<BehaviorAction> GetBehaviorActions(Entity entity) =>
        _behaviorLookup.TryGetValue(entity.Name, out var beh) ? beh.Actions : [];

    /// <summary>
    /// Builds the <c>options =&gt; …</c> body for the EF Core DbContext registration,
    /// driven by the DBMS pack: sqlite → <c>UseSqlite("Data Source=library.db")</c>
    /// (matches the shipped demo), sqlserver → <c>UseSqlServer(...)</c>, generic →
    /// <c>UseInMemoryDatabase(domain)</c> (no provider package required).
    /// </summary>
    private Node BuildProviderRegistration(Parameter optionsParam) {
        var dbName = _domainName.ToLowerInvariant();
        switch (_dbms) {
            case DbmsPack.Sqlite:
                return new Invoke(new Member(optionsParam, "UseSqlite"),
                    new Constant($"Data Source={dbName}.db"));
            case DbmsPack.SqlServer:
                return new Invoke(new Member(optionsParam, "UseSqlServer"),
                    new Constant($"Server=(localdb)\\mssqllocaldb;Database={_domainName};Trusted_Connection=True"));
            default:
                return new Invoke(new Member(optionsParam, "UseInMemoryDatabase"),
                    new Constant(_domainName));
        }
    }

    private StorageEntity GetStorageEntity(Entity entity) => _storageLookup[entity.Name];

    private AggregateEntity GetAggregateEntity(Entity entity) => _aggregateLookup[entity.Name];

    private IReadOnlyList<ConstructorParameterOrder> GetConstructorOrder(Entity entity) {
        if (!_entityStructureLookup.TryGetValue(entity.Name, out var metadata)) {
            throw new InvalidOperationException(
                $"EntityStructureMetadata is required for constructor ordering on entity '{entity.Name}'.");
        }

        return metadata.ConstructorParameters;
    }

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

    private static Assignment Init(string name, Node value) =>
        new(new Variable(name), value);

    private static Block Stmts(IEnumerable<Node> nodes) {
        var list = nodes as IList<Node> ?? nodes.ToList();
        var locals = new List<Node>();
        var seen = new HashSet<Variable>(ReferenceEqualityComparer.Instance);
        foreach (var n in list) {
            if (n is Assignment { Destination: Variable v } && seen.Add(v))
                locals.Add(v);
        }
        return new Block(list, locals);
    }

    private static string Pluralize(string name) => name + "s";
    private static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);
    private static string Pascalize(string name) => DomainTypeMapping.ToPascalCase(name);

    /// <summary>
    /// When parent and child share a key token (both shadow <c>id</c>), the child
    /// route/param becomes <c>{entity}{Key}</c> so ASP.NET does not see two <c>{id}</c>.
    /// </summary>
    private static string DistinctChildKeyParam(string parentKey, string childKey, string childEntityName) {
        if (!string.Equals(parentKey, childKey, StringComparison.OrdinalIgnoreCase))
            return childKey;
        return ToCamelCase(childEntityName) + Pascalize(childKey);
    }

    private static bool IsCollectionNavigation(StorageEntity parentStore, string pascalRel) =>
        parentStore.CollectionNavigations.Any(n =>
            string.Equals(n.PropertyName, pascalRel, StringComparison.Ordinal));
    private static string GetClrTypeName(string domainType) => DomainTypeMapping.ToClrTypeName(domainType);

    private static bool IsNumericClrType(string clrType) => clrType switch {
        "long" or "int" or "short" or "byte" or "double" or "float" or "decimal" => true,
        _ => false,
    };

    /// <summary>True when the CLR type is a non-nullable value type (a primitive scalar or
    /// enum) — the only CLR kinds that do not need a <c>default!</c> initializer on an
    /// init-only DTO property. Value types (record classes) and <c>byte[]</c> are reference
    /// types and must be initialized to silence CS8618.</summary>
    private bool IsPrimitiveValueTypeClr(string domainType, string clrType) =>
        clrType switch {
            "long" or "int" or "short" or "byte" or "double" or "float" or "decimal" or "bool"
                or "DateTime" or "DateOnly" or "TimeOnly" or "TimeSpan" or "Guid" => true,
            _ => _enumLookup.ContainsKey(domainType),
        };

    /// <summary>CLR-representable numeric bounds for a DTO member type — used to cap open
    /// range constraints so <c>[Range(min, max)]</c> never emits a raw double.MaxValue.</summary>
    private static (double Min, double Max) ClrNumericBounds(string clrType) => clrType switch {
        "byte" => (byte.MinValue, byte.MaxValue),
        "short" => (short.MinValue, short.MaxValue),
        "int" => (int.MinValue, int.MaxValue),
        "long" => (long.MinValue, long.MaxValue),
        "float" => (float.MinValue, float.MaxValue),
        "decimal" => (double.MinValue, double.MaxValue),
        _ => (double.MinValue, double.MaxValue),
    };

    /// <summary>Generates the Minimal API Program.cs as a Syntax IR compilation unit.</summary>
    public CompilationUnitNode GenerateCompilationUnit(string dbContextName) {
        var dtoTypes = new List<TypeDefinitionNode>();
        var topLevelStatements = new List<Node>();

        // ── Builder setup (Issue 9: CreateBuilder(args) not args.Clone) ──
        topLevelStatements.Add(Init(BuilderVar,
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

        // ── EF Core provider (pack-driven; InMemory fallback for generic) ──
        var dbOptionsParam = new Parameter("options");
        topLevelStatements.Add(new Invoke(
            new Member(
                new Member(new TypeReference(BuilderVar), "Services"),
                "AddDbContext"),
            new Lambda([dbOptionsParam],
                BuildProviderRegistration(dbOptionsParam))) {
            TypeArguments = [new TypeReference(dbContextName)]
        });

        // ── Build + seed ──
        topLevelStatements.Add(Init(AppVar,
            new Invoke(new Member(new TypeReference(BuilderVar), "Build"))));

        var dbVar = new Variable("db");
        var scopeBody = new List<Node> {
            new Assignment(dbVar,
                new Invoke(
                    new Member(
                        new Member(new Variable("scope"), "ServiceProvider"),
                        "GetRequiredService")) {
                    TypeArguments = [new TypeReference(dbContextName)]
                })
        };
        if (_dbms == DbmsPack.Sqlite) {
            scopeBody.Add(new Await(
                new Invoke(
                    new Member(
                        new Member(new Variable("db"), "Database"),
                        "EnsureCreatedAsync"))));
        }
        scopeBody.Add(new Await(new Invoke(new Variable("SeedAsync"), new Variable("db"))));
        topLevelStatements.Add(new UsingStatement(
            Init("scope",
                new Invoke(new Member(
                    new Member(new TypeReference(AppVar), "Services"),
                    "CreateScope"))),
            new Block(scopeBody, [dbVar])));

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
            Usings: ["System.Text.Json", "System.Text.Json.Serialization", "Microsoft.EntityFrameworkCore", "System.ComponentModel.DataAnnotations", "Poly.Generated"],
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
            AppendCreateCallStatements(statements, entity, dtoParam, dbParam, route, keyProp);
        }
    }

    /// <summary>Appends Syntax IR for POST /api/entities endpoint — calls Entity.Create with result handling.</summary>
    private void AppendCreateCallStatements(List<Node> statements, Entity entity,
        Parameter dtoParam, Parameter dbParam,
        string route, Property? uniqueProp) {
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

        var createArgs = new List<Node>();
        // Scalar constructor params (ESM order) come from the DTO; collection navs
        // are ctor params (IEnumerable<T>) that start empty. ESM now carries the
        // COMPLETE signature — no re-scan of domain.Relationships here.
        foreach (var parameter in GetConstructorOrder(entity)) {
            if (parameter.IsCollection) {
                createArgs.Add(new Invoke(new Member(new TypeReference("Enumerable"), "Empty")) {
                    TypeArguments = [new TypeReference(parameter.Type.TypeName)]
                });
                continue;
            }
            if (parameter.IsNavigation) {
                if (parameter.IsBackReference) continue; // auto-wired, not from DTO
                createArgs.Add(new Invoke(new Member(new TypeReference("Enumerable"), "Empty")) {
                    TypeArguments = [new TypeReference(parameter.Type.TypeName)]
                });
                continue;
            }

            createArgs.Add(new Member(new Variable("dto"), parameter.Name));
        }

        var resultVarName = $"{ToCamelCase(entity.Name)}Result";
        var bodyNodes = new List<Node>();

        // var result = Entity.Create(...)
        bodyNodes.Add(Init(resultVarName,
            new Invoke(new Member(new TypeReference(entity.Name), "Create"), [.. createArgs])));

        // if (!result.IsSuccess) return Results.Conflict(new { error = result.ErrorMessage });
        bodyNodes.Add(new IfStatement(
            new Ast.Nodes.Not(new Member(new Variable(resultVarName), "IsSuccess")),
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
                Stmts(bodyNodes))));
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
        var childKeyParam = DistinctChildKeyParam(parentKey, childKey, entity.Name);
        var pascalRel = Pascalize(relNameRaw);
        var isCollection = IsCollectionNavigation(parentStore, pascalRel);
        var pluralParent = Pluralize(parentEntity.Name);
        var listRoute = $"/api/{Pluralize(parentEntity.Name).ToLowerInvariant()}/{{{parentKey}}}/{relName.ToLowerInvariant()}";
        var detailRoute = $"{listRoute}/{{{childKeyParam}}}";
        var parentKeyP = new Parameter(parentKey, new TypeReference(parentKeyType));
        var childKeyP = new Parameter(childKeyParam, new TypeReference(childKeyType));
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
                        new Await(new Invoke(
                            new Member(new Member(new TypeReference("db"), pluralParent), "FindAsync"),
                            new Variable(parentKey)))),
                    new IfStatement(new Equal(new Variable("parent"), new Constant(null!)),
                        new Block(new Return(
                            new Invoke(new Member(new TypeReference("Results"), "NotFound"))))),
                    new Await(new Invoke(new Member(collCall, "LoadAsync"))),
                    new Return(new Invoke(new Member(new TypeReference("Results"), "Ok"),
                        new Member(new Variable("parent"), pascalRel)))
                ]))));

        if (!isCollection)
            return;

        // Detail: back-ref filtering matching string oracle
        var detailBody = new List<Node>();
        var backRefName = backRefRaw != null ? Pascalize(backRefRaw) : null;
        var parentKeyPropName = parentStore.KeyProperty?.Name;
        var childKeyPropName = childStore.KeyProperty?.Name;

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
        }
        else {
            detailBody.Add(Init("parent",
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
            detailBody.Add(Init("child",
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
                Stmts(detailBody))));
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
                var childKeyParam = DistinctChildKeyParam(pKeyName, keyName, entity.Name);
                var isCollection = IsCollectionNavigation(pStore, pascalRel);
                var parentRoute = $"/api/{Pluralize(pEntity.Name).ToLowerInvariant()}/{{{pKeyName}}}/{ToCamelCase(relNameRaw).ToLowerInvariant()}";
                actionRoute = isCollection
                    ? $"{parentRoute}/{{{childKeyParam}}}/{ToCamelCase(ia.Name).ToLowerInvariant()}"
                    : $"{parentRoute}/{ToCamelCase(ia.Name).ToLowerInvariant()}";
                actionParams.Add(new Parameter(pKeyName, new TypeReference(pKeyType)));
                if (isCollection)
                    actionParams.Add(new Parameter(childKeyParam, new TypeReference(keyType)));
                if (ia.Parameters.Count > 0)
                    actionParams.Add(new Parameter("dto", new TypeReference($"{Pascalize(ia.Name)}Dto")));
                actionParams.Add(new Parameter("db", new TypeReference(dbContextName)));

                // Parent + entity lookup + membership check
                preActionNodes.Add(Init("parentEntity",
                    new Await(new Invoke(new Member(new Member(new TypeReference("db"), Pluralize(pEntity.Name)), "FindAsync"), new Variable(pKeyName)))));
                preActionNodes.Add(new IfStatement(new Equal(new Variable("parentEntity"), new Constant(null!)),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{pEntity.Name} not found"))))));
                if (isCollection) {
                    preActionNodes.Add(Init("entity",
                        new Await(new Invoke(new Member(new Member(new TypeReference("db"), pluralName), "FindAsync"), new Variable(childKeyParam)))));
                    preActionNodes.Add(new IfStatement(new Equal(new Variable("entity"), new Constant(null!)),
                        new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{entity.Name} not found"))))));
                    var parEntry = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parentEntity"));
                    var parColl = new Invoke(new Member(parEntry, "Collection"), new Lambda([new Parameter("e")], new Member(new Variable("e"), pascalRel)));
                    preActionNodes.Add(new Await(new Invoke(new Member(parColl, "LoadAsync"))));
                    preActionNodes.Add(new IfStatement(
                        new Ast.Nodes.Not(
                            new Invoke(
                                new Member(new Member(new Variable("parentEntity"), pascalRel), "Any"),
                                new Lambda([new Parameter("e")], new Equal(new Variable("e"), new Variable("entity"))))),
                        new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{entity.Name} not found for this {pEntity.Name}"))))));
                }
                else {
                    var parEntry = new Invoke(new Member(new TypeReference("db"), "Entry"), new Variable("parentEntity"));
                    var parRef = new Invoke(new Member(parEntry, "Reference"), new Lambda([new Parameter("e")], new Member(new Variable("e"), pascalRel)));
                    preActionNodes.Add(new Await(new Invoke(new Member(parRef, "LoadAsync"))));
                    preActionNodes.Add(Init("entity", new Member(new Variable("parentEntity"), pascalRel)));
                    preActionNodes.Add(new IfStatement(new Equal(new Variable("entity"), new Constant(null!)),
                        new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{entity.Name} not found"))))));
                }
            }
            else {
                var baseRoute = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
                actionRoute = $"{baseRoute}/{{{keyName}}}/{ToCamelCase(ia.Name).ToLowerInvariant()}";
                actionParams.Add(new Parameter(keyName, new TypeReference(keyType)));
                if (ia.Parameters.Count > 0)
                    actionParams.Add(new Parameter("dto", new TypeReference($"{Pascalize(ia.Name)}Dto")));
                actionParams.Add(new Parameter("db", new TypeReference(dbContextName)));

                preActionNodes.Add(Init("entity",
                    new Await(new Invoke(new Member(new Member(new TypeReference("db"), pluralName), "FindAsync"), new Variable(keyName)))));
                preActionNodes.Add(new IfStatement(new Equal(new Variable("entity"), new Constant(null!)),
                    new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{entity.Name} not found"))))));
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
                    tryBody.Add(Init(lv, new Await(new Invoke(
                        new Member(new Member(new TypeReference("db"), Pluralize(param.DomainType)), "FindAsync"),
                        new Member(new Variable("dto"), $"{param.Name}Id")))));
                    tryBody.Add(new IfStatement(new Equal(new Variable(lv), new Constant(null!)),
                        new Block(new Return(new Invoke(new Member(new TypeReference("Results"), "NotFound"), new Constant($"{param.DomainType} not found"))))));
                    invokeArgs.Add(new Variable(lv));
                }
                else invokeArgs.Add(new Member(new Variable("dto"), param.Name));
            }
            tryBody.Add(Init("result",
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

            // Catch: returns Results.StatusCode(500). No variable — the exception is
            // swallowed (avoids CS0168 unused-variable warnings).
            var catchBody = new Return(
                new Invoke(new Member(new TypeReference("Results"), "StatusCode"), new Constant(500)));

            var bodyNodes = new List<Node>(preActionNodes);
            bodyNodes.Add(new TryCatchFinally(
                Stmts(tryBody),
                CatchClauses: [new CatchClause(new NamedTypeReference("Exception"), null, catchBody)]));

            statements.Add(new Invoke(
                new Member(new TypeReference(AppVar), "MapPost"),
                new Constant(actionRoute),
                new Lambda([.. actionParams],
                    Stmts(bodyNodes))));
        }
    }

    /// <summary>Builds Syntax IR for action DTO types.</summary>
    private void BuildActionDtoTypes(List<TypeDefinitionNode> dtoTypes, Entity entity) {
        foreach (var ia in GetBehaviorActions(entity)) {
            if (ia.Parameters.Count == 0) continue;
            var domainAction = entity.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, ia.Name, StringComparison.Ordinal));
            var props = new List<PropertyDefinitionNode>();
            foreach (var param in ia.Parameters) {
                if (param.IsEntityRef) {
                    props.Add(new PropertyDefinitionNode(
                        $"{param.Name}Id",
                        new TypeReference("string"),
                        Getter: new PropertyGetterDefinitionNode(),
                        Initializer: new PropertyInitializerDefinitionNode(new NullForgiving(new Default()))));
                    continue;
                }
                var clrType = GetClrTypeName(param.DomainType);
                var prop = new PropertyDefinitionNode(
                    param.Name,
                    new TypeReference(clrType),
                    Getter: new PropertyGetterDefinitionNode(),
                    Initializer: IsPrimitiveValueTypeClr(param.DomainType, clrType)
                        ? new PropertyInitializerDefinitionNode()
                        : new PropertyInitializerDefinitionNode(new NullForgiving(new Default())));
                if (domainAction is not null) {
                    var attrs = BuildConstraintAttributes(
                        clrType, GetActionParamImplicitConstraints(entity, domainAction, param.Name));
                    if (IsEnumTypeName(param.DomainType))
                        attrs = [EnumDataTypeAttribute(param.DomainType), .. attrs];
                    if (attrs.Count > 0) prop = prop with { Attributes = attrs };
                }
                props.Add(prop);
            }
            dtoTypes.Add(new TypeDefinitionNode(
                $"{Pascalize(ia.Name)}Dto",
                Properties: props,
                Semantics: new TypeDefinitionSemantics(
                    TypeDefinitionMutability.Immutable,
                    TypeDefinitionEqualitySemantics.Value
                )
            ));
        }
    }

    /// <summary>
    /// Resolves the effective range for a property: the analysis-verified envelope when
    /// the invariant analysis proved no effect can produce an out-of-range value, else
    /// the declared constraint.
    /// </summary>
    private RangeConstraint? GetPropertyRange(Entity entity, string propertyName) {
        var column = GetStorageEntity(entity).Columns.FirstOrDefault(c =>
            string.Equals(c.Name, propertyName, StringComparison.Ordinal));
        if (column?.VerifiedRange is { } verified && column.IsRangeVerified)
            return new RangeConstraint(verified.Min, verified.Max);
        return entity.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal))
            ?.Constraints.OfType<RangeConstraint>().FirstOrDefault();
    }

    /// <summary>
    /// Derives the implicit constraints an action parameter must satisfy: for each
    /// property the parameter flows into — <c>assign Prop to param</c> on this entity, or a
    /// <c>create</c>/<c>create in</c> initializer <c>Prop: param</c> on a related entity —
    /// its effective constraints are merged by intersection (<see cref="ConstraintMerge"/>).
    /// Not declared in the DSL — proven by the action's own effects, so the action DTO
    /// enforces the same envelope at the API boundary. Conflicting constraints (e.g. two
    /// targets with different patterns) merge to nothing and are dropped.
    /// Only UNCONDITIONAL flows contribute: a parameter that reaches a target only inside
    /// an <c>if</c> branch has no universally-provable envelope, and intersecting the
    /// branches' ranges would falsely reject valid inputs.
    /// </summary>
    private IReadOnlyList<Constraint> GetActionParamImplicitConstraints(Entity entity, Poly.DomainModeling.Ontology.Action action, string paramName) {
        var merged = new List<Constraint>();
        var unconditional = FlattenUnconditionalEffects(action.Effects).ToList();

        foreach (var assign in unconditional.OfType<AssignEffect>()) {
            // In the domain model a parameter reference in an effect value is still a
            // PropertyAccess/ParameterAccess whose name equals the action parameter
            // (the param→bare-identifier rewrite happens later, at C# lowering).
            var valueName = ValueNameOf(assign.Value);
            if (valueName is null ||
                !string.Equals(valueName, paramName, StringComparison.Ordinal))
                continue;
            if (assign.Target is not PropertyAccess target) continue;
            MergeTargetConstraints(merged, entity, target.Name);
        }

        foreach (var create in unconditional.OfType<Effect>()
            .Where(e => e is CreateEntityInstance or CreateEntityInRelationshipEffect)) {
            var (targetEntityName, initializers) = create switch {
                CreateEntityInstance cei => (cei.Type.TypeName, cei.Initializers),
                CreateEntityInRelationshipEffect cer => (
                    cer.ResolvedTargetType?.TypeName ?? ResolveRelationshipTarget(entity, cer.RelationshipName),
                    cer.Initializers),
                _ => (null, null!),
            };
            if (targetEntityName is null) continue;
            var targetEntity = _entities.FirstOrDefault(e =>
                string.Equals(e.Name, targetEntityName, StringComparison.Ordinal));
            if (targetEntity is null) continue;
            foreach (var binding in initializers) {
                var valueName = ValueNameOf(binding.Expression);
                if (valueName is null ||
                    !string.Equals(valueName, paramName, StringComparison.Ordinal))
                    continue;
                MergeTargetConstraints(merged, targetEntity, binding.PropertyName);
            }
        }

        return merged;
    }

    private static string? ValueNameOf(DomainExpression expression) => expression switch {
        PropertyAccess pa => pa.Name,
        ParameterAccess pa => pa.Name,
        _ => null,
    };

    private void MergeTargetConstraints(List<Constraint> merged, Entity owner, string propertyName) {
        foreach (var constraint in EffectivePropertyConstraints(owner, propertyName)) {
            var existingIndex = merged.FindIndex(m => m.GetType() == constraint.GetType());
            if (existingIndex < 0) {
                merged.Add(constraint);
                continue;
            }
            var net = merged[existingIndex].Merge(constraint);
            if (net is null) merged.RemoveAt(existingIndex);
            else merged[existingIndex] = net;
        }
    }

    /// <summary>The effective constraints for a property: its analysis-verified-or-declared
    /// range plus the declared length/pattern/required/equality constraints.</summary>
    private IReadOnlyList<Constraint> EffectivePropertyConstraints(Entity owner, string propertyName) {
        var result = new List<Constraint>();
        var range = GetPropertyRange(owner, propertyName);
        if (range is not null) result.Add(range);
        var prop = owner.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal));
        if (prop is not null)
            result.AddRange(prop.Constraints.Where(c =>
                c is LengthConstraint or PatternConstraint or RequiredConstraint
                    or EqualityConstraint));
        return result;
    }

    private string? ResolveRelationshipTarget(Entity source, string relationshipName) {
        var rel = source.Navigations.FirstOrDefault(n =>
            string.Equals(n.Name, relationshipName, StringComparison.Ordinal));
        return rel?.Target.TypeName;
    }

    /// <summary>Flattens a composite (sequential) effect tree WITHOUT descending into
    /// conditional branches — only flows that execute on every path.</summary>
    private static IEnumerable<Effect> FlattenUnconditionalEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            if (effect is CompositeEffect composite) {
                foreach (var nested in FlattenUnconditionalEffects(composite.Effects)) yield return nested;
            }
        }
    }

    /// <summary>Maps effective constraints to DataAnnotations validation attributes, when
    /// the CLR type can express them.</summary>
    private static IReadOnlyList<AttributeNode> BuildConstraintAttributes(string clrType, IEnumerable<Constraint> constraints) {
        var attrs = new List<AttributeNode>();
        foreach (var constraint in constraints) {
            switch (constraint) {
                case RangeConstraint r when IsNumericClrType(clrType):
                    // Open bounds (range(0, ) / range(, 100)) cap at the CLR type's
                    // representable range — the DTO member can never exceed it — instead
                    // of emitting a raw double.MaxValue literal.
                    var (minBound, maxBound) = ClrNumericBounds(clrType);
                    attrs.Add(new AttributeNode("Range", new List<Expression> {
                        new Constant(r.Minimum is not null ? Convert.ToDouble(r.Minimum) : minBound),
                        new Constant(r.Maximum is not null ? Convert.ToDouble(r.Maximum) : maxBound)
                    }));
                    break;
                case LengthConstraint l when clrType == "string":
                    attrs.Add(new AttributeNode("MinLength", [new Constant(l.MinLength)]));
                    attrs.Add(new AttributeNode("MaxLength", [new Constant(l.MaxLength)]));
                    break;
                case PatternConstraint p when clrType == "string":
                    attrs.Add(new AttributeNode("RegularExpression", [new Constant(p.Pattern)]));
                    break;
                case RequiredConstraint when clrType == "string":
                    attrs.Add(new AttributeNode("Required", []));
                    break;
                // Value-set union: equals(v) → [AllowedValues(...)] — the member must equal
                // the pinned value. (Enum unions are enforced by the CLR enum type; see the
                // enum [EnumDataType] propagation.)
                case EqualityConstraint eq:
                    attrs.Add(new AttributeNode("AllowedValues", [new Constant(eq.ExpectedValue)]));
                    break;
            }
        }
        return attrs;
    }

    /// <summary>The <c>[EnumDataType(typeof(EnumName))]</c> attribute declaring an enum-typed
    /// member's allowed-value union on the transport contract.</summary>
    private static AttributeNode EnumDataTypeAttribute(string enumName) =>
        new("EnumDataType", [new TypeOf(new TypeReference(enumName))]);

    private bool IsEnumTypeName(string domainTypeName) =>
        _enumLookup.ContainsKey(domainTypeName);

    /// <summary>Builds Syntax IR for a DTO type definition.</summary>
    private void BuildDtoTypes(List<TypeDefinitionNode> dtoTypes, Entity entity) {
        // The DTO mirrors the entity's CREATE signature (the POST endpoint passes
        // the DTO members straight into Entity.Create(...)). Use the complete
        // constructor metadata — the SAME source the endpoint reads — rather than
        // re-deriving "scalar non-default props" here (drift = CS7036-class bug).
        var scalarParams = GetConstructorOrder(entity)
            .Where(p => !p.IsNavigation)
            .ToList();

        if (scalarParams.Count == 0) return;
        if (!GetStorageEntity(entity).IsRoot) return;

        // Emit as a record with get/init properties so transport validation attributes
        // attach to properties and are enforced by ASP.NET model binding, and the
        // contract stays immutable. An init-only accessor is a property with no setter
        // but an initializer present; non-nullable reference scalars get `= default!;`
        // for CS8618 hygiene. Declared constraints map to validation attributes: range →
        // [Range] (using the analysis-VERIFIED envelope when proven, else declared),
        // length → [MinLength]/[MaxLength], pattern → [RegularExpression], required → [Required].
        var props = scalarParams.Select(param => {
            var clrType = GetClrTypeName(param.Type.TypeName);
            var prop = new PropertyDefinitionNode(
                param.Name,
                new TypeReference(clrType),
                Getter: new PropertyGetterDefinitionNode(),
                Initializer: IsPrimitiveValueTypeClr(param.Type.TypeName, clrType)
                    ? new PropertyInitializerDefinitionNode()
                    : new PropertyInitializerDefinitionNode(new NullForgiving(new Default())));

            var effective = new List<Constraint>();
            var range = GetPropertyRange(entity, param.Name);
            if (range is not null) effective.Add(range);
            var declared = entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, param.Name, StringComparison.Ordinal))?.Constraints ?? [];
            effective.AddRange(declared.Where(c =>
                c is LengthConstraint or PatternConstraint or RequiredConstraint
                    or EqualityConstraint));

            var attrs = BuildConstraintAttributes(clrType, effective);
            // Enum-typed members: the CLR enum type enforces membership at binding; the
            // [EnumDataType] attribute additionally declares the allowed-value union on the
            // contract and fails loud at validation if binding ever bypasses the type.
            if (IsEnumTypeName(param.Type.TypeName))
                attrs = [EnumDataTypeAttribute(param.Type.TypeName), .. attrs];
            return attrs.Count == 0 ? prop : prop with { Attributes = attrs };
        }).ToList();

        dtoTypes.Add(new TypeDefinitionNode(
            $"{entity.Name}Dto",
            Properties: props,
            Semantics: new TypeDefinitionSemantics(
                TypeDefinitionMutability.Mutable,
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
                var createArgs = new List<Node>();

                // Complete constructor signature from ESM (now includes collection
                // navs) — scalar props get sample values, navs start empty.
                foreach (var parameter in GetConstructorOrder(entity)) {
                    if (parameter.IsNavigation) {
                        if (parameter.IsBackReference) continue; // auto-wired
                        createArgs.Add(new Invoke(
                            new Member(new TypeReference("Enumerable"), "Empty")) {
                            TypeArguments = [new TypeReference(parameter.Type.TypeName)]
                        });
                        continue;
                    }

                    var prop = entity.Properties.FirstOrDefault(p =>
                        string.Equals(p.Name, parameter.Name, StringComparison.Ordinal));
                    if (prop is null) {
                        throw new InvalidOperationException(
                            $"Constructor parameter '{parameter.Name}' on entity '{entity.Name}' does not match a property.");
                    }
                    createArgs.Add(MakeSampleValue(prop));
                }

                var dtoVar = ToCamelCase(entity.Name);
                bodyNodes.Add(Init($"{dtoVar}Result",
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
            Body: Stmts(bodyNodes),
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
            _ when _enumLookup.TryGetValue(typeName, out var enumType) && enumType.MemberNames.Count > 0
                => new Member(new TypeReference(typeName), enumType.MemberNames[0]),
            _ => new Constant("Sample"),
        };
    }
}

/// <summary>
/// Emits the composition-root host files (<c>Program.cs</c> + <c>demo.http</c>) from the
/// analyzed domain via the artifact-contributor hook. Surfaces only the root domain's own
/// entities — produced internal contracts contribute value types and operation endpoints,
/// never child-entity routes.
/// </summary>
public sealed class MinimalApiHostArtifactContributor : IArtifactContributor {
    private readonly IStorageSyntaxEmitter? _emitter;
    private readonly DbmsPack _dbms;

    public MinimalApiHostArtifactContributor(
        IStorageSyntaxEmitter? emitter = null,
        DbmsPack dbms = DbmsPack.Generic) {
        _emitter = emitter;
        _dbms = dbms;
    }

    public IReadOnlyList<(string FileName, string Source)> Contribute(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage
            ?? throw new InvalidOperationException(
                "Minimal API artifacts require StorageMappingMetadata.");
        var behavior = BehaviorMetadata.From(domain, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate
            ?? throw new InvalidOperationException(
                "Minimal API artifacts require OwnershipAggregateMetadata.");
        var dbContextName = $"{domain.Name}DbContext";
        var apiGen = new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate, _emitter, _dbms);
        var httpGen = new HttpFileGenerator(domain, analysis, storage, behavior, aggregate);
        return [
            ("Program.cs", new CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(dbContextName))),
            ("demo.http", httpGen.Generate()),
        ];
    }
}