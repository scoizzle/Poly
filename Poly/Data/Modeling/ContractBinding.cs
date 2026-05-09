using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record ContractBinding : DomainMember {
    internal readonly List<ContractFieldMap> _fieldMaps = [];

    public ContractBinding(
        Domain domain,
        string name,
        ImportedContract contract,
        ContractEndpoint endpoint,
        Action localAction,
        string localParameterName
    ) : base(domain, name) {
        Contract = contract;
        Endpoint = endpoint;
        LocalAction = localAction;
        LocalParameterName = localParameterName;
    }

    public ImportedContract Contract { get; private set; }
    public ContractEndpoint Endpoint { get; private set; }
    public Action LocalAction { get; private set; }
    public string LocalParameterName { get; private set; } = string.Empty;
    public IReadOnlyCollection<ContractFieldMap> FieldMaps => _fieldMaps.AsReadOnly();
    public sealed override IEnumerable<DomainObject> ChildObjects => [.. _fieldMaps];
}