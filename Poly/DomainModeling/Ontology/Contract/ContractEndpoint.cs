namespace Poly.DomainModeling.Ontology.Contract;

public sealed record ContractEndpoint(
    string Name,
    ContractEndpointKind Kind,
    ContractEndpointDirection Direction,
    DomainTypeReference PayloadType
) : DomainMember(Name);