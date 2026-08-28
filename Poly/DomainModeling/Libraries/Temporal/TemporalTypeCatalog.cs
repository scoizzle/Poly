using Poly.DomainModeling.Evolution;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Primitive types owned by the temporal library. Seeded only when
/// <c>uses temporal</c> is imported (product language default includes it).
/// </summary>
public static class TemporalTypeCatalog {
    public static IReadOnlyList<(string Name, TypeCategory Category)> Definitions { get; } =
    [
        ("Date",     TypeCategory.Primitive | TypeCategory.DateOnly),
        ("Time",     TypeCategory.Primitive | TypeCategory.TimeOfDay),
        ("DateTime", TypeCategory.Primitive | TypeCategory.DateTime),
        ("Duration", TypeCategory.Primitive | TypeCategory.Duration),
    ];

    public static IReadOnlyList<DomainChange> CreateChanges() =>
        Definitions.Select(d => new AddPrimitiveTypeChange(d.Name, d.Category, [])).ToList();
}