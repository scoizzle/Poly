namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Shared domain→host mappings used by lowering convention views and C# backends.
/// Domain facts stay protocol-agnostic; CLR/SQL names live here as adapter projections.
/// </summary>
public static class DomainTypeMapping {
    /// <summary>Maps a domain primitive (or known alias) to a C# type name for codegen.</summary>
    public static string ToClrTypeName(string domainType) => domainType switch {
        "Text" or "String" => "string",
        "Number" or "Int" or "Int64" => "long",
        "Int32" => "int",
        "Boolean" or "Bool" => "bool",
        "DateTime" or "Timestamp" => "DateTime",
        "Date" or "DateOnly" => "DateOnly",
        "Time" or "TimeOnly" => "TimeOnly",
        "Duration" or "TimeSpan" => "TimeSpan",
        "Decimal" => "decimal",
        "Float" or "Double" => "double",
        "Guid" or "Uuid" => "Guid",
        "Binary" => "byte[]",
        _ => domainType,
    };

    /// <summary>
    /// Maps a domain primitive to a <b>generic SQL</b> column type (D3).
    /// Vendor packs override via <see cref="TypeMappingRegistry"/> — core must
    /// not bake SQL Server / Oracle / Postgres-specific strings as permanent defaults.
    /// </summary>
    public static string ToSqlColumnType(string domainType) => domainType switch {
        "Text" or "String" => "varchar",
        "Number" or "Int" or "Int64" => "bigint",
        "Int32" => "integer",
        "Boolean" or "Bool" => "boolean",
        "DateTime" or "Timestamp" => "timestamp",
        "Date" or "DateOnly" => "date",
        "Time" or "TimeOnly" => "time",
        "Duration" or "TimeSpan" => "interval",
        "Decimal" => "decimal",
        "Float" or "Double" => "double precision",
        "Guid" or "Uuid" => "uuid",
        "Binary" => "binary",
        _ => "varchar",
    };

    /// <summary>True when the CLR type is a non-nullable value type in C#.</summary>
    public static bool IsNonNullableClrValueType(string clrTypeName) => clrTypeName is
        "int" or "long" or "double" or "decimal" or "float" or "bool"
        or "DateTime" or "DateOnly" or "TimeOnly" or "TimeSpan" or "Guid";

    public static string ToCamelCase(string name) {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
            return name;

        int upperCount = 0;
        for (int i = 0; i < name.Length && char.IsUpper(name[i]); i++)
            upperCount++;

        if (upperCount <= 1)
            return char.ToLowerInvariant(name[0]) + name.Substring(1);

        return name.Substring(0, upperCount).ToLowerInvariant()
             + name.Substring(upperCount);
    }

    public static string ToPascalCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsUpper(name[0])
            ? name
            : char.ToUpperInvariant(name[0]) + name.Substring(1);
}