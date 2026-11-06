namespace Poly.DomainModeling.Ontology;

/// <summary>
/// Ontology term metadata for one domain element: the IRI it denotes, a human label,
/// a description, and (for an enum member) the term name inside the IRI's vocabulary.
/// Authored in <c>.poly</c> as the core annotations <c>iri(...)</c>, <c>label(...)</c>,
/// <c>description(...)</c>, and <c>term(...)</c>.
/// </summary>
public sealed record TermMetadata(
    string? Iri = null,
    string? Label = null,
    string? Description = null,
    string? Term = null) {
    /// <summary>True when no term key was declared.</summary>
    public bool IsEmpty => Iri is null && Label is null && Description is null && Term is null;
}

/// <summary>
/// The core term vocabulary over <see cref="Facet"/> metadata. Term annotations are
/// ordinary facets on the element, so every existing walk, evolution, and print path
/// carries them; this type is the one reader that gives them meaning.
/// </summary>
public static class TermFacets {
    public const string Iri = "iri";
    public const string Label = "label";
    public const string Description = "description";
    public const string Term = "term";

    /// <summary>The keywords the core term vocabulary reserves.</summary>
    public static IReadOnlyList<string> Keywords { get; } = [Iri, Label, Description, Term];

    /// <summary>True when <paramref name="keyword"/> is term metadata.</summary>
    public static bool IsTermKeyword(string keyword) =>
        keyword is Iri or Label or Description or Term;

    /// <summary>
    /// A term annotation is exactly one non-empty string argument, and an <c>iri</c> must
    /// be an absolute http(s) or urn IRI. Numbers, booleans, null, extra arguments,
    /// blanks, and CURIEs are typos, not terms; the parser fails closed on them.
    /// </summary>
    public static bool IsWellFormed(string keyword, IReadOnlyDictionary<string, AnnotationValue> arguments) {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!IsTermKeyword(keyword))
            return true;
        if (arguments.Count != 1
            || !arguments.TryGetValue("0", out var value)
            || value is not AnnotationString text
            || string.IsNullOrWhiteSpace(text.Value)) {
            return false;
        }
        return keyword != Iri || IsAbsoluteIri(text.Value);
    }

    /// <summary>True for an absolute http(s) or urn IRI. A CURIE has no prefix declaration here.</summary>
    public static bool IsAbsoluteIri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("urn", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the term keys from a facet list. Returns null when none is present.
    /// A last-wins rule mirrors the annotation surface: a repeated key replaces the
    /// earlier value (the parser keeps both facets, the reader keeps the last).
    /// </summary>
    public static TermMetadata? Read(IEnumerable<Facet>? facets) {
        if (facets is null)
            return null;

        string? iri = null, label = null, description = null, term = null;
        foreach (var facet in facets) {
            if (facet is not Annotation annotation || !IsTermKeyword(annotation.Name))
                continue;
            if (!annotation.Arguments.TryGetValue("0", out var value)
                || value is not AnnotationString text) {
                continue;
            }

            switch (annotation.Name) {
                case Iri: iri = text.Value; break;
                case Label: label = text.Value; break;
                case Description: description = text.Value; break;
                case Term: term = text.Value; break;
            }
        }

        var metadata = new TermMetadata(iri, label, description, term);
        return metadata.IsEmpty ? null : metadata;
    }
}