using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;

namespace Poly.DslCompiler;

/// <summary>
/// Generates an EF Core DbContext from a <see cref="Domain"/>.
///
/// Produces a single <c>LibraryDbContext.cs</c> with <c>OnModelCreating</c>
/// configured for the generated entity shapes: private constructors,
/// <c>IReadOnlyList&lt;T&gt;</c> collection navs, private setters, and
/// <c>unique</c>/<c>required</c>/<c>length</c> constraints.
///
/// This is the "get it working first" implementation — string-based generation.
/// Future versions may produce <c>TypeDefinitionNode</c> trees for CSharpGenerator.
/// </summary>
public sealed class DbContextGenerator {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _contextName;

    public DbContextGenerator(Domain domain) {
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _contextName = $"{domain.Name}DbContext";
    }

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
        var tableName = Pluralize(entity.Name);
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));

        sb.AppendLine($"        // ── {entity.Name} ─────────────────────────────────────────────────");
        sb.AppendLine($"        modelBuilder.Entity<{entity.Name}>(b =>");
        sb.AppendLine("        {");

        if (uniqueProp is not null) {
            // Unique property → natural key
            sb.AppendLine($"            b.HasKey(x => x.{uniqueProp.Name});");
            // Key property also gets its column config here
            AppendPropertyConfig(sb, entity, uniqueProp);
        }
        else {
            // No unique property → shadow key
            sb.AppendLine("            b.Property<int>(\"Id\");");
            sb.AppendLine("            b.HasKey(\"Id\");");
        }

        // Configure remaining properties
        foreach (var prop in entity.Properties) {
            if (uniqueProp is not null && string.Equals(prop.Name, uniqueProp.Name, StringComparison.Ordinal))
                continue;
            AppendPropertyConfig(sb, entity, prop);
        }

        // Collection navigations: set backing field access mode
        var domainRels = _domain.Relationships.ToList();
        foreach (var rel in domainRels) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal))
                continue;

            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                         or RelationshipCardinality.ManyToMany;

            if (isMany) {
                var pascalName = ToPascalCase(rel.Name);
                sb.AppendLine($"            b.Metadata.FindNavigation(nameof({entity.Name}.{pascalName}))!");
                sb.AppendLine($"                .SetPropertyAccessMode(PropertyAccessMode.Field);");
            }
            // Singular navs don't need explicit config — EF resolves them automatically
        }

        sb.AppendLine("        });");
        sb.AppendLine();
    }

    /// <summary>Appends column-level configuration for a single property.</summary>
    private void AppendPropertyConfig(StringBuilder sb, Entity entity, Property prop) {
        // Required constraints on nullable types
        if (prop.Constraints.Any(c => c is RequiredConstraint) && !IsValueDomainType(prop.Type.TypeName)) {
            sb.AppendLine($"            b.Property(x => x.{prop.Name}).IsRequired();");
        }

        // Length constraints
        var lengthC = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault();
        if (lengthC is not null) {
            sb.AppendLine($"            b.Property(x => x.{prop.Name}).HasMaxLength({lengthC.MaxLength});");
        }
        // Pattern-only properties (like Email with a regex but no length constraint)
        // get a reasonable default max length for the DB column.
        else if (prop.Name.Equals("Email", StringComparison.Ordinal) || prop.Name.Equals("EmailAddress", StringComparison.Ordinal)) {
            var hasPattern = prop.Constraints.Any(c => c is PatternConstraint);
            if (hasPattern) {
                sb.AppendLine($"            b.Property(x => x.{prop.Name}).HasMaxLength(256);");
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    /// <summary>Returns true if the entity has required entity-reference constructor params.</summary>
    private bool HasRequiredEntityRef(Entity entity) {
        if (entity.Properties.Any(p => !p.Constraints.Any(c => c is DefaultValueConstraint)
            && _entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal))))
            return true;
        if (_domain.Relationships.Any(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal)
            && r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
            && !string.Equals(r.Target.TypeName, entity.Name, StringComparison.Ordinal)))
            return true;
        return false;
    }

    private static string Pluralize(string name) {
        // Simple pluralization: add "s". Will be wrong for some words
        // (e.g. "Book" → "Books" correct, "Person" → "Persons" wrong).
        // A future version can accept an override or use more sophisticated logic.
        return name + "s";
    }

    private static string ToPascalCase(string name) {
        if (string.IsNullOrEmpty(name) || char.IsUpper(name[0]))
            return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static bool IsValueDomainType(string typeName) => typeName switch {
        "Number" or "Int" or "Int64" or "Int32" => true,
        "Boolean" or "Bool" => true,
        "DateTime" or "Timestamp" => true,
        "Date" or "DateOnly" => true,
        "Time" or "TimeOnly" => true,
        "Duration" or "TimeSpan" => true,
        "Decimal" => true,
        "Float" or "Double" => true,
        "Guid" or "Uuid" => true,
        _ => false,
    };
}