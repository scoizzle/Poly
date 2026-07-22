using Poly.DomainModeling.Parsing;

namespace Poly.DomainModeling;

/// <summary>
/// Canonical pack implementation of <c>column(...)</c> annotation syntax.
/// Produces <see cref="Annotation"/> records with positional arguments
/// matching the P1 grammar: <c>column("NAME")</c> or <c>column("NAME","TYPE")</c>.
///
/// This is the <b>product surface</b> (P3) — replaces the earlier test-only
/// double. If a future <c>Poly.Packs.Sql</c> library is extracted, this type
/// moves there.
/// </summary>
internal sealed class ColumnAnnotationSyntax : IAnnotationSyntax {
    public string Keyword => "column";

    public bool TryPrint(Facet facet, out string text) {
        text = null!;
        if (facet is not Annotation ann || ann.Name != "column")
            return false;
        if (!ann.Arguments.TryGetValue("0", out var nameArg)
            || nameArg is not AnnotationString name
            || string.IsNullOrWhiteSpace(name.Value))
            return false;

        var escapedName = DomainDslPrinter.EscapeStringLiteral(name.Value);

        if (ann.Arguments.TryGetValue("1", out var typeArg)) {
            if (typeArg is not AnnotationString type || string.IsNullOrWhiteSpace(type.Value))
                return false;
            text = $"column(\"{escapedName}\", \"{DomainDslPrinter.EscapeStringLiteral(type.Value)}\")";
            return true;
        }

        if (ann.Arguments.Count != 1)
            return false;

        text = $"column(\"{escapedName}\")";
        return true;
    }
}

/// <summary>
/// Canonical pack implementation of <c>table(...)</c> annotation syntax.
/// <c>table("NAME")</c> — single positional string argument.
/// </summary>
internal sealed class TableAnnotationSyntax : IAnnotationSyntax {
    public string Keyword => "table";

    public bool TryPrint(Facet facet, out string text) {
        text = null!;
        if (facet is not Annotation ann || ann.Name != "table")
            return false;
        if (ann.Arguments.Count != 1)
            return false;
        if (!ann.Arguments.TryGetValue("0", out var nameArg)
            || nameArg is not AnnotationString name
            || string.IsNullOrWhiteSpace(name.Value))
            return false;

        text = $"table(\"{DomainDslPrinter.EscapeStringLiteral(name.Value)}\")";
        return true;
    }
}