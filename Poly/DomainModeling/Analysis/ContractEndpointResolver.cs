using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>One bind's endpoint resolution (see <see cref="ContractEndpointResolver"/>).</summary>
internal sealed record ContractEndpointResolution(
    ContractEndpoint? Endpoint,
    ContractBindingStatus Status,
    IReadOnlyList<ContractEndpoint> Candidates,
    bool RequiredChoice);

/// <summary>
/// #420 (epic #427, spike #413 decision 5): resolve a bind's authored endpoint text to one
/// exported endpoint identity. An exported <c>Name</c> wins outright; otherwise the original
/// action name (<see cref="ContractEndpoint.SourceName"/>) matches, and the payload relation
/// picks the more-constrained accepting signature. An incomparable pair is refused, never
/// guessed.
/// </summary>
internal static class ContractEndpointResolver {
    public static ContractEndpointResolution Resolve(
        ImportedContract contract,
        Domain domain,
        string authoredEndpoint,
        string localParameterTypeName) {
        var exact = contract.Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Name, authoredEndpoint, StringComparison.Ordinal));
        if (exact is not null) {
            return ContractPayloadRelation.Accepts(
                contract, domain, localParameterTypeName, exact.PayloadType.TypeName)
                ? new ContractEndpointResolution(exact, ContractBindingStatus.Bound, [exact], RequiredChoice: false)
                : new ContractEndpointResolution(null, ContractBindingStatus.Incompatible, [exact], RequiredChoice: false);
        }

        var candidates = contract.Endpoints
            .Where(endpoint => string.Equals(
                endpoint.SourceName ?? endpoint.Name, authoredEndpoint, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0)
            return new ContractEndpointResolution(null, ContractBindingStatus.NotOnSurface, [], RequiredChoice: false);

        var accepting = candidates
            .Where(endpoint => ContractPayloadRelation.Accepts(
                contract, domain, localParameterTypeName, endpoint.PayloadType.TypeName))
            .ToList();
        if (accepting.Count == 0)
            return new ContractEndpointResolution(null, ContractBindingStatus.Incompatible, candidates, RequiredChoice: false);
        if (accepting.Count == 1)
            return new ContractEndpointResolution(accepting[0], ContractBindingStatus.Bound, candidates, RequiredChoice: false);

        var winners = accepting
            .Where(candidate => accepting.All(other =>
                ReferenceEquals(other, candidate) || IsNarrower(contract, domain, candidate, other)))
            .ToList();
        return winners.Count == 1
            ? new ContractEndpointResolution(winners[0], ContractBindingStatus.Bound, candidates, RequiredChoice: true)
            : new ContractEndpointResolution(null, ContractBindingStatus.Ambiguous, candidates, RequiredChoice: false);
    }

    /// <summary>
    /// <paramref name="candidate"/>'s payload sits inside <paramref name="other"/>'s. Mutual
    /// subsumption leaves two winners, which the caller treats as ambiguous: equal payloads
    /// do not give a preference.
    /// </summary>
    private static bool IsNarrower(
        ImportedContract contract,
        Domain domain,
        ContractEndpoint candidate,
        ContractEndpoint other) {
        var narrower = FindValueType(contract, domain, candidate.PayloadType.TypeName);
        var wider = FindValueType(contract, domain, other.PayloadType.TypeName);
        return narrower is not null
            && wider is not null
            && ContractPayloadRelation.Subsumed(narrower, wider);
    }

    private static ValueType? FindValueType(
        ImportedContract contract,
        Domain domain,
        string name) =>
        contract.Types.FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal))
        ?? domain.Types.OfType<ValueType>()
            .FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal));
}