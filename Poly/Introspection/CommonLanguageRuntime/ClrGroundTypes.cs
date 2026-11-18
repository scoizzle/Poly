using System.Collections.Frozen;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR's ground types and the facts the CLR states about each: its numbers and their representation, truth, text,
/// time, identifiers, bytes and the top type. This is the CLR host's own table; the neutral contract names none of
/// these types, and a consumer asks a definition for a fact (<see cref="ITypeDefinition.GetFact{TFact}"/>).
/// </summary>
internal static class ClrGroundTypes {
    private static readonly FrozenDictionary<Type, TypeFact[]> Facts = new Dictionary<Type, TypeFact[]> {
        [typeof(bool)] = [Truth.Instance],
        [typeof(sbyte)] = [new Numeric(IsIntegral: true, IsSigned: true, Bits: 8)],
        [typeof(short)] = [new Numeric(true, true, 16)],
        [typeof(int)] = [new Numeric(true, true, 32)],
        [typeof(long)] = [new Numeric(true, true, 64)],
        [typeof(byte)] = [new Numeric(true, false, 8)],
        [typeof(ushort)] = [new Numeric(true, false, 16)],
        [typeof(uint)] = [new Numeric(true, false, 32)],
        [typeof(ulong)] = [new Numeric(true, false, 64)],
        [typeof(float)] = [new Numeric(false, true, 32)],
        [typeof(double)] = [new Numeric(false, true, 64)],
        [typeof(decimal)] = [new Numeric(false, true, 128, IsDecimal: true)],
        [typeof(nint)] = [new Numeric(true, true, IntPtr.Size * 8, IsNativeSized: true)],
        [typeof(nuint)] = [new Numeric(true, false, IntPtr.Size * 8, IsNativeSized: true)],
        [typeof(string)] = [new Text(IsSingleCharacter: false)],
        [typeof(char)] = [new Text(IsSingleCharacter: true)],
        [typeof(DateOnly)] = [new Temporal(HasDate: true, HasTime: false)],
        [typeof(TimeOnly)] = [new Temporal(HasDate: false, HasTime: true)],
        [typeof(DateTime)] = [new Temporal(HasDate: true, HasTime: true)],
        [typeof(DateTimeOffset)] = [new Temporal(HasDate: true, HasTime: true, HasOffset: true)],
        [typeof(TimeSpan)] = [new Temporal(HasDate: false, HasTime: false, IsDuration: true)],
        [typeof(Guid)] = [UniqueIdentifier.Instance],
        [typeof(byte[])] = [Binary.Instance],
        [typeof(object)] = [TopType.Instance],
    }.ToFrozenDictionary();

    /// <summary>True when <paramref name="type"/> is one of the CLR's ground types.</summary>
    public static bool IsGround(Type type) => Facts.ContainsKey(type);

    /// <summary>The fact of type <typeparamref name="TFact"/> the CLR states about the ground type
    /// <paramref name="type"/>, or null.</summary>
    public static TFact? Fact<TFact>(Type type) where TFact : TypeFact {
        if (!Facts.TryGetValue(type, out var facts))
            return null;
        if (typeof(TFact) == typeof(Atomic))
            return Atomic.Instance as TFact;
        foreach (var fact in facts) {
            if (fact is TFact stated)
                return stated;
        }
        return null;
    }
}