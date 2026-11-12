using System.Globalization;
using System.Text;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using Entity = Poly.DomainModeling.Ontology.Entity;
using EnumType = Poly.DomainModeling.Ontology.EnumType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// A human-facing Markdown report over the same domain facts the machine artifacts use,
/// in declaration order (Q12).
/// It is a projection, not a second summary: terms, constraints, requiredness, stages,
/// actions, enums, and relationships all come from the model, so it cannot drift from
/// the JSON Schema/JSON-LD/DSL outputs.
/// </summary>
public static class DomainReportExporter {
    public static string Export(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var ordered = DomainExportOrder.Types(domain);
        var entities = ordered.OfType<Entity>().ToList();
        var valueTypes = ordered.OfType<ValueType>().ToList();
        var enums = ordered.OfType<EnumType>().ToList();
        var properties = entities.Sum(e => e.Properties.Count) + valueTypes.Sum(v => v.Properties.Count);
        var relationships = entities.Sum(e => e.Navigations.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"# {domain.Name}");
        sb.AppendLine();
        sb.AppendLine(string.Join(" · ",
            Count(entities.Count, "entity", "entities"),
            Count(enums.Count, "enum"),
            Count(valueTypes.Count, "value type"),
            Count(relationships, "relationship"),
            Count(properties, "property", "properties")));
        sb.AppendLine();

        if (entities.Count > 0) {
            sb.AppendLine("## Entities");
            sb.AppendLine();
            foreach (var entity in entities)
                AppendEntity(sb, entity);
        }

        if (enums.Count > 0) {
            sb.AppendLine("## Enums");
            sb.AppendLine();
            foreach (var enumType in enums)
                AppendEnum(sb, enumType);
        }

        if (valueTypes.Count > 0) {
            sb.AppendLine("## Value Types");
            sb.AppendLine();
            foreach (var valueType in valueTypes)
                AppendValueType(sb, valueType);
        }

        var navigations = entities
            .SelectMany(e => EntityDeclarationOrder.Navigations(e).Select(n => (Source: e, Navigation: n)))
            .ToList();
        if (navigations.Count > 0) {
            sb.AppendLine("## Relationships");
            sb.AppendLine();
            sb.AppendLine("| Source | Name | Target | Cardinality |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var (source, navigation) in navigations) {
                sb.AppendLine($"| {Cell(source.Name)} | {Cell(navigation.Name)} | " +
                    $"{Cell(navigation.Target.TypeName)} | {DomainPresentation.Cardinality(navigation.Cardinality)} |");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendEntity(StringBuilder sb, Entity entity) {
        sb.AppendLine($"### {entity.Name}");
        sb.AppendLine();
        AppendTerm(sb, entity.Facets);

        if (entity.Properties.Count > 0) {
            sb.AppendLine("| Property | Type | Constraints | Required | IRI |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            var entryAssigned = EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(entity);
            foreach (var property in EntityDeclarationOrder.Properties(entity)) {
                var term = TermFacets.Read(property.Facets);
                sb.AppendLine($"| {Cell(property.Name)} | {Cell(property.Type.TypeName)} | " +
                    $"{CodeOrDash(DomainPresentation.Constraints(property))} | " +
                    $"{(DomainPresentation.IsRequired(property, entryAssigned) ? "yes" : "no")} | " +
                    $"{IriCell(term)} |");
            }
            sb.AppendLine();
        }

        if (entity.Stages.Count > 0) {
            sb.AppendLine("**Stages**");
            sb.AppendLine();
            sb.AppendLine("| Stage | Actions |");
            sb.AppendLine("| --- | --- |");
            foreach (var stage in entity.Stages) {
                var stageActionNames = stage.Actions.Count == 0
                    ? "—"
                    : string.Join(", ", stage.Actions.Select(a => a.Name));
                sb.AppendLine($"| {Cell(stage.Name)} | {Cell(stageActionNames)} |");
            }
            sb.AppendLine();
        }

        var actions = entity.Actions
            .Select(a => (Stage: (string?)null, Action: a))
            .Concat(entity.Stages.SelectMany(s => s.Actions.Select(a => (Stage: (string?)s.Name, Action: a))))
            .ToList();
        if (actions.Count > 0) {
            sb.AppendLine("**Actions**");
            sb.AppendLine();
            sb.AppendLine("| Action | Stage | Parameters | Guards |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var (stage, action) in actions) {
                var parameters = action.Parameters.Count == 0
                    ? "—"
                    : string.Join(", ", action.Parameters.Select(p => $"{p.Name}: {p.Type.TypeName}"));
                var guards = action.Policies.Count == 0
                    ? "—"
                    : string.Join(", ", action.Policies.Select(p => p.Name));
                sb.AppendLine($"| {Cell(action.Name)} | {Cell(stage ?? "entity")} | " +
                    $"{Cell(parameters)} | {Cell(guards)} |");
            }
            sb.AppendLine();
        }
    }

    private static void AppendEnum(StringBuilder sb, EnumType enumType) {
        sb.AppendLine($"### {enumType.Name}");
        sb.AppendLine();
        AppendTerm(sb, enumType.Facets);
        sb.AppendLine("| Member | IRI | Term |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var member in enumType.MemberNames) {
            var term = TermFacets.Read(enumType.MemberFacets.GetValueOrDefault(member));
            sb.AppendLine($"| {Cell(member)} | {IriCell(term)} | " +
                $"{(term?.Term is { } name ? Code(name) : "—")} |");
        }
        sb.AppendLine();
    }

    private static void AppendValueType(StringBuilder sb, ValueType valueType) {
        sb.AppendLine($"### {valueType.Name}");
        sb.AppendLine();
        AppendTerm(sb, valueType.Facets);
        if (valueType.Properties.Count == 0)
            return;

        sb.AppendLine("| Property | Type | Constraints | Required | IRI |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        var entryAssigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in valueType.Properties) {
            var term = TermFacets.Read(property.Facets);
            sb.AppendLine($"| {Cell(property.Name)} | {Cell(property.Type.TypeName)} | " +
                $"{CodeOrDash(DomainPresentation.Constraints(property))} | " +
                $"{(DomainPresentation.IsRequired(property, entryAssigned) ? "yes" : "no")} | " +
                $"{IriCell(term)} |");
        }
        sb.AppendLine();
    }

    private static void AppendTerm(StringBuilder sb, IReadOnlyList<Facet> facets) {
        var term = TermFacets.Read(facets);
        if (term is null)
            return;
        if (term.Iri is { } iri) {
            sb.AppendLine($"**IRI:** {Code(iri)}");
            sb.AppendLine();
        }
        if (term.Description is { } description) {
            sb.AppendLine(description);
            sb.AppendLine();
        }
    }

    private static string CodeOrDash(string? text) => text is null ? "—" : Code(text);

    private static string IriCell(TermMetadata? term) =>
        term?.Iri is { } iri ? Code(iri) : "—";

    private static string Code(string text) {
        // A GFM table splits on pipes even inside a code span, and a backtick run must be
        // shorter than its fence. Escape the pipe and size the fence to the content.
        var escaped = text.Replace("|", "\\|");
        var fence = new string('`', LongestBacktickRun(escaped) + 1);
        var pad = escaped.StartsWith('`') || escaped.EndsWith('`') ? " " : "";
        return $"{fence}{pad}{escaped}{pad}{fence}";
    }

    private static int LongestBacktickRun(string text) {
        var longest = 0;
        var run = 0;
        foreach (var c in text) {
            if (c == '`') {
                run++;
                longest = Math.Max(longest, run);
            }
            else {
                run = 0;
            }
        }
        return longest;
    }

    private static string Cell(string text) =>
        text.Replace("|", "\\|").ReplaceLineEndings(" ");

    private static string Count(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";
}