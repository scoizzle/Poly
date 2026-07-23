using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Lowering;
using Poly.Introspection;
using Poly.Syntax;
using Poly.Syntax.Nodes;

namespace Poly.DslCompiler;

/// <summary>
/// Generates an EF Core DbContext from a <see cref="Domain"/>.
///
/// Produces a single <c>LibraryDbContext.cs</c> with <c>OnModelCreating</c>
/// configured for the generated entity shapes: private constructors,
/// <c>IReadOnlyList&lt;T&gt;</c> collection navs, private setters, and
/// <c>unique</c>/<c>required</c>/<c>length</c> constraints.
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

    public DbContextGenerator(Domain domain, StorageModel storageModel,
        IStorageSyntaxEmitter? emitter = null) {
        _emitter = emitter;
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _contextName = $"{domain.Name}DbContext";
        _storageModel = storageModel ?? throw new ArgumentNullException(nameof(storageModel));
        _storageLookup = _storageModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    private StorageEntity GetStorageEntity(Entity entity) =>
        _storageLookup[entity.Name];

    /// <summary>Generates the complete DbContext C# source.</summary>
    public string Generate(string @namespace = "Poly.Generated") {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine($"namespace {@namespace};");
        sb.AppendLine();
        AppendDbContext(sb);
        return sb.ToString();
    }

    private void AppendDbContext(StringBuilder sb) {
        sb.AppendLine($"public class {_contextName} : DbContext");
        sb.AppendLine("{");

        // Constructor
        sb.AppendLine($"    public {_contextName}(DbContextOptions<{_contextName}> options) : base(options) {{ }}");
        sb.AppendLine();

        // DbSet properties for all entities. CRUD API filtering is a consumer concern.
        // Child entities (Loan, Fine, etc.) are reached through parent navigation properties.
        foreach (var entity in _entities) {
            var setName = Pluralize(entity.Name);
            sb.AppendLine($"    public DbSet<{entity.Name}> {setName} => Set<{entity.Name}>();");
        }
        sb.AppendLine();

        // OnModelCreating
        sb.AppendLine("    protected override void OnModelCreating(ModelBuilder modelBuilder)");
        sb.AppendLine("    {");

        foreach (var entity in _entities) {
            AppendEntityConfig(sb, entity);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private void AppendEntityConfig(StringBuilder sb, Entity entity) {
        var store = GetStorageEntity(entity);
        var tableName = store.TableName;

        sb.AppendLine($"        // ── {entity.Name} ─────────────────────────────────────────────────");
        sb.AppendLine($"        modelBuilder.Entity<{entity.Name}>(b =>");
        sb.AppendLine("        {");

        // P3: table override from annotation / default plural
        sb.AppendLine($"            b.ToTable(\"{EscapeCSharpString(tableName)}\");");

        if (!store.HasShadowKey) {
            // Natural key from KeyProperty — match by identity, not "any unique"
            var keyProp = store.KeyProperty!;
            sb.AppendLine($"            b.HasKey(x => x.{keyProp.Name});");
            var keyCol = store.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, keyProp.Name, StringComparison.Ordinal));
            if (keyCol is not null)
                AppendColumnConfig(sb, keyCol);
        }
        else {
            // No unique property → shadow int key
            sb.AppendLine("            b.Property<int>(\"Id\");");
            sb.AppendLine("            b.HasKey(\"Id\");");
        }

        // Remaining columns (skip the natural key once — other unique props still emit)
        foreach (var col in store.Columns) {
            if (!store.HasShadowKey
                && store.KeyProperty is not null
                && string.Equals(col.Name, store.KeyProperty.Name, StringComparison.Ordinal)) {
                continue;
            }
            AppendColumnConfig(sb, col);
        }

        // Collection navigations: set backing field access mode
        foreach (var nav in store.CollectionNavigations) {
            sb.AppendLine($"            b.Metadata.FindNavigation(nameof({entity.Name}.{nav.PropertyName}))!");
            sb.AppendLine($"                .SetPropertyAccessMode(PropertyAccessMode.Field);");
        }

        sb.AppendLine("        });");
        sb.AppendLine();
    }

    /// <summary>Appends column-level configuration for a single StorageColumn.</summary>
    private static void AppendColumnConfig(StringBuilder sb, StorageColumn col) {
        // P3: physical column name (override or camelCase default)
        var colName = col.ColumnName;
        var propName = col.Name;
        var needsHasColumnName = !string.Equals(colName, propName, StringComparison.Ordinal);

        // Build the property expression and column name call
        var propCall = $"b.Property(x => x.{propName})";

        if (needsHasColumnName)
            propCall += $".HasColumnName(\"{EscapeCSharpString(colName)}\")";

        // P3: column type override (from annotation or core default)
        propCall += $".HasColumnType(\"{EscapeCSharpString(col.ColumnType)}\")";

        // Required constraints on nullable CLR types
        if (col.IsRequired && !DomainTypeMapping.IsNonNullableClrValueType(col.ClrTypeName)) {
            propCall += ".IsRequired()";
        }

        // Length constraints
        if (col.MaxLength is not null) {
            propCall += $".HasMaxLength({col.MaxLength})";
        }
        // Pattern-only properties (like Email with a regex but no length constraint)
        // get a reasonable default max length for the DB column.
        else if ((propName.Equals("Email", StringComparison.Ordinal) || propName.Equals("EmailAddress", StringComparison.Ordinal))
                 && col.Constraints.Any(c => c is PatternConstraint)) {
            propCall += ".HasMaxLength(256)";
        }

        sb.AppendLine($"            {propCall};");
    }

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
            Methods: [onModelCreatingMethod]
        );

        var unit = new CompilationUnitNode(
            Usings: ["Microsoft.EntityFrameworkCore"],
            Namespace: @namespace,
            Types: [contextType],
            TopLevelStatements: null
        );

        // Allow storage emitter to decorate the tree (no-op when null)
        if (_emitter != null)
            return _emitter.EmitDbContext(unit, _storageModel);

        return unit;
    }

    /// <summary>Builds Syntax IR for a single entity's OnModelCreating config block.</summary>
    private Node BuildEntityConfig(Entity entity) {
        var store = GetStorageEntity(entity);
        var tableName = store.TableName;

        // Lambda parameter: b
        var bParam = new Parameter("b");

        // Build statements inside the lambda body
        var bodyNodes = new List<Node>();

        // b.ToTable("TableName")
        bodyNodes.Add(new Invoke(
            new Member(bParam, "ToTable"),
            new Constant(tableName)
        ));

        // Key configuration
        if (!store.HasShadowKey && store.KeyProperty is not null) {
            var keyProp = store.KeyProperty;
            // b.HasKey(x => x.KeyPropName)
            var xParam = new Parameter("x");
            bodyNodes.Add(new Invoke(
                new Member(bParam, "HasKey"),
                new Lambda([xParam], new Member(xParam, keyProp.Name))
            ));
            // Emit column config for the key property once after HasKey (Issue 7)
            var keyCol = store.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, keyProp.Name, StringComparison.Ordinal));
            if (keyCol is not null)
                bodyNodes.Add(BuildColumnConfigNode(bParam, keyCol));
        }
        else {
            // Issue 6: Must emit both b.Property<int>("Id") and b.HasKey("Id")
            bodyNodes.Add(new Invoke(
                new Member(bParam, "Property"),
                new Constant("Id")
            ) {
                TypeArguments = [new TypeReference("int")]
            });
            bodyNodes.Add(new Invoke(
                new Member(bParam, "HasKey"),
                new Constant("Id")
            ));
        }

        // Column configurations
        foreach (var col in store.Columns) {
            if (!store.HasShadowKey
                && store.KeyProperty is not null
                && string.Equals(col.Name, store.KeyProperty.Name, StringComparison.Ordinal)) {
                continue;
            }
            bodyNodes.Add(BuildColumnConfigNode(bParam, col));
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

        // .HasColumnType("type")
        chain = new Invoke(
            new Member(chain, "HasColumnType"),
            new Constant(col.ColumnType)
        );

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

    private static string Pluralize(string name) => name + "s";

    /// <summary>Escapes <c>\</c> and <c>"</c> inside double-quoted C# string literals.</summary>
    private static string EscapeCSharpString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);
}