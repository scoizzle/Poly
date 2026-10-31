using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

using ClrPrimitive = Poly.Introspection.PrimitiveType;

namespace Poly.DslCompiler;

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
public sealed class DbContextGenerator {
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

    private StorageEntity GetStorageEntity(Entity entity) =>
        _storageLookup[entity.Name];

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
            methods.Add(RecordMethod());
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

    /// <summary>
    /// The context is the host half of <see cref="IStoragePort"/>: <c>Exists</c>
    /// queries the mapped <c>DbSet</c>; <c>Record</c> keeps the pending key a
    /// nested create must see before the host saves.
    /// </summary>
    private static FieldDefinitionNode PendingKeysField() =>
        new("_pendingKeys",
            new NamedTypeReference("HashSet", TypeArguments: [new PrimitiveTypeReference(ClrPrimitive.String)]),
            DefaultValue: new New(new NamedTypeReference("HashSet", TypeArguments: [new PrimitiveTypeReference(ClrPrimitive.String)])),
            IsReadOnly: true,
            AccessModifier: AccessModifier.Private);

    private MethodDefinitionNode ExistsMethod() {
        var entityParam = new Parameter("entity", new PrimitiveTypeReference(ClrPrimitive.String));
        var propertyParam = new Parameter("property", new PrimitiveTypeReference(ClrPrimitive.String));
        var valueParam = new Parameter("value", new PrimitiveTypeReference(ClrPrimitive.String));
        var nodes = new List<Node> {
            new IfStatement(
                new Invoke(
                    new Member(new Member(new ThisReference(), "_pendingKeys"), "Contains"),
                    PendingKey(entityParam, propertyParam, valueParam)),
                new Block([new Return(new Constant(true))]))
        };

        foreach (var store in _storageModel.Entities.OrderBy(e => e.Name, StringComparer.Ordinal)) {
            var unique = store.Columns
                .Where(c => c.IsUnique)
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
            if (unique.Count == 0)
                continue;
            var body = new List<Node>();
            foreach (var column in unique) {
                var row = new Parameter("e");
                Node comparison = string.Equals(column.ClrTypeName, "string", StringComparison.Ordinal)
                    ? new Equal(new Member(row, column.Name), valueParam)
                    : new Equal(
                        new Invoke(new Member(new Member(row, column.Name), "ToString")),
                        valueParam);
                var any = new Invoke(
                    new Member(new Member(new ThisReference(), Pluralize(store.Name)), "Any"),
                    new Lambda([row], comparison));
                body.Add(new IfStatement(
                    new Equal(propertyParam, new Constant(column.Name)),
                    new Block([new Return(any)])));
            }
            body.Add(new Return(new Constant(false)));
            nodes.Add(new IfStatement(
                new Equal(entityParam, new Constant(store.Name)),
                new Block(body)));
        }

        nodes.Add(new Return(new Constant(false)));
        return new MethodDefinitionNode(
            "Exists",
            new PrimitiveTypeReference(ClrPrimitive.Boolean),
            Parameters: [entityParam, propertyParam, valueParam],
            Body: new Block(nodes));
    }

    private static MethodDefinitionNode RecordMethod() {
        var entityParam = new Parameter("entity", new PrimitiveTypeReference(ClrPrimitive.String));
        var propertyParam = new Parameter("property", new PrimitiveTypeReference(ClrPrimitive.String));
        var valueParam = new Parameter("value", new PrimitiveTypeReference(ClrPrimitive.String));
        return new MethodDefinitionNode(
            "Record",
            new TypeReference("void"),
            Parameters: [entityParam, propertyParam, valueParam],
            Body: new Block([
                new Invoke(
                    new Member(new Member(new ThisReference(), "_pendingKeys"), "Add"),
                    PendingKey(entityParam, propertyParam, valueParam))
            ]));
    }

    private static Node PendingKey(Node entity, Node property, Node value) {
        Node key = new Poly.Ast.Nodes.Add(entity, new Constant("\u001f"));
        key = new Poly.Ast.Nodes.Add(key, property);
        key = new Poly.Ast.Nodes.Add(key, new Constant("\u001f"));
        return new Poly.Ast.Nodes.Add(key, value);
    }

    /// <summary>Builds Syntax IR for a single entity's OnModelCreating config block.</summary>
    private Node BuildEntityConfig(Entity entity) {
        var store = GetStorageEntity(entity);
        var tableName = EmitTableName(entity, store);

        // Lambda parameter: b
        var bParam = new Parameter("b");

        // Build statements inside the lambda body
        var bodyNodes = new List<Node>();

        // b.ToTable("TableName") — or with verified-range CHECK constraints via the modern
        // ToTable(tableBuilder => …) API (HasCheckConstraint on EntityTypeBuilder is obsolete).
        // Checks are emitted only when the invariant analysis PROVED no effect can produce an
        // out-of-range value, so the DB constraints are sound.
        var checks = store.Columns
            .Where(c => c.IsRangeVerified && c.VerifiedRange is { } vr && (vr.Min is not null || vr.Max is not null))
            .Select(c => (Name: $"CK_{c.ColumnName}", Sql: CheckSql(c.ColumnName, c.VerifiedRange!)))
            .ToList();
        if (checks.Count == 0) {
            bodyNodes.Add(new Invoke(
                new Member(bParam, "ToTable"),
                new Constant(tableName)
            ));
        }
        else {
            var tParam = new Parameter("t");
            var tBody = checks.Select(check => (Node)new Invoke(
                new Member(tParam, "HasCheckConstraint"),
                new Constant(check.Name), new Constant(check.Sql))).ToList();
            bodyNodes.Add(new Invoke(
                new Member(bParam, "ToTable"),
                new Constant(tableName),
                new Lambda([tParam], new Block(tBody))
            ));
        }

        // Synthetic int identity. A `unique` property is a unique index, not the primary key,
        // so a second unique on the same entity is stored instead of dropped.
        // PolyId when the entity already has a CLR property named Id.
        var shadowKey = ShadowKeyName(entity, store);
        bodyNodes.Add(new Invoke(
            new Member(bParam, "Property"),
            new Constant(shadowKey)
        ) {
            TypeArguments = [new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.Int32)]
        });
        bodyNodes.Add(new Invoke(
            new Member(bParam, "HasKey"),
            new Constant(shadowKey)
        ));

        // Column configurations
        foreach (var col in store.Columns)
            bodyNodes.Add(BuildColumnConfigNode(bParam, col));

        foreach (var col in store.Columns.Where(c => c.IsUnique)) {
            var xParam = new Parameter("x");
            var index = new Invoke(
                new Member(bParam, "HasIndex"),
                new Lambda([xParam], new Member(xParam, col.Name)));
            bodyNodes.Add(new Invoke(new Member(index, "IsUnique")));
        }

        // Collection navigations: set backing field access mode
        foreach (var nav in store.CollectionNavigations) {
            // b.Metadata.FindNavigation(nameof(Entity.NavProp))!.SetPropertyAccessMode(PropertyAccessMode.Field)
            bodyNodes.Add(BuildNavigationConfigNode(bParam, nav, entity.Name));
        }

        // modelBuilder.Entity<EntityName>(b => { ... })
        var lambdaBody = new Block(expressions: bodyNodes, variables: []);

        return new Invoke(
            new Member(new TypeReference("modelBuilder"), "Entity"),
            new Lambda([bParam], lambdaBody)
        ) {
            TypeArguments = [new TypeReference(entity.Name)]
        };
    }

    /// <summary>Builds Syntax IR for a column's fluent configuration chain.</summary>
    private static Node BuildColumnConfigNode(Parameter bParam, StorageColumn col) {
        var colName = col.ColumnName;
        var propName = col.Name;
        var needsHasColumnName = !string.Equals(colName, propName, StringComparison.Ordinal);

        // b.Property(x => x.PropName)
        var xParam = new Parameter("x");
        Node chain = new Invoke(
            new Member(bParam, "Property"),
            new Lambda([xParam], new Member(xParam, propName))
        );

        // .HasColumnName("name")
        if (needsHasColumnName) {
            chain = new Invoke(
                new Member(chain, "HasColumnName"),
                new Constant(colName)
            );
        }

        // Enum columns store the int backing value. The CLR type name is not a SQL type.
        var columnType = col.ColumnType;
        if (col.IsEnum
            && (string.Equals(columnType, col.ClrTypeName, StringComparison.Ordinal)
                || string.Equals(columnType, col.DomainType, StringComparison.Ordinal)))
            columnType = "INTEGER";
        chain = new Invoke(
            new Member(chain, "HasColumnType"),
            new Constant(columnType)
        );
        if (col.IsEnum) {
            chain = new Invoke(new Member(chain, "HasConversion")) {
                TypeArguments = [new PrimitiveTypeReference(ClrPrimitive.Int32)]
            };
        }

        // .IsRequired()
        if (col.IsRequired && !DomainTypeMapping.IsNonNullableClrValueType(col.ClrTypeName)) {
            chain = new Invoke(new Member(chain, "IsRequired"));
        }

        // .HasMaxLength(n)
        if (col.MaxLength is not null) {
            chain = new Invoke(
                new Member(chain, "HasMaxLength"),
                new Constant((int)col.MaxLength.Value)
            );
        }
        else if ((propName.Equals("Email", StringComparison.Ordinal)
                  || propName.Equals("EmailAddress", StringComparison.Ordinal))
                 && col.Constraints.Any(c => c is PatternConstraint)) {
            chain = new Invoke(
                new Member(chain, "HasMaxLength"),
                new Constant(256)
            );
        }

        return chain;
    }

    /// <summary>Builds Syntax IR for a collection navigation's property access mode config.</summary>
    /// <summary>Formats a numeric bound for a SQL CHECK constraint.</summary>
    private static string FormatBound(double d) =>
        d == Math.Floor(d) ? d.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Builds the SQL predicate for a verified column's CHECK constraint.</summary>
    private static string CheckSql(string columnName, ValueRange verified) {
        var parts = new List<string>();
        if (verified.Min is not null) parts.Add($"{columnName} >= {FormatBound(verified.Min.Value)}");
        if (verified.Max is not null) parts.Add($"{columnName} <= {FormatBound(verified.Max.Value)}");
        return string.Join(" AND ", parts);
    }

    private static Node BuildNavigationConfigNode(Parameter bParam, StorageNavigation nav, string entityName) {
        // b.Metadata.FindNavigation(nameof(Entity.NavProp))!.SetPropertyAccessMode(PropertyAccessMode.Field)
        // Issue 8: nameof must receive a Member expression, not a string Constant
        var nameofCall = new Invoke(
            new TypeReference("nameof"),
            new Member(new TypeReference(entityName), nav.PropertyName));

        return new Invoke(
            new Member(
                new NullForgiving(
                    new Invoke(
                        new Member(new Member(bParam, "Metadata"), "FindNavigation"),
                        nameofCall
                    )
                ),
                "SetPropertyAccessMode"
            ),
            new Member(new TypeReference("PropertyAccessMode"), "Field")
        );
    }

    // ── Helpers ────────────────────────────────────────────────

    private static string Pluralize(string name) => Pluralizer.Pluralize(name);

    /// <summary>
    /// Default storage table is <c>Name + "s"</c> (<c>Opportunitys</c>). The host
    /// emits the English plural unless <c>table("…")</c> overrode the name.
    /// </summary>
    private static string EmitTableName(Entity entity, StorageEntity store) =>
        string.Equals(store.TableName, entity.Name + "s", StringComparison.Ordinal)
            ? Pluralize(entity.Name)
            : store.TableName;

    private static string ShadowKeyName(Entity entity, StorageEntity store) {
        var taken = entity.Properties.Any(p => p.Name == "Id")
            || store.Columns.Any(c => c.Name == "Id");
        return taken ? "PolyId" : "Id";
    }
}