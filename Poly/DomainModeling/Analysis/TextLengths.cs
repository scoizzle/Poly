using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The length interval of a Text-valued expression (#1040): what a <c>length(...)</c> write is decided
/// against. A Text carries its declared <c>length(...)</c>; a Text with no such constraint is
/// <c>[0, 2147483647]</c>, and that upper bound is exact rather than a guess — a string's length is an
/// <see cref="int"/>. <c>+</c> is concatenation, so it adds the two intervals, and a <c>match</c> hulls
/// its arms. Anything else is <c>null</c> (unknown): a value that is not Text, a Text the walk cannot
/// name (a relationship leaf, a call), or a match with an arm it cannot bound.
/// <para>
/// The walk reads DECLARED constraints, in the scope the expression is written in. Its lower bound is
/// what the runtime guarantees: a member that can be absent contributes 0, because C# string
/// concatenation treats a null operand as <c>""</c> (<c>A + "!"</c> with a null <c>A</c> is
/// <c>"!"</c>) and the runtime length guard applies to a present value only. A declared minimum is
/// therefore used only for a required member read inside an action body.
/// </para>
/// </summary>
internal static class TextLengths {
    /// <summary>A length is an <see cref="int"/>: this is the exact upper bound, not an infinity, and it
    /// is also how a declared <c>length(0, 2147483647)</c> is written.</summary>
    private const decimal MaxLength = int.MaxValue;

    /// <summary>The length interval of <paramref name="expression"/>, or null when it is not a bounded Text.</summary>
    internal static ValueRange? Of(DomainExpression expression, Entity entity, Action? action, Domain domain) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(domain);
        // The same type system the emitted guards ask, so "is this Text" cannot disagree with them.
        return Of(expression, entity, action, RuntimeAnalysisCache.Session(domain).TargetTypeSystem);
    }

    private static ValueRange? Of(
        DomainExpression expression, Entity entity, Action? action, Poly.Introspection.ITypeSystem system) =>
        expression switch {
            Literal { Value: string text } => new ValueRange(text.Length, text.Length),
            PropertyAccess property => Declared(
                entity.Properties.FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal)),
                system, action),
            ParameterAccess parameter => Declared(
                action?.Parameters.FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.Ordinal)),
                system, action),
            Add concatenation => Concat(
                Of(concatenation.Left, entity, action, system), Of(concatenation.Right, entity, action, system)),
            MatchExpr match => Hull(match, entity, action, system),
            _ => null,
        };

    /// <summary>A Text member's declared length, else <c>[0, int.MaxValue]</c>; null for a member that is
    /// absent or is not a Text type (a length is not one of its constraints). The lower bound is the
    /// declared minimum only for a required member in an action body — the one case the runtime
    /// guarantees it is present.</summary>
    private static ValueRange? Declared(Property? property, Poly.Introspection.ITypeSystem system, Action? action) {
        if (property is null || !system.LengthApplies(property.Type.TypeName)) return null;
        var declared = property.Constraints.OfType<LengthConstraint>().FirstOrDefault();
        var present = action is not null && EntityStructureAnalyzer.IsRequired(property);
        return new ValueRange(present ? declared?.MinLength ?? 0 : 0, declared?.MaxLength ?? MaxLength);
    }

    /// <summary>The concatenation of two known Texts, or unknown when either side is not one.</summary>
    private static ValueRange? Concat(ValueRange? left, ValueRange? right) {
        if (left is not { Min: { } leftMin, Max: { } leftMax }
            || right is not { Min: { } rightMin, Max: { } rightMax })
            return null;
        return new ValueRange(
            Math.Min(leftMin + rightMin, MaxLength),
            Math.Min(leftMax + rightMax, MaxLength));
    }

    /// <summary>The hull of the match's arm lengths and its required <c>else</c>; unknown when any of them
    /// has none.</summary>
    private static ValueRange? Hull(
        MatchExpr match, Entity entity, Action? action, Poly.Introspection.ITypeSystem system) {
        ValueRange? hull = null;
        foreach (var arm in match.Arms.Select(a => a.Value).Append(match.Else)) {
            if (Of(arm, entity, action, system) is not { } length) return null;
            hull = hull is null ? length : hull.Join(length);
            if (hull is null) return null;
        }
        return hull;
    }
}