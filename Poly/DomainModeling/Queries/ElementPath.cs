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

    public static bool TryParse(string? text, out ElementPath? path, out string? error) {
        path = null;
        error = null;
        return false;
    }

    public static ElementPath Parse(string text) => new([]);
}