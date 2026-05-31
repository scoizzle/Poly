namespace Poly.DomainModeling.Constraints;

public sealed record EnumConstraint(IReadOnlyList<EnumConstraint.Member> Members) : Constraint {
    public sealed record Member(string Name, object? CanonicalValue = null, string? Label = null) : DomainObject {
        public object EffectiveCanonicalValue => CanonicalValue ?? Name;
    }
}