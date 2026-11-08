using Poly.DomainModeling.Language;

namespace Poly.Compile;

/// <summary>
/// Finds the outline node for a domain element path. The outline spells paths with dots and the
/// domain with slashes, so this walks the outline by name segment and never compares the two
/// spellings as strings.
/// </summary>
internal static class SpanBridge {
    /// <summary>
    /// <paramref name="domainPath"/> is <c>Domain/Entity/Member…</c>. Returns the deepest node the
    /// segments reach, or null when not even the first segment after the domain name is found.
    /// </summary>
    public static OutlineNode? Locate(DslOutline outline, string domainPath) {
        var segments = domainPath.Split('/');
        IReadOnlyList<OutlineNode> level = outline.Roots;
        OutlineNode? found = null;
        foreach (var segment in segments.Skip(1)) {
            var next = level.FirstOrDefault(n =>
                string.Equals(n.Name, segment, StringComparison.Ordinal)
                && n.Kind is not ("domain" or "uses"));
            if (next is null)
                break;
            found = next;
            level = next.Children;
        }
        return found;
    }
}