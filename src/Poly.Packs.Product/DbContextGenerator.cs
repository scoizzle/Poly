using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Runtime;

namespace Poly.Packs.Product;

/// <summary>
/// Generates an EF Core DbContext from a <see cref="Domain"/>.
///
/// Produces a single <c>{Domain}DbContext.cs</c> with <c>OnModelCreating</c>
/// configured for the generated entity shapes: private constructors,
/// <c>IReadOnlyList&lt;T&gt;</c> collection navs, private setters, and
/// <c>unique</c>/<c>required</c>/<c>length</c> constraints.
///
/// Output is produced via <c>GenerateCompilationUnit</c> + <c>CSharpWriter</c>
/// (Syntax IR path), not direct string building.
///
/// Consumes <see cref="StorageModel"/> from the infrastructure analysis
/// for key structure, column metadata, and navigation classification.
/// </summary>
public sealed partial class DbContextGenerator {
    /// <summary>The shadow column every stored entity's optimistic-concurrency token lives in. A storage concern, so
    /// it is never a CLR member: the entity and its DTO's shape do not change (#626, #653).</summary>
    internal const string VersionProperty = "Version";

    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _contextName;
    private readonly StorageModel _storageModel;
    private readonly Dictionary<string, StorageEntity> _storageLookup;
    private readonly IStorageSyntaxEmitter? _emitter;
    private readonly bool _implementsStoragePort;
    private readonly string _versionProperty;

    public DbContextGenerator(Domain domain, StorageModel storageModel,
        IStorageSyntaxEmitter? emitter = null, bool implementsStoragePort = false) {
        _emitter = emitter;
        _implementsStoragePort = implementsStoragePort;
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _contextName = $"{domain.Name}DbContext";
        _storageModel = storageModel ?? throw new ArgumentNullException(nameof(storageModel));
        _storageLookup = _storageModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _versionProperty = VersionPropertyName();
    }

    /// <summary>Generates the complete DbContext C# source via IR.</summary>
    public string Generate(string? @namespace = null) =>
        new CSharpWriter().Write(GenerateCompilationUnit(@namespace));

    /// <summary>Generates the DbContext as a Syntax IR compilation unit.</summary>
    public CompilationUnitNode GenerateCompilationUnit(string? @namespace = null) {
        @namespace ??= DomainSession.CSharpNamespace(_domain);
        var props = new List<PropertyDefinitionNode>();
        var entityConfigMethods = new List<Node>();

        // Constructor parameter
        var optionsParam = new Parameter("options",
            new NamedTypeReference("DbContextOptions",
                TypeArguments: [new NamedTypeReference(_contextName)]));

        // DbSet properties for all entities
        foreach (var entity in _entities) {
            var setName = Pluralize(entity.Name);
            props.Add(new PropertyDefinitionNode(
                setName,
                new NamedTypeReference("DbSet",
                    TypeArguments: [new NamedTypeReference(entity.Name)]),
                Getter: new PropertyGetterDefinitionNode(
                    Body: new Invoke(new TypeReference("Set")) {
                        TypeArguments = [new TypeReference(entity.Name)]
                    })
            ));

            // Build OnModelCreating entity config block
            entityConfigMethods.Add(BuildEntityConfig(entity));
        }

        // Relationships come after every entity block: each one needs both ends' keys, declared above.
        entityConfigMethods.AddRange(BuildRelationshipConfigs());

        // OnModelCreating body
        var modelBuilderParam = new Parameter("modelBuilder", new TypeReference("ModelBuilder"));
        var onModelCreatingBody = new Block(
            expressions: entityConfigMethods,
            variables: []
        );

        var onModelCreatingMethod = new MethodDefinitionNode(
            "OnModelCreating",
            new TypeReference("void"),
            Parameters: [modelBuilderParam],
            Body: onModelCreatingBody,
            IsOverride: true,
            AccessModifier: AccessModifier.Protected
        );

        var methods = new List<MethodDefinitionNode> { onModelCreatingMethod, BumpVersionsMethod() };
        List<FieldDefinitionNode>? fields = null;
        List<Node>? interfaces = null;
        var usings = new List<string> { "Microsoft.EntityFrameworkCore" };
        // A DateTime column's UTC converter names DateTimeKind, which no other emitted member needs (#937).
        if (_entities.Any(e => _storageLookup.TryGetValue(e.Name, out var store)
                && store.Columns.Any(c => string.Equals(c.ClrTypeName, "DateTime", StringComparison.Ordinal))))
            usings.Add("System");
        if (_implementsStoragePort) {
            usings.Add("System.Collections.Generic");
            usings.Add("System.Linq");
            fields = [PendingKeysField()];
            methods.Add(ExistsMethod());
            methods.Add(TryReserveMethod());
            methods.Add(RecordMethod());
            methods.Add(ReleaseMethod());
            interfaces = [TypeReference.To(typeof(IStoragePort))];
        }

        var contextType = new TypeDefinitionNode(
            _contextName,
            BaseType: new TypeReference("DbContext"),
            Constructors: [
                new ConstructorDefinitionNode(
                    Parameters: [optionsParam]
                ) {
                    BaseConstructorInvocation = new BaseConstructorInvocationNode(
                        [new Parameter("options")]
                    )
                }
            ],
            Properties: props,
            Methods: methods,
            Fields: fields,
            Interfaces: interfaces,
            AccessModifier: Poly.DomainModeling.Lowering.DomainAccess.ForInfrastructure(
                _domain, _entities.Select(e => e.Name))
        );

        var unit = new CompilationUnitNode(
            Usings: usings,
            Namespace: @namespace,
            Types: [contextType],
            TopLevelStatements: null
        );

        // Allow storage emitter to decorate the tree (no-op when null)
        if (_emitter != null)
            return _emitter.EmitDbContext(unit, _storageModel);

        return unit;
    }

    // ── Helpers ────────────────────────────────────────────────

    /// <summary>
    /// <c>BumpVersions(entity)</c>: the optimistic-concurrency step before a modifying save (#626, #653). It detects the
    /// changes first, because a snapshot-tracked property the action wrote is still reported <c>Unchanged</c> until
    /// then; it bumps every modified row's token so its <c>UPDATE</c> carries the original token in <c>WHERE</c>; and it
    /// bumps the route's own entity even when the action changed none of its columns, so two actions on one aggregate
    /// serialise on that row instead of each reading the other's state. A row that lost the race updates nothing and EF
    /// raises <c>DbUpdateConcurrencyException</c>, which the action route answers as 409. Never a retry here: a retry
    /// would re-run the action's guards against the state it just changed.
    /// </summary>
    private MethodDefinitionNode BumpVersionsMethod() {
        var entryParam = new Parameter("entry");
        var entityParam = new Parameter("entity", TypeReference.To(typeof(object)));

        // entry.Property("Version") is the non-generic PropertyEntry (ChangeTracker.Entries() hands out the
        // non-generic EntityEntry), and its value is boxed: a read is an unboxing cast back to long. Built per use,
        // so the writer never sees one node in two positions.
        var bump = new Assignment(
            new Member(
                new Invoke(new Member(new Variable("entry"), "Property"), new Constant(_versionProperty)),
                "CurrentValue"),
            new Ast.Nodes.Add(
                new TypeCast(
                    new NullForgiving(new Member(
                        new Invoke(new Member(new Variable("entry"), "Property"), new Constant(_versionProperty)),
                        "OriginalValue")),
                    TypeReference.To(typeof(long))),
                new Constant(1L)));

        var noToken = new Equal(
            new Invoke(new Member(new Member(entryParam, "Metadata"), "FindProperty"), new Constant(_versionProperty)),
            new Constant(null!));
        var routeEntityUnchanged = new Ast.Nodes.And(
            new Equal(new Member(entryParam, "Entity"), new Variable("entity")),
            new Equal(new Member(entryParam, "State"), new Member(new TypeReference("EntityState"), "Unchanged")));
        var bumpThisRow = new Ast.Nodes.Or(
            new Equal(new Member(entryParam, "State"), new Member(new TypeReference("EntityState"), "Modified")),
            routeEntityUnchanged);

        return new MethodDefinitionNode(
            "BumpVersions",
            new TypeReference("void"),
            Parameters: [entityParam],
            Body: new Block([
                new Invoke(new Member(new Member(new ThisReference(), "ChangeTracker"), "DetectChanges")),
                new ForEachLoop(
                    new Variable("entry"),
                    new Invoke(new Member(new Member(new ThisReference(), "ChangeTracker"), "Entries")),
                    new Block([
                        new IfStatement(noToken, new Block(new ContinueStatement())),
                        new IfStatement(bumpThisRow, new Block(bump)),
                    ])),
            ]),
            AccessModifier: AccessModifier.Public);
    }

    private static string Pluralize(string name) => Pluralizer.Pluralize(name);
}