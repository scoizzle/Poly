namespace Poly.DomainModeling.Ontology.Contract;

public sealed record ContractEndpoint(
    string Name,
    ContractEndpointKind Kind,
    ContractEndpointDirection Direction,
    DomainTypeReference PayloadType
) : DomainMember(Name) {
    /// <summary>
    /// The action name in the source domain. Equals <see cref="DomainMember.Name"/> unless
    /// the action name is overloaded, where the exported <c>Name</c> carries a stable suffix
    /// (<c>Charge__Money</c>) and this keeps the original (<c>Charge</c>). The surface
    /// fingerprint is keyed by this name plus the payload, so a suffix spelling change is
    /// not a surface change.
    /// </summary>
    public string? SourceName { get; init; }

    /// <summary>
    /// Public preconditions on the exported action (decision 21): the action's own
    /// <c>require</c> policies, plus its stage's policies for a stage action. Surface meaning,
    /// not implementation AST, and deliberately not in <c>Children</c>: a consumer analyzer
    /// must not walk the source domain's expressions.
    /// </summary>
    public IReadOnlyList<Policy> Preconditions { get; init; } = [];
}