using Poly.DomainModeling;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Test-only annotation syntax handler for <c>column(...)</c> and <c>table(...)</c>.
/// Matches the P1 positional grammar and fails closed on malformed argument shapes.
/// </summary>
internal sealed class TestColumnAnnotationSyntax : IAnnotationSyntax {
    public string Keyword => "column";

    public bool TryPrint(Facet facet, out string text) {
        text = null!;
        if (facet is not Annotation ann || ann.Name != "column")
            return false;
        if (!ann.Arguments.TryGetValue("0", out var nameArg) || nameArg is not AnnotationString name)
            return false;

        if (ann.Arguments.TryGetValue("1", out var typeArg)) {
            if (typeArg is not AnnotationString type)
                return false;
            text = $"column(\"{name.Value}\", \"{type.Value}\")";
            return true;
        }

        if (ann.Arguments.Count != 1)
            return false;

        text = $"column(\"{name.Value}\")";
        return true;
    }
}

internal sealed class TestTableAnnotationSyntax : IAnnotationSyntax {
    public string Keyword => "table";

    public bool TryPrint(Facet facet, out string text) {
        text = null!;
        if (facet is not Annotation ann || ann.Name != "table")
            return false;
        if (ann.Arguments.Count != 1)
            return false;
        if (!ann.Arguments.TryGetValue("0", out var nameArg) || nameArg is not AnnotationString name)
            return false;

        text = $"table(\"{name.Value}\")";
        return true;
    }
}