using Poly.DomainModeling.Ontology;
// Deliberately in the Poly.DomainModeling namespace (test assembly) so every test file
// that constructs a Domain (via `using Poly.DomainModeling;`) sees this factory.
namespace Poly.DomainModeling;

/// <summary>
/// Test-construction factory for <see cref="Domain"/>. The product <see cref="Domain"/>
/// ctor only accepts entity-owned navigations (2-arg: name + types); relationships live
/// on their source entities. This factory keeps the legacy flat (types, relationships)
/// construction usable from tests by redistributing relationships onto their source
/// entities' <see cref="Entity.Navigations"/>. Fail-closed: a relationship whose source
/// entity is not in <paramref name="types"/> throws.
/// </summary>
public static class DomainTestFactory {
    public static Domain Create(
        string name,
        IReadOnlyList<DomainType> types,
        IReadOnlyList<Relationship> relationships) {
        if (relationships.Count == 0)
            return new Domain(name, types);

        var bySource = relationships
            .GroupBy(r => r.Source.TypeName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var entityNames = types.OfType<Entity>().Select(static e => e.Name).ToHashSet(StringComparer.Ordinal);
        var orphan = bySource.Keys.FirstOrDefault(source => !entityNames.Contains(source));
        if (orphan is not null)
            throw new ArgumentException(
                $"Relationship '{relationships.First(r => string.Equals(r.Source.TypeName, orphan, StringComparison.Ordinal)).Name}' " +
                $"references source entity '{orphan}' which is not defined in the domain.");

        return new Domain(name, types.Select(t => t is Entity e && bySource.TryGetValue(e.Name, out var rels)
            ? e with { Navigations = [.. e.Navigations, .. rels] }
            : t).ToList());
    }

    public static Domain Create(string name, IReadOnlyList<DomainType> types) =>
        new Domain(name, types);
}