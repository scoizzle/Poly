namespace Poly.DomainModeling.Meaning;

using Poly.DomainModeling.Ontology;

/// <summary>
/// Prints one core term annotation keyword (<c>iri</c>, <c>label</c>, <c>description</c>,
/// <c>term</c>). Parsing is the generic annotation grammar; this handler makes the term
/// survive <c>export_dsl</c>. Validation of the argument shape lives in
/// <see cref="TermFacets.IsWellFormed"/> and runs in the parser, so a malformed term can
/// never reach printing.
/// </summary>
internal sealed class TermAnnotationSyntax(string keyword) : IAnnotationSyntax {
    public string Keyword { get; } = keyword;

    public bool TryPrint(Facet facet, out string text) {
        text = null!;
        if (facet is not Annotation annotation
            || !string.Equals(annotation.Name, Keyword, StringComparison.Ordinal)
            || annotation.Arguments.Count != 1
            || !annotation.Arguments.TryGetValue("0", out var raw)
            || raw is not AnnotationString { Value.Length: > 0 } value) {
            return false;
        }

        text = $"{Keyword}(\"{DomainDslPrinter.EscapeStringLiteral(value.Value)}\")";
        return true;
    }
}