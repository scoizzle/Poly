namespace Poly.Data.Modeling;

public enum ContractSourceKind {
    InternalDomain,
    ExternalProvider
}

public enum ContractEndpointKind {
    Operation,
    Event
}

public enum ContractEndpointDirection {
    Inbound,
    Outbound
}