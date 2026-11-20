using System.Collections.Frozen;

using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// The C# spelling of the CLR's ground types: the keyword the language writes for a type (<c>int</c> for
/// <c>System.Int32</c>), or the short name it writes for one it has no keyword for (<c>DateTime</c>). A fact about C#,
/// so it lives with the reader and writer that use it; the keyword is a name the C# surface binds to a definition, not
/// an id shared with any other platform.
/// </summary>
public static class CSharpTypeNames {
    private static readonly FrozenDictionary<Type, string> Spellings = new Dictionary<Type, string> {
        [typeof(bool)] = "bool",
        [typeof(sbyte)] = "sbyte",
        [typeof(short)] = "short",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(byte)] = "byte",
        [typeof(ushort)] = "ushort",
        [typeof(uint)] = "uint",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(char)] = "char",
        [typeof(object)] = "object",
        [typeof(nint)] = "nint",
        [typeof(nuint)] = "nuint",
        [typeof(byte[])] = "byte[]",
        [typeof(DateTime)] = "DateTime",
        [typeof(DateOnly)] = "DateOnly",
        [typeof(TimeOnly)] = "TimeOnly",
        [typeof(TimeSpan)] = "TimeSpan",
        [typeof(Guid)] = "Guid",
        [typeof(DateTimeOffset)] = "DateTimeOffset",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, Type> Types = new Dictionary<string, Type>(StringComparer.Ordinal) {
        ["bool"] = typeof(bool),
        ["sbyte"] = typeof(sbyte),
        ["short"] = typeof(short),
        ["int"] = typeof(int),
        ["long"] = typeof(long),
        ["byte"] = typeof(byte),
        ["ushort"] = typeof(ushort),
        ["uint"] = typeof(uint),
        ["ulong"] = typeof(ulong),
        ["float"] = typeof(float),
        ["double"] = typeof(double),
        ["decimal"] = typeof(decimal),
        ["string"] = typeof(string),
        ["char"] = typeof(char),
        ["object"] = typeof(object),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The C# spelling of the ground type <paramref name="type"/>, or null when C# spells it by its name.</summary>
    public static string? SpellingOf(Type type) => Spellings.GetValueOrDefault(type);

    /// <summary>The C# spelling of the ground type <paramref name="definition"/> is, or null.</summary>
    public static string? SpellingOf(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr ? SpellingOf(clr.RuntimeType) : null;

    /// <summary>The CLR type the type keyword <paramref name="keyword"/> names, or null when it is not one.</summary>
    public static Type? TypeOf(string keyword) => Types.GetValueOrDefault(keyword);
}