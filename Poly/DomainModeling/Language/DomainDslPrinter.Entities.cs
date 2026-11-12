using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Language;

// DomainDslPrinter partial: entity declaration printing (G-00b mechanical split).
public sealed partial class DomainDslPrinter {
    private void PrintEntity(Entity entity) {
        if (entity.Visibility == Visibility.Public) {
            _sb.Append("public entity ");
            _sb.Append(entity.Name);
        }
        else {
            _sb.Append(entity.Name);
            _sb.Append(": entity");
        }

        foreach (var facet in entity.Facets) {
            _sb.Append(' ');
            _sb.Append(PrintFacet(facet));
        }

        if (entity.InitialStageName is { } initialStageName) {
            _sb.Append(" initial(");
            _sb.Append(initialStageName);
            _sb.Append(')');
        }

        if (entity.KeyProperties.Count > 0) {

            _sb.Append(" key(");
            _sb.Append(string.Join(", ", entity.KeyProperties));
            _sb.Append(')');
        }

        _sb.AppendLine(" {");

        // Properties
        foreach (var prop in entity.Properties) {
            _sb.Append("  ");
            _sb.Append(prop.Name);
            _sb.Append(": ");
            _sb.Append(prop.Type.TypeName);

            AppendPrintedConstraints(prop.Constraints);

            foreach (var facet in prop.Facets) {
                _sb.Append(' ');
                _sb.Append(PrintFacet(facet, prop.Name));
            }

            _sb.AppendLine();
        }

        // Navigation properties (N1 source-side only)
        foreach (var rel in entity.Navigations
            .OrderBy(r => r.Name, StringComparer.Ordinal)) {
            _sb.Append("  ");
            _sb.Append(rel.Name);
            _sb.Append(": ");
            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            if (isMany) {
                _sb.Append("many ");
            }
            if (rel.SourceOwnsTarget) {
                _sb.Append("owned ");
            }
            _sb.Append(rel.Target.TypeName);
            if (rel.Inverse is { Length: > 0 } inverse) {
                _sb.Append(" via ");
                _sb.Append(inverse);
            }
            _sb.AppendLine();
        }

        // Entity-level policies
        foreach (var policy in entity.Policies) {
            _sb.Append("  ");
            PrintPolicy(policy, "  ");
        }

        // Entity-level subscriptions
        foreach (var sub in entity.Subscriptions) {
            PrintSubscription(sub, "  ");
        }

        // Stages (in declared order)
        foreach (var stage in entity.Stages) {
            PrintStage(stage, "  ", entity);
        }

        // Entity-level actions
        foreach (var action in entity.Actions) {
            PrintAction(action, "  ", stageName: null);
        }

        _sb.AppendLine("}");
    }
}