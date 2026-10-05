using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;
namespace Poly.DomainModeling;

/// <summary>
/// Builds a domain with the primitives a parsed domain starts with: the canonical built-ins
/// (<see cref="CanonicalBuiltInTypeCatalog"/>) plus the primitive seeds of each core extension listed.
/// A domain built with <c>new Domain(..)</c> or <see cref="DomainTestFactory"/> has none, so Analyze
/// reports "unknown type 'Text'".
/// It validates nothing else: a test may pass invalid types or relationships on purpose, and the
/// result then has analysis errors.
/// An extension id outside <see cref="ExtensionCatalog.Core"/> is recorded but adds no primitives, as
/// <see cref="Evolution.AddDomainExtensionChange"/> does. Unlike <c>DomainFactory.Create</c>,
/// extensions default to none.
/// </summary>
public static class ValidDomain {
    public static Domain Create(
        string name,
        IReadOnlyList<DomainType> types,
        IReadOnlyList<Relationship>? relationships = null,
        IReadOnlyList<string>? extensions = null) {
        extensions ??= [];
        var extensionPrimitives = extensions
            .Where(ExtensionCatalog.Core.Contains)
            .SelectMany(id => ExtensionCatalog.Core.Resolve(id).PrimitiveSeeds);
        var primitives = CanonicalBuiltInTypeCatalog.Definitions.Concat(extensionPrimitives)
            .DistinctBy(p => p.Name)
            .Select(p => new PrimitiveType(p.Name, p.Category, []));
        return DomainTestFactory.Create(name, [.. primitives, .. types], relationships ?? []) with { Extensions = [.. extensions] };
    }
}
