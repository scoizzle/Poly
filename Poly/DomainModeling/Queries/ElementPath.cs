namespace Poly.DomainModeling.Queries;

/// <summary>
/// A parsed address of one domain element: <c>Entity</c>, <c>Entity.Member</c>,
/// <c>Entity.Stage.Action</c>, or the name of an enum, value type or function. Both <c>.</c> and
/// <c>/</c> separate segments, so a dotted select and an analysis path parse to the same value.
/// </summary>
/// <remarks>
/// Parsing is domain-free: the domain-name prefix is handled by <see cref="ElementPathResolver"/>.
/// This is the one grammar that supersedes <c>PolySurfaceTools.NormalizeSelect</c>/<c>ElementExists</c>/
/// <c>PathMatches</c> and <c>SelectScope.Parse</c>/<c>NamesAnElement</c>. It is not the grammar of
/// <c>DomainFailure.ElementPath</c> (a runtime instance path).
/// </remarks>
internal sealed class ElementPath {
    private ElementPath(IReadOnlyList<string> segments) => Segments = segments;

    public IReadOnlyList<string> Segments { get; }

    /// <summary>The dotted form.</summary>
    public string Canonical => string.Join('.', Segments);

    public override string ToString() => Canonical;

    /// <summary>
    /// Splits on <c>.</c> and <c>/</c> and trims each segment. An empty segment is an error that names
    /// the character position it starts at. Nothing else is validated: identifiers cannot contain a
    /// separator and there is no escaping.
    /// </summary>
    public static bool TryParse(string? text, out ElementPath? path, out string? error) {
        path = null;
        if (string.IsNullOrWhiteSpace(text)) {
            error = "Element path is empty (position 0).";
            return false;
        }

        var segments = new List<string>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++) {
            if (i < text.Length && text[i] != '.' && text[i] != '/')
                continue;

            var segment = text.AsSpan(start, i - start).Trim();
            if (segment.IsEmpty) {
                error = $"Element path '{text}' has an empty segment at position {start}.";
                return false;
            }

            segments.Add(segment.ToString());
            start = i + 1;
        }

        path = new ElementPath(segments);
        error = null;
        return true;
    }

    public static ElementPath Parse(string text) =>
        TryParse(text, out var path, out var error) ? path! : throw new FormatException(error);
}