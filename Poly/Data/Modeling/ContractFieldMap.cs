using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record ContractFieldMap : DomainMember {
    public ContractFieldMap(Domain domain, string name, string remoteFieldName, string localFieldName) : base(domain, name) {
        RemoteFieldName = remoteFieldName;
        LocalFieldName = localFieldName;
    }

    public string RemoteFieldName { get; private set; } = string.Empty;
    public string LocalFieldName { get; private set; } = string.Empty;
}