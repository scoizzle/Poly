using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Primitive types owned by the temporal library. Seeded only when
/// <c>uses temporal</c> is imported (product language default includes it).
/// </summary>
public static class TemporalTypeCatalog {
    public static IReadOnlyList<(string Name, PrimitiveSemantics Semantics)> Definitions { get; } =
    [
        ("Date",     PrimitiveSemantics.Date),
        ("Time",     PrimitiveSemantics.Time),
        ("DateTime", PrimitiveSemantics.DateTime),
        ("Duration", PrimitiveSemantics.Duration),
    ];

    public static IReadOnlyList<DomainChange> CreateChanges() =>
        Definitions.Select(d => new AddPrimitiveTypeChange(d.Name, d.Semantics, [])).ToList();
}