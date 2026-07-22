using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Lowering;

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
    private readonly InfrastructureModel _infraModel;
    private readonly Dictionary<string, StorageEntity> _storageLookup;

    public DbContextGenerator(Domain domain, InfrastructureModel? infraModel = null) {
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _contextName = $"{domain.Name}DbContext";
        _infraModel = infraModel ?? new InfrastructureAnalyzer(domain).Analyze();
        _storageLookup = _infraModel.Storage.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
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

    // ── Helpers ────────────────────────────────────────────────

    private static string Pluralize(string name) => name + "s";

    /// <summary>Escapes <c>\</c> and <c>"</c> inside double-quoted C# string literals.</summary>
    private static string EscapeCSharpString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);
}