namespace Poly.DomainModeling.Meaning;

/// <summary>
/// A pack-provided handler for one annotation keyword (e.g. <c>"column"</c>, <c>"table"</c>, <c>"json"</c>).
/// The parser natively parses <c>keyword("arg1", "arg2")</c> into <see cref="Annotation"/> records;
/// packs implement this interface primarily for <b>printing</b> (and optionally for custom
/// facet types that <see cref="Annotation"/> cannot represent).
/// </summary>
public interface IAnnotationSyntax {
    /// <summary>The keyword that triggers this handler, e.g. <c>"column"</c>.</summary>
    string Keyword { get; }

    /// <summary>
    /// If this handler can print the given facet, returns the .poly text.
    /// Returns <c>false</c> if the facet is not recognized.
    /// </summary>
    bool TryPrint(Facet facet, out string text);
}