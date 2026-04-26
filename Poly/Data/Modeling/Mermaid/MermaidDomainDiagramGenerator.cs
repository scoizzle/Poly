using System.Text;

using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Mermaid;

/// <summary>
/// Generates Mermaid class diagrams from a domain model, showing entities,
/// relationships, stages, properties, and policies.
/// </summary>
public sealed class MermaidDomainDiagramGenerator {
    private readonly StringBuilder _sb = new();

    /// <summary>
    /// Generates a Mermaid classDiagram from the given domain.
    /// </summary>
    public string Generate(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        _sb.Clear();
        _sb.AppendLine("classDiagram");

        foreach (var type in domain.Types) {
            switch (type) {
                case Primitive:
                    break;
                case Relationship:
                    break;
                case Event @event:
                    EmitEventClass(@event);
                    break;
                case Entity entity:
                    EmitEntityClass(entity);
                    break;
            }
        }

        foreach (var rel in domain.Relationships) {
            if (HasRichModel(rel))
                EmitRelationshipClass(rel);
        }

        EmitPropertyAssociations(domain);

        foreach (var rel in domain.Relationships) {
            EmitRelationshipArrow(rel);
            if (HasRichModel(rel))
                EmitRelationshipClassLinks(rel);
        }

        return _sb.ToString();
    }

    private static bool HasRichModel(Relationship rel) =>
        rel.Stages.Count > 0 || rel.Policies.Count > 0 || rel.Properties.Count > 0;

    private void EmitEntityClass(Entity entity) {
        _sb.AppendLine($"    class {entity.Name} {{");

        foreach (var prop in entity.Properties)
            _sb.AppendLine($"        +{GetTypeName(prop.Type)} {prop.Name}");

        foreach (var action in entity.Stages.SelectMany(s => s.Actions)) {
            var paramList = string.Join(", ", action.Parameters.Cast<Property>().Select(p => GetTypeName(p.Type)));
            _sb.AppendLine($"        +{action.Name}({paramList})");
        }

        _sb.AppendLine("    }");

        if (entity.ParentEntity is not null)
            _sb.AppendLine($"    {entity.ParentEntity.Name} <|-- {entity.Name}");

        EmitPoliciesNote(entity.Name, entity.Policies);
        EmitStagesEnum(entity.Name, entity.Stages);
    }

    private void EmitEventClass(Event @event) {
        _sb.AppendLine($"    class {@event.Name} {{");
        _sb.AppendLine("        <<event>>");

        foreach (var prop in @event.Properties)
            _sb.AppendLine($"        +{GetTypeName(prop.Type)} {prop.Name}");

        _sb.AppendLine("    }");
    }

    private void EmitRelationshipClass(Relationship rel) {
        _sb.AppendLine($"    class {rel.Name} {{");
        _sb.AppendLine("        <<relationship>>");

        foreach (var prop in rel.Properties)
            _sb.AppendLine($"        +{GetTypeName(prop.Type)} {prop.Name}");

        _sb.AppendLine("    }");

        EmitPoliciesNote(rel.Name, rel.Policies);
        EmitStagesEnum(rel.Name, rel.Stages);
    }

    private void EmitPoliciesNote(string ownerName, IReadOnlyCollection<Policy> policies) {
        if (policies.Count == 0) return;
        var policyText = string.Join("\\n", policies.Select(p => p.Name));
        _sb.AppendLine($"    note for {ownerName} \"{policyText}\"");
    }

    private void EmitStagesEnum(string ownerName, IReadOnlyCollection<Stage> stages) {
        if (stages.Count == 0) return;

        var enumName = $"{ownerName}Stage";
        _sb.AppendLine($"    class {enumName} {{");
        _sb.AppendLine("        <<enumeration>>");

        foreach (var stage in stages) {
            var parentSuffix = stage.Parent is not null ? $" ({stage.Parent.Name})" : string.Empty;
            _sb.AppendLine($"        {stage.Name}{parentSuffix}");
        }

        _sb.AppendLine("    }");
        _sb.AppendLine($"    {ownerName} ..> {enumName} : stage");
    }

    private void EmitPropertyAssociations(Domain domain) {
        var entityNames = domain.Types
            .OfType<Entity>()
            .Where(e => e is not Relationship)
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entity in domain.Types.OfType<Entity>().Where(e => e is not Relationship)) {
            foreach (var prop in entity.Properties) {
                if (prop.Type is Entity targetEntity && entityNames.Contains(targetEntity.Name))
                    _sb.AppendLine($"    {entity.Name} --> {targetEntity.Name} : {prop.Name}");
            }
        }
    }

    private void EmitRelationshipArrow(Relationship rel) {
        var (sourceCard, targetCard) = rel.Cardinality switch {
            RelationshipCardinality.OneToOne => ("\"1\"", "\"1\""),
            RelationshipCardinality.OneToMany => ("\"1\"", "\"*\""),
            RelationshipCardinality.ManyToOne => ("\"*\"", "\"1\""),
            RelationshipCardinality.ManyToMany => ("\"*\"", "\"*\""),
            _ => (string.Empty, string.Empty)
        };

        var arrow = rel.SourceOwnsTarget ? "*--" : "-->";
        _sb.AppendLine($"    {rel.Source.Name} {sourceCard} {arrow} {targetCard} {rel.Target.Name} : {rel.Name}");
    }

    private void EmitRelationshipClassLinks(Relationship rel) {
        _sb.AppendLine($"    {rel.Name} ..> {rel.Source.Name} : source");
        _sb.AppendLine($"    {rel.Name} ..> {rel.Target.Name} : target");
    }

    private static string GetTypeName(IDomainType type) => type switch {
        Collection c => $"{GetTypeName(c.ElementType)}[]",
        _ => type.Name
    };
}