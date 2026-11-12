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
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _contextName;
    private readonly StorageModel _storageModel;
    private readonly Dictionary<string, StorageEntity> _storageLookup;
    private readonly IStorageSyntaxEmitter? _emitter;
    private readonly bool _implementsStoragePort;

    public DbContextGenerator(Domain domain, StorageModel storageModel,
        IStorageSyntaxEmitter? emitter = null, bool implementsStoragePort = false) {
        _emitter = emitter;
        _implementsStoragePort = implementsStoragePort;
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _contextName = $"{domain.Name}DbContext";
        _storageModel = storageModel ?? throw new ArgumentNullException(nameof(storageModel));
        _storageLookup = _storageModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    /// <summary>Generates the complete DbContext C# source via IR.</summary>
    public string Generate(string @namespace = "Poly.Generated") =>
        new CSharpWriter().Write(GenerateCompilationUnit(@namespace));

    /// <summary>Generates the DbContext as a Syntax IR compilation unit.</summary>
    public CompilationUnitNode GenerateCompilationUnit(string @namespace = "Poly.Generated") {
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

        var methods = new List<MethodDefinitionNode> { onModelCreatingMethod };
        List<FieldDefinitionNode>? fields = null;
        List<Node>? interfaces = null;
        var usings = new List<string> { "Microsoft.EntityFrameworkCore" };
        if (_implementsStoragePort) {
            usings.Add("System.Collections.Generic");
            usings.Add("System.Linq");
            fields = [PendingKeysField()];
            methods.Add(ExistsMethod());
            methods.Add(TryReserveMethod());
            methods.Add(RecordMethod());
            methods.Add(ReleaseMethod());
            interfaces = [new ClrTypeReference(typeof(IStoragePort))];
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
            Interfaces: interfaces
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

    private static string Pluralize(string name) => Pluralizer.Pluralize(name);
}