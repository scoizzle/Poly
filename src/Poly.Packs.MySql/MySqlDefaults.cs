using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;

namespace Poly.Packs.MySql;

/// <summary>
/// Applies MySQL vendor defaults to explicit domain input builders.
///
/// Type maps follow MySQL conventions / EF Core MySQL provider defaults:
/// <c>Text</c> → <c>longtext</c>, <c>Number</c> → <c>bigint</c>,
/// <c>Boolean</c> → <c>tinyint(1)</c>, <c>DateTime</c> → <c>datetime(6)</c>,
/// etc.
///
/// Usage:
/// <code>
/// var inputs = DomainInputBuilder.CreateWithSqlPack()
///     .AddMySqlDefaults();
/// </code>
/// </summary>
public static class MySqlDefaults {
    public static DomainInputBuilder AddMySqlDefaults(this DomainInputBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        ApplyTypeMaps(builder.TypeMaps);
        return builder;
    }

    public static void ApplyTypeMaps(TypeMappingRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);

        registry.OverrideSqlColumnType("Text", "longtext");
        registry.OverrideSqlColumnType("String", "longtext");

        registry.OverrideSqlColumnType("Number", "bigint");
        registry.OverrideSqlColumnType("Int", "bigint");
        registry.OverrideSqlColumnType("Int64", "bigint");
        registry.OverrideSqlColumnType("Int32", "int");

        registry.OverrideSqlColumnType("Boolean", "tinyint(1)");
        registry.OverrideSqlColumnType("Bool", "tinyint(1)");

        registry.OverrideSqlColumnType("DateTime", "datetime(6)");
        registry.OverrideSqlColumnType("Timestamp", "datetime(6)");
        registry.OverrideSqlColumnType("Date", "date");
        registry.OverrideSqlColumnType("DateOnly", "date");

        registry.OverrideSqlColumnType("Float", "float");
        registry.OverrideSqlColumnType("Double", "double");

        registry.OverrideSqlColumnType("Decimal", "decimal(65,30)");

        registry.OverrideSqlColumnType("Guid", "char(36)");
        registry.OverrideSqlColumnType("Uuid", "char(36)");

        registry.OverrideSqlColumnType("Binary", "blob");
    }
}