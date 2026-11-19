using Poly.Introspection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR host's identity facts about its own built-in types: which definitions are its calendar types, its timestamp,
/// its Guid, its <c>byte[]</c>, and the ground types its table names. Each is stated by runtime-type identity on the
/// CLR's own definitions; C#'s conversions and operators over those types are the C# type system's
/// (<see cref="Poly.Interpretation.CSharp.CSharpBuiltIns"/>, #998 T10b). A fact of the CLR host, not vocabulary: the
/// neutral contract names no ground type, and another platform states its own or none.
/// </summary>
internal static class ClrBuiltInRules {
    /// <summary>The CLR host's own table of ground types: the types it treats as built in and defines no member for.</summary>
    private static readonly HashSet<Type> PrimitiveTypes = [
        typeof(bool), typeof(sbyte), typeof(short), typeof(int), typeof(long), typeof(byte), typeof(ushort), typeof(uint),
        typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(char), typeof(DateTime),
        typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(Guid), typeof(DateTimeOffset), typeof(byte[]),
        typeof(object), typeof(nint), typeof(nuint),
    ];

    /// <summary>The CLR's own calendar types (DateTime, DateOnly, TimeOnly, DateTimeOffset, TimeSpan): the compatibility
    /// analyzer's Date category, which matches the CLR's definitions (owner ruling 2026-11-19; its removing row is I8b,
    /// #972). Only the CLR's own definitions answer.</summary>
    public static bool IsCalendarType(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr
        && (clr.RuntimeType == typeof(DateTime) || clr.RuntimeType == typeof(DateOnly) || clr.RuntimeType == typeof(TimeOnly)
            || clr.RuntimeType == typeof(DateTimeOffset) || clr.RuntimeType == typeof(TimeSpan));

    /// <summary>A timestamp: exactly the CLR's DateTime or DateTimeOffset.</summary>
    public static bool IsTimestamp(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr && (clr.RuntimeType == typeof(DateTime) || clr.RuntimeType == typeof(DateTimeOffset));

    /// <summary>The CLR's Guid: the compatibility analyzer's Guid category (same ruling and removing row).</summary>
    public static bool IsGuid(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr && clr.RuntimeType == typeof(Guid);

    /// <summary>The CLR's byte[] as a value: the product pack's scalar (same ruling).</summary>
    public static bool IsByteArray(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr && clr.RuntimeType == typeof(byte[]);

    /// <summary>One of the CLR host's own primitive types (its table: bool through byte[], and object), by identity.</summary>
    public static bool IsPrimitive(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr && PrimitiveTypes.Contains(clr.RuntimeType);
}