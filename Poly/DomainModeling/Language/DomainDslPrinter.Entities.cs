using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

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

        foreach (var member in EntityDeclarationOrder.Members(entity)) {
            switch (member.Kind) {
                case EntityMemberKind.Property:
                    PrintProperty((Property)member.Node);
                    break;
                case EntityMemberKind.Navigation:
                    PrintNavigation((Relationship)member.Node);
                    break;
                case EntityMemberKind.Policy:
                    _sb.Append("  ");
                    PrintPolicy((Policy)member.Node, "  ");
                    break;
                case EntityMemberKind.Stage:
                    PrintStage((Stage)member.Node, "  ", entity);
                    break;
                case EntityMemberKind.Action:
                    PrintAction((Action)member.Node, "  ", stageName: null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(member), member.Kind, null);
            }
        }

        // Entity-level subscriptions: no order slot, printed after the members
        foreach (var sub in entity.Subscriptions) {
            PrintSubscription(sub, "  ");
        }

        _sb.AppendLine("}");
    }

    private void PrintProperty(Property prop) {
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

    private void PrintNavigation(Relationship rel) {
        _sb.Append("  ");
        _sb.Append(rel.Name);
        _sb.Append(": ");
        // Q11: a one-to-one navigation prints `one T`, which reads back as one-to-one without
        // ambiguity. A bare `x: T` also reads back as one-to-one, but it fails closed when T spells a
        // Phase 1a unsupported keyword (actor, schedule, parallel, for, function). ManyToOne has no DSL
        // spelling and prints bare, so a code-built ManyToOne reprints as `one T` on its second print.
        var isMany = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
        if (isMany) {
            _sb.Append("many ");
        }
        else if (rel.Cardinality == RelationshipCardinality.OneToOne) {
            _sb.Append("one ");
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
}