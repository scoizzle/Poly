
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Shared domain→host mappings used by lowering convention views and C# backends.
/// Domain facts stay protocol-agnostic. Core maps Text/Number/Boolean/Uuid/Binary;
/// temporal Date/Time/Duration maps register from the temporal library.
/// </summary>
public static class DomainTypeMapping {
    /// <summary>Maps a domain primitive (or known alias) to a C# type name for codegen.</summary>
    public static string ToClrTypeName(string domainType) => domainType switch {
        "Text" or "String" => "string",
        "Number" or "Int" or "Int64" => "long",
        "Int32" => "int",
        "Boolean" or "Bool" => "bool",
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

    /// <summary>Maps a C# type spelling (after <see cref="ToClrTypeName"/>) to the CLR type it names, when that type
    /// is one the domain prelude holds (<see cref="DomainTypeSystem.Ground"/>): C# states each one's spelling, so no
    /// table here can drift from the names above.</summary>
    public static bool TryClrType(string clrTypeName, out Type type) {
        type = DomainTypeSystem.Ground(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared, clrTypeName)?.GetRuntimeType()!;
        return type is not null;
    }

    /// <summary>Static default member on a CLR value type (MinValue / Empty / Zero).</summary>
    public static bool TryDefaultMember(string clrTypeName, out string typeName, out string memberName) {
        switch (clrTypeName) {
            case "DateTime":
                typeName = "DateTime";
                memberName = "MinValue";
                return true;
            case "DateOnly":
                typeName = "DateOnly";
                memberName = "MinValue";
                return true;
            case "TimeOnly":
                typeName = "TimeOnly";
                memberName = "MinValue";
                return true;
            case "TimeSpan":
                typeName = "TimeSpan";
                memberName = "Zero";
                return true;
            case "Guid":
                typeName = "Guid";
                memberName = "Empty";
                return true;
            default:
                typeName = "";
                memberName = "";
                return false;
        }
    }

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