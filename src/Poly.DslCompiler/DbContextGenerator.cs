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

        if (!store.HasShadowKey) {
            // Natural key from unique property
            sb.AppendLine($"            b.HasKey(x => x.{store.KeyProperty!.Name});");
            // Key property gets its column config from the column metadata
            var keyCol = store.Columns.FirstOrDefault(c =>
                c.IsUnique && c.HasDefault == false);
            if (keyCol is not null)
                AppendColumnConfig(sb, keyCol);
        }
        else {
            // No unique property → shadow int key
            sb.AppendLine("            b.Property<int>(\"Id\");");
            sb.AppendLine("            b.HasKey(\"Id\");");
        }

        // Configure remaining columns (excluding the key, already configured above)
        foreach (var col in store.Columns) {
            if (col.IsUnique && !store.HasShadowKey) continue; // skip natural key column
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
        // Required constraints on nullable CLR types
        if (col.IsRequired && !DomainTypeMapping.IsNonNullableClrValueType(col.ClrTypeName)) {
            sb.AppendLine($"            b.Property(x => x.{col.Name}).IsRequired();");
        }

        // Length constraints
        if (col.MaxLength is not null) {
            sb.AppendLine($"            b.Property(x => x.{col.Name}).HasMaxLength({col.MaxLength});");
        }
        // Pattern-only properties (like Email with a regex but no length constraint)
        // get a reasonable default max length for the DB column.
        else if ((col.Name.Equals("Email", StringComparison.Ordinal) || col.Name.Equals("EmailAddress", StringComparison.Ordinal))
                 && col.Constraints.Any(c => c is PatternConstraint)) {
            sb.AppendLine($"            b.Property(x => x.{col.Name}).HasMaxLength(256);");
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    private static string Pluralize(string name) => name + "s";
}