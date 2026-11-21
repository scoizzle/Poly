using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// A property's default as an artifact can carry it: a literal value, an enum member, or nothing. Every exporter
/// reads this one rule, so none can call a default static that another omits. A runtime default (<c>Now</c>,
/// <c>Today</c>, <c>Guid</c>) and a <c>null</c> literal have no static value, and are omitted rather than guessed
/// (JSON Schema's <c>default</c>, SHACL's <c>x-poly-default</c>).
/// </summary>
internal static class DefaultValueFacts {
    /// <summary>Reads <paramref name="constraint"/> against the property's own type. <paramref name="literal"/> is
    /// the CLR value a literal default names (a <see cref="long"/> for a Number, a <see cref="bool"/>, a
    /// <see cref="string"/>, …); <paramref name="enumMember"/> is the member name of a <c>default(Member)</c> on an
    /// enum-typed property. False means the default has no static value.</summary>
    internal static bool TryRead(
        DefaultValueConstraint constraint,
        Property property,
        IReadOnlyDictionary<string, DomainType> byName,
        out object? literal,
        out string? enumMember) {
        literal = null;
        enumMember = null;
        switch (constraint.Expression) {
            case Literal { Value: { } value }:
                literal = value;
                return true;
            case PropertyAccess access when byName.TryGetValue(property.Type.TypeName, out var declared)
                && declared is EnumType enumType
                && enumType.MemberNames.Contains(access.Name, StringComparer.Ordinal):
                enumMember = access.Name;
                return true;
            default:
                return false;
        }
    }
}