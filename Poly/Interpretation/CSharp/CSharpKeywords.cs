namespace Poly.Interpretation.CSharp;

/// <summary>
/// The C# spelling of a primitive: the keyword the language surface writes, which is a fact about C#, not about the
/// primitive's meaning. Lives with the reader and writer that use it.
/// </summary>
public static class CSharpKeywords {
    /// <summary>The C# keyword or type name for <paramref name="id"/>.</summary>
    public static string GetCSharpKeyword(this PrimitiveType id) => id switch {
        PrimitiveType.Boolean => "bool",
        PrimitiveType.Int8 => "sbyte",
        PrimitiveType.Int16 => "short",
        PrimitiveType.Int32 => "int",
        PrimitiveType.Int64 => "long",
        PrimitiveType.UInt8 => "byte",
        PrimitiveType.UInt16 => "ushort",
        PrimitiveType.UInt32 => "uint",
        PrimitiveType.UInt64 => "ulong",
        PrimitiveType.Float32 => "float",
        PrimitiveType.Float64 => "double",
        PrimitiveType.Decimal => "decimal",
        PrimitiveType.String => "string",
        PrimitiveType.Char => "char",
        PrimitiveType.DateTime => "DateTime",
        PrimitiveType.DateOnly => "DateOnly",
        PrimitiveType.TimeOnly => "TimeOnly",
        PrimitiveType.TimeSpan => "TimeSpan",
        PrimitiveType.Guid => "Guid",
        PrimitiveType.ByteArray => "byte[]",
        PrimitiveType.Structure => "object",
        PrimitiveType.NInt => "nint",
        PrimitiveType.NUInt => "nuint",
        _ => id.ToString()
    };
}