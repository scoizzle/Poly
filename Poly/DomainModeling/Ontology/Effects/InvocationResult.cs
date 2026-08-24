
namespace Poly.DomainModeling.Ontology.Effects;

/// <summary>
/// Describes the output(s) produced by an <see cref="Action"/> or <see cref="Effect"/>.
/// </summary>
public sealed record InvocationResult(IReadOnlyList<InvocationResult.Member> Members) : DomainObject {
    public static readonly InvocationResult Void = new([]);

    public sealed record Member(string Name, DomainTypeReference Type, IReadOnlyList<Constraint> Constraints) : DomainObject {
        public override IEnumerable<Node?> Children => [.. Constraints];
    }

    public override IEnumerable<Node?> Children => Members;
}