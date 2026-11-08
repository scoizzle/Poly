using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Lint-only: imported-contract / binding checks and exported-surface closure checks;
/// writes no metadata others read.
/// </summary>
internal sealed class ContractIntegrationAnalyzer : INodeAnalyzer {
    public const string Id = "DomainContractIntegrationAnalyzer";
    public string PassName => Id;
    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        ReportTypeNameClashes(context, domain);

        foreach (var contract in domain.ImportedContracts) {
            ValidateContract(context, domain, contract);
        }

        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var prop in entity.Properties) {
                if (FindContractOwningType(domain, prop.Type.TypeName) is { } owner) {
                    context.ReportError(
                        prop,
                        $"Entity '{entity.Name}' property '{prop.Name}' is typed as contract value type '{prop.Type.TypeName}' from '{owner.Name}'. Stored state must use parent-domain types; use the contract type only on a bound action parameter.",
                        DomainModelDiagnosticCodes.ContractIntegration);
                }
            }
        }

        foreach (var binding in domain.ContractBindings) {
            ValidateBinding(context, domain, binding);
        }

        ValidatePublicSurface(context, domain);
    }

    /// <summary>
    /// An exported signature must be closed (decision 12, #417): a public action's parameter
    /// and result types and a public value type's field types may not name a type internal to
    /// this domain. A public action must also be reachable through public (#418): its owning
    /// entity must be public, because a public entity exports its name and its public actions.
    /// Not checked: a public entity's properties (decision 13 exports its name and
    /// public actions only), an imported contract's ACL value types (that contract's surface),
    /// primitives, and enums (no visibility mark; #418 gives them a digest entry).
    /// </summary>
    private static void ValidatePublicSurface(AnalysisContext context, Domain domain) {
        foreach (var valueType in domain.Types.OfType<ValueType>()) {
            if (valueType.Visibility != Visibility.Public)
                continue;
            foreach (var property in valueType.Properties) {
                ReportUnclosedSignature(
                    context, domain, valueType, property.Type,
                    $"Public value type '{valueType.Name}'",
                    $"in property '{property.Name}'");
            }
        }

        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var action in entity.Actions) {
                if (action.Visibility == Visibility.Public)
                    ValidatePublicAction(context, domain, entity, action);
            }
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions) {
                    if (action.Visibility == Visibility.Public)
                        ValidatePublicAction(context, domain, entity, action);
                }
            }
        }
    }

    private static void ValidatePublicAction(
        AnalysisContext context,
        Domain domain,
        Entity entity,
        Action action) {
        if (entity.Visibility != Visibility.Public) {
            context.ReportError(
                action,
                $"Public action '{action.Name}' on entity '{entity.Name}' is not reachable: " +
                $"entity '{entity.Name}' is internal. Mark '{entity.Name}' public, or remove " +
                $"the public mark from '{action.Name}'.",
                DomainModelDiagnosticCodes.PublicMemberOnInternalOwner);
            return;
        }

        var subject = $"Public action '{action.Name}' on entity '{entity.Name}'";

        foreach (var parameter in action.Parameters) {
            ReportUnclosedSignature(
                context, domain, action, parameter.Type,
                subject, $"in parameter '{parameter.Name}'");
        }

        foreach (var member in action.Result.Members) {
            ReportUnclosedSignature(
                context, domain, action, member.Type,
                subject, "as its result");
        }
    }

    /// <summary>
    /// Reports one finding when <paramref name="typeReference"/> resolves to a type this
    /// domain declares with <see cref="Visibility.Internal"/>. An unresolved reference is
    /// skipped: <see cref="DomainCatalogPass"/> already reported the unknown type.
    /// </summary>
    private static void ReportUnclosedSignature(
        AnalysisContext context,
        Domain domain,
        Node reportNode,
        DomainTypeReference typeReference,
        string subject,
        string position) {
        if (context.GetMetadata<ResolvedTypeReferenceMetadata>(typeReference)
            is not ResolvedTypeReferenceMetadata resolved)
            return;

        if (!IsDeclaredInDomain(domain, resolved.Type))
            return;

        var isPublic = resolved.Type switch {
            Entity entity => entity.Visibility == Visibility.Public,
            ValueType valueType => valueType.Visibility == Visibility.Public,
            _ => true
        };
        if (isPublic)
            return;

        var kind = resolved.Type is Entity ? "entity" : "value";
        context.ReportError(
            reportNode,
            $"{subject} names internal {kind} type '{resolved.Type.Name}' {position}. " +
            $"Mark '{resolved.Type.Name}' public, or remove the public mark.",
            DomainModelDiagnosticCodes.PublicSurfaceNotClosed);
    }

    /// <summary>
    /// Reference identity, not record equality: a contract ACL value type with the same shape
    /// as a domain value type must not count as declared here.
    /// </summary>
    private static bool IsDeclaredInDomain(Domain domain, DomainType type) =>
        domain.Types.Any(candidate => ReferenceEquals(candidate, type));

    private static void ReportTypeNameClashes(AnalysisContext context, Domain domain) {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in domain.Types.OfType<ValueType>())
            seen[t.Name] = $"domain value type '{t.Name}'";
        foreach (var t in domain.Types.OfType<EnumType>())
            seen[t.Name] = $"enum '{t.Name}'";
        foreach (var t in domain.Types.OfType<Entity>())
            seen[t.Name] = $"entity '{t.Name}'";

        foreach (var contract in domain.ImportedContracts) {
            foreach (var vt in contract.Types) {
                if (seen.TryGetValue(vt.Name, out var prior)) {
                    context.ReportError(
                        vt,
                        $"Value type '{vt.Name}' on contract '{contract.Name}' clashes with {prior}.",
                        DomainModelDiagnosticCodes.ContractIntegration);
                }
                else {
                    seen[vt.Name] = $"contract '{contract.Name}' value type '{vt.Name}'";
                }
            }
        }
    }

    private static ImportedContract? FindContractOwningType(Domain domain, string typeName) {
        foreach (var contract in domain.ImportedContracts) {
            if (contract.Types.Any(t => string.Equals(t.Name, typeName, StringComparison.Ordinal)))
                return contract;
        }
        return null;
    }

    private static bool PayloadTypeExists(Domain domain, ImportedContract contract, string typeName) {
        if (typeName is "Text" or "Number" or "Boolean")
            return true;
        if (contract.Types.Any(t => string.Equals(t.Name, typeName, StringComparison.Ordinal)))
            return true;
        return domain.Types.Any(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
    }

    private static void ValidateContract(AnalysisContext context, Domain domain, ImportedContract contract) {
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

        foreach (var endpoint in contract.Endpoints) {
            if (!PayloadTypeExists(domain, contract, endpoint.PayloadType.TypeName)) {
                context.ReportError(
                    endpoint,
                    $"Contract '{contract.Name}' endpoint '{endpoint.Name}' payload type '{endpoint.PayloadType.TypeName}' is not a primitive or a value type on that contract (or the parent domain).",
                    DomainModelDiagnosticCodes.ContractIntegration);
            }
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

        if (!PayloadTypeExists(domain, contract, endpoint.PayloadType.TypeName)) {
            context.ReportError(
                endpoint,
                $"Contract '{contract.Name}' endpoint '{endpoint.Name}' payload type '{endpoint.PayloadType.TypeName}' is not a primitive or a value type on that contract (or the parent domain).",
                DomainModelDiagnosticCodes.ContractIntegration);
        }

        var action = DomainAnalysis.FindActionOnAnyEntity(domain, binding.ActionName);
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

}