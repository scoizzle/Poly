using System.Collections.Frozen;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// What the CLR says one of its ground types is: the answers <see cref="ClrTypeDefinition"/> gives for its truth,
/// text, number, time, identifier, byte and top types. The CLR host's own table; every other CLR type answers none of
/// these (<c>default</c>).
/// </summary>
internal readonly record struct ClrGround(
    bool IsBoolean = false,
    bool IsText = false,
    bool IsCharacter = false,
    int NumberBits = 0,
    bool IsIntegral = false,
    bool IsSigned = false,
    bool IsDecimal = false,
    bool IsNativeSized = false,
    bool HasDate = false,
    bool HasTime = false,
    bool HasOffset = false,
    bool IsDuration = false,
    bool IsUniqueIdentifier = false,
    bool IsBinary = false,
    bool IsTopType = false) {
    public bool IsNumber => NumberBits > 0;
    public bool IsTemporal => HasDate || HasTime || IsDuration;

    private static readonly FrozenDictionary<Type, ClrGround> Table = new Dictionary<Type, ClrGround> {
        [typeof(bool)] = new(IsBoolean: true),
        [typeof(sbyte)] = new(NumberBits: 8, IsIntegral: true, IsSigned: true),
        [typeof(short)] = new(NumberBits: 16, IsIntegral: true, IsSigned: true),
        [typeof(int)] = new(NumberBits: 32, IsIntegral: true, IsSigned: true),
        [typeof(long)] = new(NumberBits: 64, IsIntegral: true, IsSigned: true),
        [typeof(byte)] = new(NumberBits: 8, IsIntegral: true),
        [typeof(ushort)] = new(NumberBits: 16, IsIntegral: true),
        [typeof(uint)] = new(NumberBits: 32, IsIntegral: true),
        [typeof(ulong)] = new(NumberBits: 64, IsIntegral: true),
        [typeof(float)] = new(NumberBits: 32, IsSigned: true),
        [typeof(double)] = new(NumberBits: 64, IsSigned: true),
        [typeof(decimal)] = new(NumberBits: 128, IsSigned: true, IsDecimal: true),
        [typeof(nint)] = new(NumberBits: IntPtr.Size * 8, IsIntegral: true, IsSigned: true, IsNativeSized: true),
        [typeof(nuint)] = new(NumberBits: IntPtr.Size * 8, IsIntegral: true, IsNativeSized: true),
        [typeof(string)] = new(IsText: true),
        [typeof(char)] = new(IsText: true, IsCharacter: true),
        [typeof(DateOnly)] = new(HasDate: true),
        [typeof(TimeOnly)] = new(HasTime: true),
        [typeof(DateTime)] = new(HasDate: true, HasTime: true),
        [typeof(DateTimeOffset)] = new(HasDate: true, HasTime: true, HasOffset: true),
        [typeof(TimeSpan)] = new(IsDuration: true),
        [typeof(Guid)] = new(IsUniqueIdentifier: true),
        [typeof(byte[])] = new(IsBinary: true),
        [typeof(object)] = new(IsTopType: true),
    }.ToFrozenDictionary();

    /// <summary>True when <paramref name="type"/> is one of the CLR's ground types.</summary>
    public static bool IsGround(Type type) => Table.ContainsKey(type);

    /// <summary>What the CLR says <paramref name="type"/> is, or <c>default</c> when it is not a ground type.</summary>
    public static ClrGround Of(Type type) => Table.GetValueOrDefault(type);
}