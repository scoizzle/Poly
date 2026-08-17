namespace Poly.DomainModeling;

public sealed record ContractBinding(
    string Name,
    string ContractName,
    string EndpointName,
    string ActionName,
    string LocalParameterName,
    IReadOnlyList<ContractFieldMap> FieldMaps
) : DomainMember(Name);