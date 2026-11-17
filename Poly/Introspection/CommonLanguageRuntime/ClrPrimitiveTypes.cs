namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR's runtime types for the primitive vocabulary, both directions. A platform fact, so it lives here and not on
/// <see cref="PrimitiveType"/>: another platform's primitive may have no CLR type, and resolving a primitive id is
/// <see cref="ITypeDefinitionProvider.GetTypeDefinition(PrimitiveType)"/>'s job, never the root's default.
/// </summary>
internal static class ClrPrimitiveTypes {
    /// <summary>The CLR type <paramref name="id"/> denotes, or null when it names no CLR type.</summary>
    public static Type? GetClrType(this PrimitiveType id) => id switch {
        PrimitiveType.Boolean => typeof(bool),
        PrimitiveType.Int8 => typeof(sbyte),
        PrimitiveType.Int16 => typeof(short),
        PrimitiveType.Int32 => typeof(int),
        PrimitiveType.Int64 => typeof(long),
        PrimitiveType.UInt8 => typeof(byte),
        PrimitiveType.UInt16 => typeof(ushort),
        PrimitiveType.UInt32 => typeof(uint),
        PrimitiveType.UInt64 => typeof(ulong),
        PrimitiveType.Float32 => typeof(float),
        PrimitiveType.Float64 => typeof(double),
        PrimitiveType.Decimal => typeof(decimal),
        PrimitiveType.String => typeof(string),
        PrimitiveType.Char => typeof(char),
        PrimitiveType.DateTime => typeof(DateTime),
        PrimitiveType.DateOnly => typeof(DateOnly),
        PrimitiveType.TimeOnly => typeof(TimeOnly),
        PrimitiveType.TimeSpan => typeof(TimeSpan),
        PrimitiveType.Guid => typeof(Guid),
        PrimitiveType.ByteArray => typeof(byte[]),
        PrimitiveType.Structure => typeof(object),
        PrimitiveType.NInt => typeof(nint),
        PrimitiveType.NUInt => typeof(nuint),
        _ => null
    };

    /// <summary>The primitive <paramref name="type"/> denotes, or null when it is not a CLR primitive.</summary>
    public static PrimitiveType? GetPrimitiveType(this Type type) => type switch {
        Type t when t == typeof(bool) => PrimitiveType.Boolean,
        Type t when t == typeof(sbyte) => PrimitiveType.Int8,
        Type t when t == typeof(short) => PrimitiveType.Int16,
        Type t when t == typeof(int) => PrimitiveType.Int32,
        Type t when t == typeof(long) => PrimitiveType.Int64,
        Type t when t == typeof(byte) => PrimitiveType.UInt8,
        Type t when t == typeof(ushort) => PrimitiveType.UInt16,
        Type t when t == typeof(uint) => PrimitiveType.UInt32,
        Type t when t == typeof(ulong) => PrimitiveType.UInt64,
        Type t when t == typeof(float) => PrimitiveType.Float32,
        Type t when t == typeof(double) => PrimitiveType.Float64,
        Type t when t == typeof(decimal) => PrimitiveType.Decimal,
        Type t when t == typeof(string) => PrimitiveType.String,
        Type t when t == typeof(char) => PrimitiveType.Char,
        Type t when t == typeof(DateTime) => PrimitiveType.DateTime,
        Type t when t == typeof(DateOnly) => PrimitiveType.DateOnly,
        Type t when t == typeof(TimeOnly) => PrimitiveType.TimeOnly,
        Type t when t == typeof(TimeSpan) => PrimitiveType.TimeSpan,
        Type t when t == typeof(Guid) => PrimitiveType.Guid,
        Type t when t == typeof(byte[]) => PrimitiveType.ByteArray,
        Type t when t == typeof(object) => PrimitiveType.Structure,
        Type t when t == typeof(nint) => PrimitiveType.NInt,
        Type t when t == typeof(nuint) => PrimitiveType.NUInt,
        _ => null
    };
}