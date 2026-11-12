using System.Globalization;

using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// Fact formatting shared by the human-facing projections (Markdown report and HTML docs).
/// One place decides requiredness and constraint text so the two documents cannot drift.
/// </summary>
internal static class DomainPresentation {
    /// <summary>
    /// The DSL create rule: required unless <c>optional</c>, a <c>default</c> supplies
    /// the value, or the initial stage's entry effect initializes it.
    /// </summary>
    public static bool IsRequired(Property property, IReadOnlySet<string>? entryAssigned) =>
        !property.Constraints.OfType<OptionalConstraint>().Any()
        && !property.Constraints.OfType<DefaultValueConstraint>().Any()
        && (entryAssigned is null || !entryAssigned.Contains(property.Name));

    /// <summary>The property's constraints as DSL text, or null when it has none.</summary>
    public static string? Constraints(Property property) {
        ArgumentNullException.ThrowIfNull(property);
        if (property.Constraints.Count == 0)
            return null;

        return string.Join(", ", property.Constraints.Select(constraint => constraint switch {
            RangeConstraint range => $"range({Bound(range.Minimum)}, {Bound(range.Maximum)})",
            LengthConstraint length => $"length({length.MinLength}, {length.MaxLength})",
            PatternConstraint pattern => $"pattern(\"{DomainDslPrinter.EscapeStringLiteral(pattern.Pattern)}\")",
            DefaultValueConstraint defaultValue => $"default({Expression(defaultValue.Expression)})",
            UniqueConstraint => "unique",
            OptionalConstraint => "optional",
            RequiredConstraint => "required",
            EqualityConstraint equality => $"equals({LiteralValue(equality.ExpectedValue)})",
            _ => constraint.GetType().Name,
        }));
    }

    public static string Expression(DomainExpression expression) => expression switch {
        Literal literal => LiteralValue(literal.Value),
        PropertyAccess access => access.Name,
        _ => "…",
    };

    public static string LiteralValue(object? value) => value switch {
        null => "null",
        bool flag => flag ? "true" : "false",
        string text => $"\"{DomainDslPrinter.EscapeStringLiteral(text)}\"",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static string Bound(object? value) =>
        value is null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    public static string Cardinality(RelationshipCardinality cardinality) => cardinality switch {
        RelationshipCardinality.OneToOne => "one to one",
        RelationshipCardinality.OneToMany => "one to many",
        RelationshipCardinality.ManyToOne => "many to one",
        RelationshipCardinality.ManyToMany => "many to many",
        _ => cardinality.ToString(),
    };
}