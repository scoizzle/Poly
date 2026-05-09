using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record ImportedContract : DomainMember {
    internal readonly List<ContractEndpoint> _endpoints = [];

    public ImportedContract(
        Domain domain,
        string name,
        ContractSourceKind sourceKind,
        string sourceIdentifier,
        string version
    ) : base(domain, name) {
        SourceKind = sourceKind;
        SourceIdentifier = sourceIdentifier;
        Version = version;
    }

    public ContractSourceKind SourceKind { get; private set; }
    public string SourceIdentifier { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public IReadOnlyCollection<ContractEndpoint> Endpoints => _endpoints.AsReadOnly();
    public sealed override IEnumerable<DomainObject> ChildObjects => [.. _endpoints];
}