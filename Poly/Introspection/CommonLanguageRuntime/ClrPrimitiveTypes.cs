namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR's runtime types for its own table of built-in types (<see cref="ClrPrimitive"/>), both directions.
/// </summary>
internal static class ClrPrimitiveTypes {
    /// <summary>The CLR type <paramref name="id"/> denotes, or null when it names no CLR type.</summary>
    public static Type? GetClrType(this ClrPrimitive id) => id switch {
        ClrPrimitive.Boolean => typeof(bool),
        ClrPrimitive.Int8 => typeof(sbyte),
        ClrPrimitive.Int16 => typeof(short),
        ClrPrimitive.Int32 => typeof(int),
        ClrPrimitive.Int64 => typeof(long),
        ClrPrimitive.UInt8 => typeof(byte),
        ClrPrimitive.UInt16 => typeof(ushort),
        ClrPrimitive.UInt32 => typeof(uint),
        ClrPrimitive.UInt64 => typeof(ulong),
        ClrPrimitive.Float32 => typeof(float),
        ClrPrimitive.Float64 => typeof(double),
        ClrPrimitive.Decimal => typeof(decimal),
        ClrPrimitive.String => typeof(string),
        ClrPrimitive.Char => typeof(char),
        ClrPrimitive.DateTime => typeof(DateTime),
        ClrPrimitive.DateOnly => typeof(DateOnly),
        ClrPrimitive.TimeOnly => typeof(TimeOnly),
        ClrPrimitive.TimeSpan => typeof(TimeSpan),
        ClrPrimitive.Guid => typeof(Guid),
        ClrPrimitive.DateTimeOffset => typeof(DateTimeOffset),
        ClrPrimitive.ByteArray => typeof(byte[]),
        ClrPrimitive.Structure => typeof(object),
        ClrPrimitive.NInt => typeof(nint),
        ClrPrimitive.NUInt => typeof(nuint),
        _ => null
    };

    /// <summary>The primitive <paramref name="type"/> denotes, or null when it is not a CLR primitive.</summary>
    public static ClrPrimitive? GetPrimitiveType(this Type type) => type switch {
        Type t when t == typeof(bool) => ClrPrimitive.Boolean,
        Type t when t == typeof(sbyte) => ClrPrimitive.Int8,
        Type t when t == typeof(short) => ClrPrimitive.Int16,
        Type t when t == typeof(int) => ClrPrimitive.Int32,
        Type t when t == typeof(long) => ClrPrimitive.Int64,
        Type t when t == typeof(byte) => ClrPrimitive.UInt8,
        Type t when t == typeof(ushort) => ClrPrimitive.UInt16,
        Type t when t == typeof(uint) => ClrPrimitive.UInt32,
        Type t when t == typeof(ulong) => ClrPrimitive.UInt64,
        Type t when t == typeof(float) => ClrPrimitive.Float32,
        Type t when t == typeof(double) => ClrPrimitive.Float64,
        Type t when t == typeof(decimal) => ClrPrimitive.Decimal,
        Type t when t == typeof(string) => ClrPrimitive.String,
        Type t when t == typeof(char) => ClrPrimitive.Char,
        Type t when t == typeof(DateTime) => ClrPrimitive.DateTime,
        Type t when t == typeof(DateOnly) => ClrPrimitive.DateOnly,
        Type t when t == typeof(TimeOnly) => ClrPrimitive.TimeOnly,
        Type t when t == typeof(TimeSpan) => ClrPrimitive.TimeSpan,
        Type t when t == typeof(Guid) => ClrPrimitive.Guid,
        Type t when t == typeof(DateTimeOffset) => ClrPrimitive.DateTimeOffset,
        Type t when t == typeof(byte[]) => ClrPrimitive.ByteArray,
        Type t when t == typeof(object) => ClrPrimitive.Structure,
        Type t when t == typeof(nint) => ClrPrimitive.NInt,
        Type t when t == typeof(nuint) => ClrPrimitive.NUInt,
        _ => null
    };

    /// <summary>The CLR primitive <paramref name="definition"/> is, or null: a definition that is not the CLR's own
    /// (a modelled declaration or collection, another platform's type), or a CLR type the table does not name.</summary>
    public static ClrPrimitive? GetClrPrimitive(this ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr ? clr.RuntimeType.GetPrimitiveType() : null;
}