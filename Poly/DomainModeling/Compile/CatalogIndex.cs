using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Read-only name lookup over one catalog's entity and scaffolding trees.
/// Built from the catalog as it stands; later registrations are not visible.
/// </summary>
public sealed class CatalogIndex {
    private readonly Dictionary<string, Artifact> _trees;

    public CatalogIndex(ArtifactCatalog catalog) {
        ArgumentNullException.ThrowIfNull(catalog);
        var trees = new Dictionary<string, Artifact>(StringComparer.Ordinal);
        foreach (var artifact in catalog.Artifacts) {
            var id = artifact.Descriptor.Id;
            if (id.Type is not ("entity" or "scaffolding"))
                continue;
            var name = id.Segments[^1];
            if (id.Type == "entity")
                trees[name] = artifact;
            else
                trees.TryAdd(name, artifact);
        }
        _trees = trees;
    }

    /// <summary>
    /// The entity or scaffolding tree whose last path segment is <paramref name="name"/>, or null.
    /// When an entity and the domain share a name, the entity tree.
    /// </summary>
    public Artifact? Tree(string name) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _trees.GetValueOrDefault(name);
    }

    /// <summary>The method with this name on <paramref name="treeName"/>'s tree, or null.</summary>
    public MethodDefinitionNode? Method(string treeName, string methodName) {
        ArgumentException.ThrowIfNullOrEmpty(treeName);
        ArgumentException.ThrowIfNullOrEmpty(methodName);
        if (Tree(treeName)?.Payload is not IReadOnlyList<TypeDefinitionNode> types)
            return null;
        foreach (var type in types) {
            if (type.Methods is null)
                continue;
            foreach (var method in type.Methods) {
                if (string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    return method;
            }
        }
        return null;
    }

    /// <summary>The property with this name on <paramref name="treeName"/>'s tree, or null.</summary>
    public PropertyDefinitionNode? Navigation(string treeName, string memberName) {
        ArgumentException.ThrowIfNullOrEmpty(treeName);
        ArgumentException.ThrowIfNullOrEmpty(memberName);
        if (Tree(treeName)?.Payload is not IReadOnlyList<TypeDefinitionNode> types)
            return null;
        foreach (var type in types) {
            if (type.Properties is null)
                continue;
            foreach (var property in type.Properties) {
                if (string.Equals(property.Name, memberName, StringComparison.Ordinal))
                    return property;
            }
        }
        return null;
    }
}
