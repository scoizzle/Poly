using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;

namespace Poly.Packs.SqlServer;

/// <summary>
/// Applies SQL Server vendor defaults to explicit domain input builders.
///
/// Overrides core generic SQL type maps with SQL Server-specific strings:
/// <c>Text</c> → <c>nvarchar(max)</c>, <c>Boolean</c> → <c>bit</c>,
/// <c>DateTime</c> → <c>datetime2</c>, etc. Also registers a storage convention
/// that validates identifier length (max 128 chars) and rejects oversized
/// column/table names.
///
/// Usage:
/// <code>
/// var inputs = DomainHostBuilder.Create().WithStorageFacets()
///     .AddSqlServerDefaults();
/// </code>
/// </summary>
public static class SqlServerDefaults {
    /// <summary>
    /// Registers SQL Server type-map overrides and the identifier-length
    /// convention on <paramref name="builder"/>.
    /// </summary>
    public static DomainHostBuilder AddSqlServerDefaults(this DomainHostBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Load(new SqlServerLibrary());
    }

    /// <summary>
    /// Registers SQL Server type-map overrides on the given registry.
    /// </summary>
    public static void ApplyTypeMaps(TypeMappingRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);

        // Character types: SQL Server uses n-prefixed Unicode by default
        registry.OverrideSqlColumnType("Text", "nvarchar(max)");
        registry.OverrideSqlColumnType("String", "nvarchar(max)");

        // Numeric types
        registry.OverrideSqlColumnType("Boolean", "bit");
        registry.OverrideSqlColumnType("Bool", "bit");
        registry.OverrideSqlColumnType("Int32", "int");

        // Date/time
        registry.OverrideSqlColumnType("DateTime", "datetime2");
        registry.OverrideSqlColumnType("Timestamp", "datetime2");

        // Binary
        registry.OverrideSqlColumnType("Binary", "varbinary(max)");

        // Floating-point
        registry.OverrideSqlColumnType("Float", "float");
        registry.OverrideSqlColumnType("Double", "float");

        // Identifier types
        registry.OverrideSqlColumnType("Guid", "uniqueidentifier");
        registry.OverrideSqlColumnType("Uuid", "uniqueidentifier");
    }
}