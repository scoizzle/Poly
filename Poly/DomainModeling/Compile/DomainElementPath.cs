using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Builds the written name path of a domain element (the same path shape as
/// <see cref="ArtifactId.Path"/>), used by analysis findings.
/// </summary>
internal static class DomainElementPath {
    /// <summary>
    /// Walks <paramref name="domain"/> for <paramref name="node"/> and returns the path of
    /// named ancestors ending at the nearest named node on that walk. When the node is not
    /// found under the domain, returns the domain name alone (fail closed to a real path).
    /// </summary>
    public static string Resolve(Domain domain, Node node) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(node);
        if (ReferenceEquals(domain, node))
            return domain.Name;

        var path = new List<string> { domain.Name };
        if (Walk(domain, node, path))
            return string.Join('/', path);
        return domain.Name;
    }

    private static bool Walk(Node current, Node target, List<string> path) {
        foreach (var child in current.Children) {
            if (child is null)
                continue;
            var named = NameOf(child);
            if (named is not null)
                path.Add(named);
            if (ReferenceEquals(child, target))
                return true;
            if (Walk(child, target, path))
                return true;
            if (named is not null)
                path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    private static string? NameOf(Node node) => node is DomainMember member ? member.Name : null;
}