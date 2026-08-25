namespace Poly.Packs.Sqlite;

/// <summary>
/// Applies SQLite vendor defaults to explicit domain input builders.
///
/// First shippable DBMS pack: no external database service required to dogfood.
/// Overrides core generic SQL type maps with SQLite storage-class / EF-friendly
/// type names:
/// <c>Text</c> → <c>TEXT</c>, <c>Number</c> → <c>INTEGER</c>,
/// <c>Boolean</c> → <c>INTEGER</c>, <c>DateTime</c> → <c>TEXT</c>,
/// <c>Binary</c> → <c>BLOB</c>, etc.
///
/// Load via <c>builder.Load(new SqliteLibrary())</c>.
/// </summary>
public static class SqliteDefaults {
    /// <summary>
    /// Registers SQLite type-map overrides on the given registry.
    /// Values match common EF Core SQLite provider column types / affinities.
    /// </summary>
    public static void ApplyTypeMaps(TypeMappingRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);

        // SQLite storage classes: NULL, INTEGER, REAL, TEXT, BLOB
        registry.OverrideSqlColumnType("Text", "TEXT");
        registry.OverrideSqlColumnType("String", "TEXT");

        registry.OverrideSqlColumnType("Number", "INTEGER");
        registry.OverrideSqlColumnType("Int", "INTEGER");
        registry.OverrideSqlColumnType("Int64", "INTEGER");
        registry.OverrideSqlColumnType("Int32", "INTEGER");

        // No native bool — INTEGER 0/1 is the EF Core default
        registry.OverrideSqlColumnType("Boolean", "INTEGER");
        registry.OverrideSqlColumnType("Bool", "INTEGER");

        // Date/time stored as ISO-8601 TEXT by the EF SQLite provider default
        registry.OverrideSqlColumnType("DateTime", "TEXT");
        registry.OverrideSqlColumnType("Timestamp", "TEXT");
        registry.OverrideSqlColumnType("Date", "TEXT");
        registry.OverrideSqlColumnType("DateOnly", "TEXT");
        registry.OverrideSqlColumnType("Time", "TEXT");
        registry.OverrideSqlColumnType("TimeOnly", "TEXT");
        registry.OverrideSqlColumnType("Duration", "TEXT");
        registry.OverrideSqlColumnType("TimeSpan", "TEXT");

        // Decimal has no REAL affinity that preserves precision — TEXT (EF default)
        registry.OverrideSqlColumnType("Decimal", "TEXT");

        registry.OverrideSqlColumnType("Float", "REAL");
        registry.OverrideSqlColumnType("Double", "REAL");

        registry.OverrideSqlColumnType("Guid", "TEXT");
        registry.OverrideSqlColumnType("Uuid", "TEXT");

        registry.OverrideSqlColumnType("Binary", "BLOB");
    }
}