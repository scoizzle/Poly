using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using Action = Poly.DomainModeling.Ontology.Action;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Action parameter plumbing: the bound request parameter and contract-body splice.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// When <paramref name="action"/> is bound to a contract endpoint, splices the library's
    /// contributed body at the bind: the bound `request` parameter, the body's leading statements, then
    /// <c>adapterResult = &lt;final expression&gt;</c> and
    /// <c>if (!adapterResult.IsSuccess) return</c> that Failure (void <c>DomainResult</c>) or
    /// <c>DomainResult&lt;T&gt;.Failure(ErrorMessage)</c> (typed). A surface-only in-solution
    /// contribution erases the call and leaves the local action body untouched. A hand-built
    /// domain's authored name is the fallback when analysis published no resolved endpoint; a recorded non-<c>Bound</c>
    /// binding fact (#422) refuses instead of silently erasing the call.
    /// </summary>
    private static Node? SpliceContractBody(
        Domain domain, Action action, Node? effectsBody, bool isVoid,
        INodeMetadataProvider analysis, ContractSplicePlan? splice, LocalNames? names) {
        if (splice is null) return effectsBody;
        var binding = domain.ContractBindings.FirstOrDefault(b =>
            string.Equals(b.ActionName, action.Name, StringComparison.Ordinal));
        if (binding is null) return effectsBody;
        // #422: an acknowledged bind failure is stored, not hidden. This must precede the
        // contract/endpoint early returns, which would otherwise erase the call silently.
        if (analysis.GetMetadata<ContractBindingMetadata>(binding) is { Status: not ContractBindingStatus.Bound } refused)
            throw new InvalidOperationException(
                $"Contract binding '{binding.Name}' is recorded as '{refused.Status}' and cannot be lowered. " +
                "Resolve the finding (the contract, endpoint, parameter, or pin) or remove the acknowledgement.");
        var contract = domain.ImportedContracts.FirstOrDefault(c =>
            string.Equals(c.Name, binding.ContractName, StringComparison.Ordinal));
        if (contract is null) return effectsBody;
        // #420: analysis recorded the resolved exported identity (an original action name may
        // map to a suffixed endpoint). Splice that identity; the authored text is the fallback
        // when analysis published none.
        var resolvedEndpoint = analysis.GetMetadata<ContractBindingMetadata>(binding)?.Endpoint
            ?? binding.EndpointName;
        var endpoint = contract.Endpoints.FirstOrDefault(e =>
            string.Equals(e.Name, resolvedEndpoint, StringComparison.Ordinal));
        if (endpoint is null) return effectsBody;

        var body = splice.BodyFor(contract, endpoint);
        if (body is null) return effectsBody;

        foreach (var local in body.Variables.OfType<Variable>()) {
            if (action.Parameters.Any(p => string.Equals(p.Name, local.Name, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Contract '{contract.Name}' endpoint '{endpoint.Name}' body local '{local.Name}' collides with a parameter on action '{action.Name}'.");
        }

        var locals = new List<Node>(body.Variables);
        var prefix = new List<Node>();
        if (!string.Equals(binding.LocalParameterName, "request", StringComparison.Ordinal)
            && ReferencesParameter(body, "request")) {
            throw new InvalidOperationException(
                $"Contract '{contract.Name}' endpoint '{endpoint.Name}' body reads 'request', but action '{action.Name}' binds '{binding.LocalParameterName}'. A bound action with a contributed body must name its payload parameter 'request'.");
        }

        var adapterResult = names?.Next("adapterResult") ?? new Variable("adapterResult");
        if (action.Parameters.Any(p => string.Equals(p.Name, adapterResult.Name, StringComparison.Ordinal))
            || locals.OfType<Variable>().Any(v => string.Equals(v.Name, adapterResult.Name, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Contract '{contract.Name}' endpoint '{endpoint.Name}' body collides with splice local '{adapterResult.Name}' on action '{action.Name}'.");

        var final = (Return)body.Nodes[^1];
        prefix.AddRange(body.Nodes.Take(body.Nodes.Count - 1));
        prefix.Add(new Assignment(adapterResult, final.Value!));

        Node failureReturn;
        if (isVoid) {
            failureReturn = new Return(adapterResult);
        }
        else {
            var actionResultType = new NamedTypeReference("DomainResult",
                TypeArguments: [MapDomainTypeRef(action.Result!.Members[0].Type, domain, analysis)]);
            failureReturn = new Return(
                new Invoke(
                    new Member(actionResultType, "Failure"),
                    new Syntactic.Coalesce(
                        new Member(adapterResult, "ErrorMessage"),
                        new Constant(""))));
        }
        prefix.Add(new IfStatement(
            new Syntactic.Not(new Member(adapterResult, "IsSuccess")),
            new Block([failureReturn])));
        locals.Add(adapterResult);

        return effectsBody is Block block
            ? new Block([.. prefix, .. block.Nodes], [.. locals, .. block.Variables])
            : effectsBody is null
                ? new Block(prefix, locals)
                : new Block([.. prefix, effectsBody], locals);
    }

    private static bool ReferencesParameter(Node node, string name) {
        if (node is Parameter parameter && string.Equals(parameter.Name, name, StringComparison.Ordinal))
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ReferencesParameter(child, name))
                return true;
        }
        return false;
    }
}