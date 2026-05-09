using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record ContractEndpoint : DomainMember {
    public ContractEndpoint(
        Domain domain,
        string name,
        ContractEndpointKind kind,
        ContractEndpointDirection direction,
        DomainType payloadType
    ) : base(domain, name) {
        Kind = kind;
        Direction = direction;
        PayloadType = payloadType;
    }

    public ContractEndpointKind Kind { get; private set; }
    public ContractEndpointDirection Direction { get; private set; }
    public DomainType PayloadType { get; private set; }
}