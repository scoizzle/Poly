using Poly.Introspection;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// The ground meanings the domain language has a rule for. This is the domain's own closed list (the DSL's built-in
/// types are closed), read from the facts a platform states about a definition; it is not a list of what a platform
/// may have. A ground type the domain has no rule for (a character, a narrow or unsigned integer, a timestamp with an
/// offset, the top type) has no domain ground.
/// </summary>
internal enum DomainGround { Text, Int32, Int64, Float32, Float64, Decimal, Boolean, Date, DateTime, Time, Duration, Guid, Binary }

internal static class DomainGrounds {
    /// <summary>The domain ground <paramref name="definition"/> is, or null when the domain has no rule for it.</summary>
    public static DomainGround? Of(ITypeDefinition? definition) {
        if (definition is null)
            return null;
        if (definition.GetFact<Numeric>() is { } number) {
            return number switch {
                { IsDecimal: true } => DomainGround.Decimal,
                { IsIntegral: true, IsSigned: true, IsNativeSized: false, Bits: 32 } => DomainGround.Int32,
                { IsIntegral: true, IsSigned: true, IsNativeSized: false, Bits: 64 } => DomainGround.Int64,
                { IsIntegral: false, Bits: 32 } => DomainGround.Float32,
                { IsIntegral: false, Bits: 64 } => DomainGround.Float64,
                _ => null
            };
        }
        if (definition.GetFact<Text>() is { } text)
            return text.IsSingleCharacter ? null : DomainGround.Text;
        if (definition.GetFact<Truth>() is not null)
            return DomainGround.Boolean;
        if (definition.GetFact<Temporal>() is { } time) {
            return time switch {
                { IsDuration: true } => DomainGround.Duration,
                { HasOffset: true } => null,
                { HasDate: true, HasTime: true } => DomainGround.DateTime,
                { HasDate: true } => DomainGround.Date,
                { HasTime: true } => DomainGround.Time,
                _ => null
            };
        }
        if (definition.GetFact<UniqueIdentifier>() is not null)
            return DomainGround.Guid;
        return definition.GetFact<Binary>() is not null ? DomainGround.Binary : null;
    }
}