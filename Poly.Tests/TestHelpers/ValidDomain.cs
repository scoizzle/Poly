using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;
namespace Poly.DomainModeling;

/// <summary>
/// Hand-built <see cref="Domain"/> that analyzes cleanly. A parsed domain starts with the canonical
/// built-in primitives (<see cref="CanonicalBuiltInTypeCatalog"/>) plus the primitives of each extension it
/// imports; a domain built in code has none, so Analyze reports "unknown type 'Text'". This declares exactly
/// those and nothing else, from the same sources the DSL loader uses.
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
