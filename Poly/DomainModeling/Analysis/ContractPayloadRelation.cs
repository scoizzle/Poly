using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Contract;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// #420: whether a bound parameter's value type can be passed to an endpoint's payload type.
/// Identity is a name match. Refinement is constraint subsumption over value types with the
/// same property shape: the caller's constraints must sit inside the payload's, never outside
/// it (handing a wider value to a narrower payload is unsound). Subsumption is the existing
/// <see cref="ConstraintMerge"/> intersection rule; it is not replaced by name equality.
/// </summary>
internal static class ContractPayloadRelation {
    public static bool Accepts(
        ImportedContract contract,
        Domain domain,
        string localTypeName,
        string payloadTypeName) {
        if (string.Equals(localTypeName, payloadTypeName, StringComparison.Ordinal))
            return true;
        if (FindValueType(contract, domain, localTypeName) is not { } local)
            return false;
        if (FindValueType(contract, domain, payloadTypeName) is not { } payload)
            return false;
        return Subsumed(local, payload);
    }

    /// <summary>
    /// #425: whether the two named types have the same property shape (same names and types),
    /// ignoring their constraints. A boundary check can only compare constraints when the
    /// shapes match; a shape mismatch is an <c>Incompatible</c> bind, not an unknown one.
    /// </summary>
    public static bool SameShape(
        ImportedContract contract,
        Domain domain,
        string localTypeName,
        string payloadTypeName) {
        if (string.Equals(localTypeName, payloadTypeName, StringComparison.Ordinal))
            return true;
        if (FindValueType(contract, domain, localTypeName) is not { } local)
            return false;
        if (FindValueType(contract, domain, payloadTypeName) is not { } payload)
            return false;
        return SameShape(local, payload);
    }

    private static bool SameShape(ValueType local, ValueType payload) {
        if (local.Properties.Count != payload.Properties.Count)
            return false;
        foreach (var property in payload.Properties) {
            var candidate = local.Properties.FirstOrDefault(member =>
                string.Equals(member.Name, property.Name, StringComparison.Ordinal));
            if (candidate is null
                || !string.Equals(candidate.Type.TypeName, property.Type.TypeName, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when every value of <paramref name="narrower"/> is a value of
    /// <paramref name="wider"/>: same property shape and every wider constraint implied by a
    /// narrower one.
    /// </summary>
    public static bool Subsumed(ValueType narrower, ValueType wider) {
        if (narrower.Properties.Count != wider.Properties.Count)
            return false;
        if (!SameSet(narrower.Constraints, wider.Constraints))
            return false;

        foreach (var wideProperty in wider.Properties) {
            var narrowProperty = narrower.Properties.FirstOrDefault(property =>
                string.Equals(property.Name, wideProperty.Name, StringComparison.Ordinal));
            if (narrowProperty is null)
                return false;
            if (!string.Equals(
                    narrowProperty.Type.TypeName, wideProperty.Type.TypeName, StringComparison.Ordinal))
                return false;
            if (!Dominates(narrowProperty.Constraints, wideProperty.Constraints))
                return false;
        }
        return true;
    }

    private static ValueType? FindValueType(ImportedContract contract, Domain domain, string name) =>
        contract.Types.FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal))
        ?? domain.Types.OfType<ValueType>()
            .FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal));

    private static bool Dominates(
        IReadOnlyList<Constraint> narrower,
        IReadOnlyList<Constraint> wider) {
        foreach (var outer in wider) {
            if (!narrower.Any(inner => Implies(inner, outer)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// <paramref name="inner"/> implies <paramref name="outer"/> when their intersection is
    /// <paramref name="inner"/> itself. Different constraint kinds are never implied, and a
    /// merge that is unsatisfiable returns null. Comparison is the canonical printed text:
    /// a constraint is a <see cref="Node"/>, so its id makes record equality never hold
    /// across a fresh merge.
    /// </summary>
    private static bool Implies(Constraint inner, Constraint outer) {
        var innerText = Printer.PrintConstraintText(inner);
        if (string.Equals(innerText, Printer.PrintConstraintText(outer), StringComparison.Ordinal))
            return true;
        var merged = inner.Merge(outer);
        return merged is not null
            && string.Equals(Printer.PrintConstraintText(merged), innerText, StringComparison.Ordinal);
    }

    private static bool SameSet(IReadOnlyList<Constraint> left, IReadOnlyList<Constraint> right) =>
        left.Count == right.Count
        && Canonical(left).SequenceEqual(Canonical(right), StringComparer.Ordinal);

    private static IEnumerable<string> Canonical(IReadOnlyList<Constraint> constraints) =>
        constraints.Select(Printer.PrintConstraintText).OrderBy(text => text, StringComparer.Ordinal);

    private static readonly DomainDslPrinter Printer = new();
}