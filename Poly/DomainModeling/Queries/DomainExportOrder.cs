using Poly.DomainModeling.Ontology;

using Entity = Poly.DomainModeling.Ontology.Entity;
using EnumType = Poly.DomainModeling.Ontology.EnumType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// Declaration order for document projections (Q12). Projections list what the model lists, in
/// the order it lists it; nothing here sorts for determinism.
/// </summary>
internal static class DomainExportOrder {
    /// <summary>Entities, value types and enums in <see cref="Domain.Types"/> order; primitive types excluded.</summary>
    public static IReadOnlyList<DomainType> Types(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return [.. domain.Types.Where(t => t is Entity or ValueType or EnumType)];
    }
}