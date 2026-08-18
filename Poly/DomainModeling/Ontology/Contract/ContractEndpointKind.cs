namespace Poly.DomainModeling.Ontology.Contract;

public enum ContractEndpointKind {
    Operation,
    Event
}

public enum ContractEndpointDirection {
    Inbound,
    Outbound
}

public enum ContractSourceKind {
    InternalDomain,
    ExternalProvider
}