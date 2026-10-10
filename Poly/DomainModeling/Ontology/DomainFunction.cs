namespace Poly.DomainModeling.Ontology;

/// <summary>
/// A named function on a <see cref="Domain"/>: parameters, a return type, and a single expression body.
/// </summary>
public sealed record DomainFunction(
    string Name,
    IReadOnlyList<Property> Parameters,
    DomainTypeReference ReturnType,
    DomainExpression Body
) : DomainMember(Name) {
    public sealed override IEnumerable<Node?> Children => [.. Parameters, ReturnType, Body];
}
