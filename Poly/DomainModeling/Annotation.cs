namespace Poly.DomainModeling;

/// <summary>
/// A portable annotation/facet stored as a keyword name + closed-value arguments.
/// Vendor packs produce and consume <c>Annotation</c> records; a pack may desugar
/// vendor-specific syntax (e.g. <c>ora(…)</c>) to an <c>Annotation("column", …)</c>
/// during parsing.
/// </summary>
/// <param name="Name">The annotation keyword, e.g. <c>"column"</c>, <c>"table"</c>, <c>"json"</c>.</param>
/// <param name="Arguments">Positional or named arguments. Keys are argument names
/// (positional: <c>"0"</c>, <c>"1"</c>, …; named: the argument name). Values
/// are closed <see cref="AnnotationValue"/> literals only.</param>
public sealed record Annotation(
    string Name,
    IReadOnlyDictionary<string, AnnotationValue> Arguments
) : Facet {
    // Dictionary equality is reference-based by default; annotations must compare by content.
    public bool Equals(Annotation? other) {
        if (other is null)
            return false;
        if (!string.Equals(Name, other.Name, StringComparison.Ordinal))
            return false;
        if (Arguments.Count != other.Arguments.Count)
            return false;
        foreach (var (key, value) in Arguments) {
            if (!other.Arguments.TryGetValue(key, out var otherValue) || !Equals(value, otherValue))
                return false;
        }
        return true;
    }

    public override int GetHashCode() {
        var hc = new HashCode();
        hc.Add(Name, StringComparer.Ordinal);
        foreach (var key in Arguments.Keys.OrderBy(k => k, StringComparer.Ordinal)) {
            hc.Add(key, StringComparer.Ordinal);
            hc.Add(Arguments[key]);
        }
        return hc.ToHashCode();
    }
}