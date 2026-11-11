using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// #426 slice 1: an expression call that names an imported contract endpoint resolves, or
/// fails, the way a bind does. The call's name is the endpoint's authored name (the call
/// grammar has no contract qualifier), its single argument is the payload, and the result is
/// recorded as a <see cref="ContractCallMetadata"/> on the call node. The pin, boundary and
/// status classification is <see cref="ContractIntegrationAnalyzer.ClassifyResolution"/>, shared
/// with the bind path. Analysis only: nothing here lowers, and the call's value type stays
/// unknown (that is a pending decision, not this pass's).
/// </summary>
internal static class ContractCallResolver {
    /// <summary>Whether a loaded contract exports an endpoint the name can refer to.</summary>
    public static bool IsExported(Domain domain, string name) =>
        ExportingContracts(domain, name).Count > 0;

    /// <summary>
    /// Resolves <paramref name="call"/> once per call node. <paramref name="argumentTypeName"/>
    /// is the type name the caller's scope gives the call's argument, or null when the scope
    /// does not know it. A bare identifier the scope does not know is deferred (no fact, no
    /// finding): a shared policy is also walked in scopes that lack the action's parameters.
    /// </summary>
    public static void Resolve(
        AnalysisContext context, Domain domain, FunctionCall call, string? argumentTypeName) {
        if (context.GetMetadata<ContractCallMetadata>(call) is not null)
            return;

        var contracts = ExportingContracts(domain, call.Name);
        if (contracts.Count == 0)
            return;

        var where = call.Line > 0 ? $" (line {call.Line}, col {call.Col})" : "";
        var subject = $"Contract call '{call.Name}'";

        if (contracts.Count > 1) {
            Record(context, call, contracts[0], ContractBindingStatus.Ambiguous, endpoint: null);
            context.ReportError(
                call,
                $"{subject} names an endpoint exported by several contracts: " +
                $"{string.Join(", ", contracts.Select(contract => $"'{contract.Name}'"))}{where}. " +
                "Remove one `uses`, or rename the endpoint on its contract.",
                DomainModelDiagnosticCodes.ContractOverloadAmbiguous);
            return;
        }

        var contract = contracts[0];
        if (call.Arguments.Count != 1) {
            Record(context, call, contract, ContractBindingStatus.Unresolved, endpoint: null);
            context.ReportError(
                call,
                $"{subject} takes exactly one argument, the payload; got {call.Arguments.Count}{where}.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        var argument = call.Arguments[0];
        var argumentName = argument switch {
            PropertyAccess property => property.Name,
            ParameterAccess parameter => parameter.Name,
            _ => null,
        };
        if (argumentName is null) {
            Record(context, call, contract, ContractBindingStatus.Unresolved, endpoint: null);
            context.ReportError(
                call,
                $"{subject} needs its argument to be a parameter or property of a declared type; " +
                $"a literal or computed value has no type name to resolve the endpoint with{where}.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        if (argumentTypeName is null)
            return;

        var local = new Property(argumentName, new DomainTypeReference(argumentTypeName), []);
        var resolution = ContractEndpointResolver.Resolve(contract, domain, call.Name, argumentTypeName);
        var classified = ContractIntegrationAnalyzer.ClassifyResolution(
            context, domain, call, subject, contract, resolution, local);
        Record(context, call, contract, classified.Status, classified.Endpoint?.Name);
        if (classified.Boundary is not null)
            context.SetMetadata(call, classified.Boundary);

        switch (resolution.Status) {
            case ContractBindingStatus.NotOnSurface:
                ContractIntegrationAnalyzer.ReportContractFinding(
                    context, domain, call,
                    DomainModelDiagnosticCodes.ContractEndpointNotOnSurface,
                    $"{subject} references endpoint '{call.Name}' that does not belong to contract '{contract.Name}'{where}.");
                break;

            case ContractBindingStatus.Incompatible when classified.Boundary is null:
                context.ReportError(
                    call,
                    $"{subject} argument '{argumentName}' type '{argumentTypeName}' is incompatible with " +
                    $"{ContractIntegrationAnalyzer.DescribeCandidates(resolution.Candidates)} on contract '{contract.Name}'{where}.",
                    DomainModelDiagnosticCodes.ContractIntegration);
                break;

            case ContractBindingStatus.Ambiguous:
                context.ReportError(
                    call,
                    $"{subject} names '{call.Name}', which matches " +
                    $"{ContractIntegrationAnalyzer.DescribeCandidates(resolution.Candidates)} and no payload is more " +
                    $"constrained than the others{where}. Name the exported endpoint instead.",
                    DomainModelDiagnosticCodes.ContractOverloadAmbiguous);
                break;

            case ContractBindingStatus.Bound when resolution.RequiredChoice:
                context.ReportWarning(
                    call,
                    $"{subject} names '{call.Name}', which matches " +
                    $"{ContractIntegrationAnalyzer.DescribeCandidates(resolution.Candidates)}. Selected " +
                    $"'{resolution.Endpoint!.Name}' as the more constrained payload; name the exported endpoint " +
                    "to make the choice explicit.",
                    DomainModelDiagnosticCodes.ContractOverloadSelected);
                break;
        }
    }

    /// <summary>
    /// Slice 1 resolves an endpoint call but nothing lowers it, so analysis must not pass it:
    /// reported at the call node in every position, whatever the resolution status or argument
    /// typing, once per call node (a shared policy is walked in several scopes).
    /// </summary>
    public static void RejectUnlowerable(AnalysisContext context, FunctionCall call) {
        if (context.Diagnostics.Any(d =>
                ReferenceEquals(d.Node, call)
                && string.Equals(d.Code, DomainModelDiagnosticCodes.ContractCallNotLowerable, StringComparison.Ordinal)))
            return;
        var where = call.Line > 0 ? $" (line {call.Line}, col {call.Col})" : "";
        context.ReportError(
            call,
            $"Calling imported endpoint '{call.Name}' from an expression is not supported yet; " +
            $"it cannot be lowered (#426){where}.",
            DomainModelDiagnosticCodes.ContractCallNotLowerable);
    }

    private static List<ImportedContract> ExportingContracts(Domain domain, string name) =>
        domain.ImportedContracts
            .Where(contract => contract.Endpoints.Any(endpoint =>
                string.Equals(endpoint.Name, name, StringComparison.Ordinal)
                || string.Equals(endpoint.SourceName ?? endpoint.Name, name, StringComparison.Ordinal)))
            .ToList();

    private static void Record(
        AnalysisContext context,
        FunctionCall call,
        ImportedContract contract,
        ContractBindingStatus status,
        string? endpoint) =>
        context.SetMetadata(call, new ContractCallMetadata(
            contract.UsesId,
            contract.Name,
            contract.SourceIdentifier,
            contract.Version,
            contract.Fingerprint,
            call.Name,
            endpoint,
            status));
}