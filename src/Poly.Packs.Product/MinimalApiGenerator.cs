using Poly.Analysis;
using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Runtime;

using ClrPrimitive = Poly.Introspection.PrimitiveType;

namespace Poly.Packs.Product;

/// <summary>
/// Generates an ASP.NET Minimal API Program.cs from a <see cref="Domain"/>.
///
/// Consumes AggregateModel / StorageModel / BehaviorModel from infrastructure
/// analysis for root detection, parent routing, keys, and action shapes.
/// </summary>
public sealed partial class MinimalApiGenerator {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _domainName;
    private readonly Dictionary<string, StorageEntity> _storageLookup;
    private readonly Dictionary<string, BehaviorEntity> _behaviorLookup;
    private readonly Dictionary<string, AggregateEntity> _aggregateLookup;
    private readonly Dictionary<string, EnumType> _enumLookup;
    private readonly Dictionary<string, EntityStructureMetadata> _entityStructureLookup;
    private readonly CatalogIndex? _catalog;
    private readonly AnalysisResult _analysis;

    private readonly IStorageSyntaxEmitter? _emitter;
    private readonly string? _vendor;

    public MinimalApiGenerator(Domain domain,
        AnalysisResult analysis,
        StorageModel storageModel,
        BehaviorModel behaviorModel,
        AggregateModel aggregateModel,
        IStorageSyntaxEmitter? emitter = null,
        string? vendor = null,
        CatalogIndex? catalog = null) {
        ArgumentNullException.ThrowIfNull(analysis);
        _analysis = analysis;
        _emitter = emitter;
        _vendor = vendor;
        _catalog = catalog;
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
        _actionDtoCollisions = new Lazy<HashSet<string>>(ComputeActionDtoCollisions);
    }

    /// <summary>Action DTO type name for an endpoint contract: <c>{Action}Dto</c>, or
    /// <c>{Entity}{Action}Dto</c> when the plain name collides (see
    /// <see cref="_actionDtoCollisions"/>) so Program.cs stays a single compilation unit.</summary>
    private string GetActionDtoName(Entity entity, string actionName) {
        var plain = $"{Pascalize(actionName)}Dto";
        return _actionDtoCollisions.Value.Contains(plain) ? $"{entity.Name}{Pascalize(actionName)}Dto" : plain;
    }

    private IReadOnlyList<BehaviorAction> GetBehaviorActions(Entity entity) =>
        _behaviorLookup.TryGetValue(entity.Name, out var beh) ? beh.Actions : [];

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
    /// One argument per constructor parameter, in ESM order: a collection nav is empty, a
    /// singular nav (including an auto-wired back-reference) is <c>null</c>, and a scalar comes
    /// from <paramref name="scalar"/>. Emitting an argument for <em>every</em> parameter is what
    /// keeps this positional call aligned with the printed <c>Create</c> signature — skipping a
    /// back-reference (or filling a singular nav with an empty enumerable) shifts every later
    /// argument and the emitted host fails to compile.
    /// </summary>
    private List<Node> BuildCreateArgs(
        Entity entity,
        IReadOnlyList<ConstructorParameterOrder> order,
        Func<Property, Node> scalar) {
        var args = new List<Node>(order.Count);
        foreach (var parameter in order) {
            if (parameter.IsCollection) {
                args.Add(new Invoke(new Member(new TypeReference("Enumerable"), "Empty")) {
                    TypeArguments = [new TypeReference(VisibleType(parameter.Type.TypeName))]
                });
                continue;
            }
            if (parameter.IsNavigation) {
                args.Add(new Constant(null!));
                continue;
            }
            var prop = entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, parameter.Name, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Constructor parameter '{parameter.Name}' on entity '{entity.Name}' does not match a property.");
            args.Add(scalar(prop));
        }
        return args;
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
        new CSharpWriter().Write(GenerateCompilationUnit(dbContextName));

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

    private static string Pluralize(string name) => Pluralizer.Pluralize(name);
    private static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);
    private static string Pascalize(string name) => DomainTypeMapping.ToPascalCase(name);

    /// <summary>Action DTO member: PascalCase, like an entity DTO. An entity ref is the id string.</summary>
    private static string ActionDtoPropertyName(BehaviorParameter param) =>
        param.IsEntityRef ? Pascalize(param.Name) + "Id" : Pascalize(param.Name);

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
    /// <summary>Host top-level statements synthesize global <c>Program</c>, and the host's
    /// <c>using &lt;ns&gt;;</c> does not outrank a namespace named like a domain type, so such an
    /// entity is spelled <c>&lt;ns&gt;.&lt;name&gt;</c> (<see cref="DomainSession.QualifyForHost"/>).</summary>
    private string VisibleType(string typeName) => DomainSession.QualifyForHost(_domain, typeName);

    private string GetClrTypeName(string domainType) =>
        VisibleType(RuntimeAnalysisCache.ClrTypeName(_domain, domainType));

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

        // A route that carries the storage port names IStoragePort, not the
        // context. The generated context implements the port, so register it
        // as the scoped implementation.
        if (_catalog?.RequiresStoragePort == true) {
            topLevelStatements.Add(new Invoke(
                new Member(
                    new Member(new TypeReference(BuilderVar), "Services"),
                    "AddScoped")) {
                TypeArguments = [
                    new ClrTypeReference(typeof(IStoragePort)),
                    new TypeReference(dbContextName)
                ]
            });
        }

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
        if (string.Equals(_vendor, "sqlite", StringComparison.Ordinal)) {
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
        AppendProblemMethodStatements(topLevelStatements);
        AppendSeedMethodStatements(topLevelStatements, dbContextName);

        // ── DTOs ──
        _emittedDtoNames.Clear();
        foreach (var entity in _entities)
            BuildDtoTypes(dtoTypes, entity);
        // Action DTOs
        foreach (var entity in _entities)
            BuildActionDtoTypes(dtoTypes, entity);

        // ── app.Run() ──
        topLevelStatements.Add(new Invoke(new Member(new TypeReference(AppVar), "Run")));

        var unit = new CompilationUnitNode(
            Usings: ["System.Text.Json", "System.Text.Json.Serialization", "Microsoft.EntityFrameworkCore", "System.ComponentModel.DataAnnotations", DomainSession.CSharpNamespace(_domain)],
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

    /// <summary>
    /// Builds the positional arguments for a static <c>Create</c> call. A
    /// contributed dependency sits where the lowered signature puts it (before
    /// the first optional parameter), so it is woven into the constructor
    /// arguments rather than appended.
    /// </summary>
    private List<Node> BuildCreateCallArgs(
        Entity entity,
        IReadOnlyList<ConstructorParameterOrder> order,
        Func<Property, Node> scalar) {
        var args = BuildCreateArgs(entity, order, scalar);
        if (RuntimeAnalysisCache.Session(_domain).ConstraintCompilers.Count == 0)
            return args;
        var method = ModuleMethod(entity, "Create");
        if (method?.Parameters is not { Count: > 0 } parameters)
            return args;

        var declared = new Dictionary<string, Node>(StringComparer.Ordinal);
        for (var i = 0; i < order.Count && i < args.Count; i++)
            declared[ToCamelCase(order[i].Name)] = args[i];

        var result = new List<Node>(parameters.Count);
        foreach (var parameter in parameters) {
            if (declared.TryGetValue(parameter.Name, out var argument)) {
                result.Add(argument);
                continue;
            }
            if (IsTrailingOmittable(entity, parameter.Name))
                continue;
            if (!string.Equals(parameter.Name, "db", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Create on '{entity.Name}' requires contributed parameter '{parameter.Name}', "
                    + "which the host cannot supply.");
            result.Add(new Variable("db"));
        }
        return result;
    }

    private bool IsTrailingOmittable(Entity entity, string parameterName) {
        if (!_entityStructureLookup.TryGetValue(entity.Name, out var structure))
            return false;
        foreach (var property in entity.Properties) {
            if (!string.Equals(ToCamelCase(property.Name), parameterName, StringComparison.Ordinal))
                continue;
            return !structure.EntryAssignedPropertyNames.Contains(property.Name)
                && (!structure.RequiredPropertyNames.Contains(property.Name)
                    || property.Constraints.OfType<DefaultValueConstraint>().Any());
        }
        return false;
    }

    private MethodDefinitionNode? ModuleMethod(Entity entity, string methodName) {
        // The host contributor supplies the lowered catalog. Direct callers
        // without one get the un-contributed shape rather than a false failure.
        if (_catalog is null)
            return null;
        return _catalog.Method(entity.Name, methodName);
    }

    /// <summary>The failure's typed kind picks the status; the body is ProblemDetails.</summary>
    private static Return DomainFailureReturn(Node result) =>
        new(new Invoke(new Variable("Problem"), new NullForgiving(new Member(result, "Error"))));

    /// <summary>A ProblemDetails response with no domain failure behind it.</summary>
    private static Node ProblemResponse(int status, string title, string? detail = null) =>
        detail is null
            ? new Invoke(new Variable("StatusProblem"), new Constant(status), new Constant(title))
            : new Invoke(new Variable("StatusProblem"), new Constant(status), new Constant(title), new Constant(detail));

    /// <summary>
    /// A shadow key is the primary key, so <c>FindAsync</c> matches it. A natural
    /// key is a unique index; the primary key is the synthetic int, so the lookup
    /// compares that property.
    /// </summary>
    private static Node LoadByRouteKey(string setName, StorageEntity store, Node keyExpr) {
        var set = new Member(new TypeReference("db"), setName);
        if (store.KeyProperty is null)
            return new Await(new Invoke(new Member(set, "FindAsync"), keyExpr));
        var row = new Parameter("e");
        return new Await(new Invoke(
            new Member(set, "FirstOrDefaultAsync"),
            new Lambda([row], new Equal(new Member(row, store.KeyProperty.Name), keyExpr))));
    }

    /// <summary>CLR keyword names stay <see cref="PrimitiveTypeReference"/> so a
    /// domain type of the same name can print with <c>@</c> in type position.</summary>
    private static Node ClrTypeNode(string clrType) => clrType switch {
        "bool" => new PrimitiveTypeReference(ClrPrimitive.Boolean),
        "byte" => new PrimitiveTypeReference(ClrPrimitive.UInt8),
        "char" => new PrimitiveTypeReference(ClrPrimitive.Char),
        "decimal" => new PrimitiveTypeReference(ClrPrimitive.Decimal),
        "double" => new PrimitiveTypeReference(ClrPrimitive.Float64),
        "float" => new PrimitiveTypeReference(ClrPrimitive.Float32),
        "int" => new PrimitiveTypeReference(ClrPrimitive.Int32),
        "long" => new PrimitiveTypeReference(ClrPrimitive.Int64),
        "object" => new PrimitiveTypeReference(ClrPrimitive.Structure),
        "sbyte" => new PrimitiveTypeReference(ClrPrimitive.Int8),
        "short" => new PrimitiveTypeReference(ClrPrimitive.Int16),
        "string" => new PrimitiveTypeReference(ClrPrimitive.String),
        "uint" => new PrimitiveTypeReference(ClrPrimitive.UInt32),
        "ulong" => new PrimitiveTypeReference(ClrPrimitive.UInt64),
        "ushort" => new PrimitiveTypeReference(ClrPrimitive.UInt16),
        _ => new TypeReference(clrType),
    };
}