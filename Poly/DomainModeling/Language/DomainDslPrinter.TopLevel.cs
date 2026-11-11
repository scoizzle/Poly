using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

namespace Poly.DomainModeling.Language;

// DomainDslPrinter partial: domain-level printers with no concern partial (function, contract, binding, policy, subscription; G-00b mechanical split).
public sealed partial class DomainDslPrinter {
    private void PrintFunction(DomainFunction function) {
        _sb.Append("function ");
        _sb.Append(function.Name);
        _sb.Append('(');
        var first = true;
        foreach (var param in function.Parameters) {
            if (!first) _sb.Append(", ");
            first = false;
            _sb.Append(param.Name);
            _sb.Append(": ");
            _sb.Append(param.Type.TypeName);
        }
        _sb.Append("): ");
        _sb.Append(function.ReturnType.TypeName);
        _sb.Append(" = ");
        _sb.Append(PrintExpression(function.Body));
        _sb.AppendLine();
    }

    private void PrintContract(ImportedContract contract) {
        var kind = contract.SourceKind == ContractSourceKind.ExternalProvider ? "external" : "internal";
        _sb.Append(contract.Name);
        _sb.Append(": contract ");
        _sb.Append(kind);
        _sb.Append(' ');
        _sb.Append(IdentifierOrStringLiteral(contract.SourceIdentifier));
        _sb.Append(' ');
        _sb.Append(IdentifierOrStringLiteral(contract.Version));
        _sb.AppendLine(" {");
        foreach (var vt in contract.Types) {
            _sb.Append("  ");
            _sb.Append(vt.Name);
            _sb.AppendLine(": value {");
            foreach (var prop in vt.Properties) {
                _sb.Append("    ");
                _sb.Append(prop.Name);
                _sb.Append(": ");
                _sb.Append(prop.Type.TypeName);
                AppendPrintedConstraints(prop.Constraints);
                _sb.AppendLine();
            }
            _sb.AppendLine("  }");
        }
        foreach (var ep in contract.Endpoints) {
            _sb.Append("  ");
            _sb.Append(ep.Name);
            _sb.Append(": ");
            _sb.Append(ep.Direction == ContractEndpointDirection.Inbound ? "inbound" : "outbound");
            _sb.Append(' ');
            _sb.Append(ep.Kind == ContractEndpointKind.Operation ? "operation" : "event");
            _sb.Append(' ');
            _sb.Append(ep.PayloadType.TypeName);
            _sb.AppendLine();
        }
        _sb.AppendLine("}");
    }

    private void PrintBinding(ContractBinding binding) {
        _sb.Append(binding.Name);
        _sb.Append(": bind ");
        _sb.Append(binding.ContractName);
        _sb.Append(' ');
        _sb.Append(binding.EndpointName);
        _sb.Append(" to ");
        _sb.Append(binding.ActionName);
        _sb.Append(' ');
        _sb.Append(binding.LocalParameterName);
        _sb.AppendLine();
    }

    private void PrintSubscription(StageSubscription sub, string indent) {
        _sb.Append(indent);
        _sb.Append("when ");
        // P4-1: emit quantifier keyword only when not the default (Each).
        if (sub.Quantifier != StageSubscriptionQuantifier.Each) {
            _sb.Append(sub.Quantifier.ToString().ToLowerInvariant());
            _sb.Append(' ');
        }
        _sb.Append(sub.RelationshipName);
        _sb.Append(' ');
        _sb.Append(string.Join(", ", sub.StageNames));
        if (!string.IsNullOrEmpty(sub.PeerBinding)) {
            _sb.Append(" as ");
            _sb.Append(sub.PeerBinding);
        }
        _sb.AppendLine(" {");

        foreach (var effect in sub.Effects) {
            _sb.Append(indent);
            _sb.Append("  ");
            PrintEffect(effect, indent + "  ");
        }

        _sb.Append(indent);
        _sb.AppendLine("}");
    }

    private void PrintPolicy(Policy policy, string indent) {
        _sb.Append(policy.Name);
        _sb.Append(": policy { ");
        _sb.Append(PrintExpression(policy.Expression));
        _sb.AppendLine(" }");
    }
}