using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology.Contract;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// One lowering's contract-body state: the contributions this session carries and the
/// node ids already spliced. A library body is a factory called once per splice site, so a
/// well-behaved library returns fresh nodes; a body reused across binds would put one node
/// id in two methods and corrupt the whole-module VM metadata, so it fails closed here.
/// </summary>
internal sealed class ContractSplicePlan {
    private readonly IReadOnlyList<ContractContribution> _contributions;
    private readonly HashSet<NodeId> _spliced = [];

    public ContractSplicePlan(IReadOnlyList<ContractContribution>? contributions) =>
        _contributions = contributions ?? [];

    /// <summary>
    /// The body for a bound endpoint, or <c>null</c> when an in-solution surface-only
    /// bind is erased.
    /// </summary>
    public Block? BodyFor(ImportedContract contract, ContractEndpoint endpoint) {
        var contribution = contract.UsesId is null
            ? null
            : _contributions.FirstOrDefault(candidate =>
                string.Equals(candidate.LibraryId, contract.UsesId, StringComparison.Ordinal));

        if (contract.UsesId is not null && contribution is null) {
            throw new InvalidOperationException(
                $"Contract '{contract.Name}' was seeded by `uses {contract.UsesId}`, but that library contributed no contract to this lowering.");
        }

        if (contribution is null) {
            return contract.SourceKind == ContractSourceKind.InternalDomain
                ? null
                : throw new InvalidOperationException(
                    $"Contract '{contract.Name}' endpoint '{endpoint.Name}' is external and has no implementation body. " +
                    $"A library must contribute the body; a surface-only contribution is legal only for an in-solution contract.");
        }

        if (!contribution.Bodies.TryGetValue(endpoint.Name, out var factory)) {
            return contract.SourceKind == ContractSourceKind.InternalDomain
                ? null
                : throw new InvalidOperationException(
                    $"Contract '{contract.Name}' endpoint '{endpoint.Name}' is external and library '{contribution.LibraryId}' contributed no implementation body.");
        }

        var body = factory()
            ?? throw new InvalidOperationException(
                $"Contract '{contract.Name}' endpoint '{endpoint.Name}' body factory returned null.");

        ValidateShape(contract, endpoint, body);
        RegisterFresh(contract, endpoint, body);
        return body;
    }

    private static void ValidateShape(ImportedContract contract, ContractEndpoint endpoint, Block body) {
        if (body.Nodes.Count == 0 || body.Nodes[^1] is not Return { Value: not null }) {
            throw new InvalidOperationException(
                $"Contract '{contract.Name}' endpoint '{endpoint.Name}' body must end in `return <DomainResult>;`.");
        }

        for (var i = 0; i < body.Nodes.Count - 1; i++) {
            if (ContainsReturn(body.Nodes[i])) {
                throw new InvalidOperationException(
                    $"Contract '{contract.Name}' endpoint '{endpoint.Name}' body returns before its final expression. " +
                    "Express an early failure as the final Conditional(condition, Failure, Success).");
            }
        }
    }

    // A lambda's return belongs to the lambda, not to the bound action.
    private static bool ContainsReturn(Node node) {
        if (node is Return)
            return true;
        if (node is Lambda)
            return false;
        foreach (var child in node.Children) {
            if (child is not null && ContainsReturn(child))
                return true;
        }
        return false;
    }

    private void RegisterFresh(ImportedContract contract, ContractEndpoint endpoint, Block body) {
        foreach (var node in Walk(body)) {
            if (!_spliced.Add(node.Id)) {
                throw new InvalidOperationException(
                    $"Contract '{contract.Name}' endpoint '{endpoint.Name}' body factory returned a node already spliced in this lowering. " +
                    "A body factory must build fresh nodes for every bind.");
            }
        }
    }

    private static IEnumerable<Node> Walk(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var descendant in Walk(child))
                yield return descendant;
        }
    }
}