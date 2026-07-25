using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class ContractIntegrationAnalyzer : INodeAnalyzer {
    public const string Id = "DomainContractIntegrationAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        foreach (var contract in domain.ImportedContracts) {
            ValidateContract(context, contract);
        }

        foreach (var binding in domain.ContractBindings) {
            ValidateBinding(context, domain, binding);
        }
    }

    private static void ValidateContract(AnalysisContext context, ImportedContract contract) {
        if (string.IsNullOrWhiteSpace(contract.SourceIdentifier)) {
            context.ReportError(
                contract,
                $"Imported contract '{contract.Name}' is missing a source identifier.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        if (string.IsNullOrWhiteSpace(contract.Version)) {
            context.ReportError(
                contract,
                $"Imported contract '{contract.Name}' is missing a version.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }
    }

    private static void ValidateBinding(AnalysisContext context, Domain domain, ContractBinding binding) {
        var contract = domain.ImportedContracts.FirstOrDefault(c =>
            string.Equals(c.Name, binding.ContractName, StringComparison.Ordinal));
        if (contract is null) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references imported contract '{binding.ContractName}' that is not registered in the domain.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        var endpoint = contract.Endpoints.FirstOrDefault(e =>
            string.Equals(e.Name, binding.EndpointName, StringComparison.Ordinal));
        if (endpoint is null) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references endpoint '{binding.EndpointName}' that does not belong to contract '{binding.ContractName}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        var action = FindActionOnAnyEntity(domain, binding.ActionName);
        if (action is null) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references action '{binding.ActionName}' that was not found on any entity in the domain.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        var localParam = action.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, binding.LocalParameterName, StringComparison.Ordinal));
        if (localParam is null) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' references missing local parameter '{binding.LocalParameterName}' on action '{binding.ActionName}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
            return;
        }

        if (!string.Equals(localParam.Type.TypeName, endpoint.PayloadType.TypeName, StringComparison.Ordinal)) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' parameter '{localParam.Name}' type '{localParam.Type.TypeName}' is incompatible with endpoint payload '{endpoint.PayloadType.TypeName}'.",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        foreach (var map in binding.FieldMaps) {
            if (string.IsNullOrWhiteSpace(map.RemoteFieldName) || string.IsNullOrWhiteSpace(map.LocalFieldName)) {
                context.ReportError(
                    binding,
                    $"Contract binding '{binding.Name}' has a field map with empty remote or local field name.",
                    DomainModelDiagnosticCodes.ContractIntegration);
            }
        }
    }

    private static Action? FindActionOnAnyEntity(Domain domain, string actionName) {
        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                var action = entity.Actions.FirstOrDefault(a =>
                    string.Equals(a.Name, actionName, StringComparison.Ordinal));
                if (action is not null) return action;
            }
        }
        return null;
    }
}