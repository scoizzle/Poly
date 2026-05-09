using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

internal sealed class ContractIntegrationAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        if (node is Domain request) {
            AnalyzeDomain(context, request.Domain);
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<ContractIntegrationAnalyzer>(domain)) {
            return;
        }

        foreach (var contract in domain.GetAvailableImportedContracts().Where(context.ShouldAnalyze)) {
            ValidateContract(context, domain, contract);
        }

        foreach (var binding in domain.GetAvailableContractBindings().Where(context.ShouldAnalyze)) {
            ValidateBinding(context, domain, binding);
        }
    }

    private static void ValidateContract(AnalysisContext context, Domain domain, ImportedContract contract) {
        if (!ReferenceEquals(contract.Domain, domain)) {
            context.ReportError(
                contract,
                $"Imported contract '{contract.Name}' does not belong to domain '{domain.Name}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        if (string.IsNullOrWhiteSpace(contract.SourceIdentifier)) {
            context.ReportError(
                contract,
                $"Imported contract '{contract.Name}' is missing a source identifier for contract binding.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        if (string.IsNullOrWhiteSpace(contract.Version)) {
            context.ReportError(
                contract,
                $"Imported contract '{contract.Name}' is missing a version for contract binding.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        foreach (var endpoint in contract.Endpoints) {
            if (!ReferenceEquals(endpoint.Domain, domain)) {
                context.ReportError(
                    endpoint,
                    $"Contract endpoint '{endpoint.Name}' does not belong to domain '{domain.Name}'.",
                    DomainModelDiagnosticCodes.ContractIntegration);
            }

            if (!ReferenceEquals(endpoint.PayloadType.Domain, domain)) {
                context.ReportError(
                    endpoint,
                    $"Contract endpoint '{endpoint.Name}' payload type '{endpoint.PayloadType.Name}' must belong to domain '{domain.Name}'.",
                    DomainModelDiagnosticCodes.ContractIntegration);
            }
        }
    }

    private static void ValidateBinding(AnalysisContext context, Domain domain, ContractBinding binding) {
        if (!ReferenceEquals(binding.Domain, domain)) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' does not belong to domain '{domain.Name}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        if (!domain.Objects.Contains(binding.Contract)) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references imported contract '{binding.Contract.Name}' that is not registered in domain '{domain.Name}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        if (!binding.Contract.Endpoints.Contains(binding.Endpoint)) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references endpoint '{binding.Endpoint.Name}' that does not belong to imported contract '{binding.Contract.Name}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        if (!ReferenceEquals(binding.LocalAction.Domain, domain) || binding.LocalAction.Entity is null) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references local action '{binding.LocalAction.Name}' that is not attached to an entity in domain '{domain.Name}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        var localParameter = binding.LocalAction.Parameters
            .OfType<Property>()
            .FirstOrDefault(parameter => string.Equals(parameter.Name, binding.LocalParameterName, StringComparison.Ordinal));

        if (localParameter is null) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references missing local parameter '{binding.LocalParameterName}' on action '{binding.LocalAction.Name}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        if (!DomainTypeAssignability.CanAssign(localParameter.Type, binding.Endpoint.PayloadType)
            && !DomainTypeAssignability.CanAssign(binding.Endpoint.PayloadType, localParameter.Type)) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' parameter '{localParameter.Name}' type '{localParameter.Type.Name}' is incompatible with endpoint payload '{binding.Endpoint.PayloadType.Name}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        foreach (var map in binding.FieldMaps) {
            if (string.IsNullOrWhiteSpace(map.RemoteFieldName) || string.IsNullOrWhiteSpace(map.LocalFieldName)) {
                context.ReportError(
                    map,
                    $"Contract binding '{binding.Name}' has a contract binding field map with empty remote/local field name.",
                    DomainModelDiagnosticCodes.ContractIntegration);
            }
        }
    }
}