using System.Collections;

namespace Poly.DomainModeling.Ontology.Constraints;

/// <summary>One closed interval of a <see cref="RangeConstraint"/> union; a null bound is unbounded.</summary>
public readonly record struct RangeInterval(object? Minimum, object? Maximum);

/// <summary>
/// The ordered, normalised (disjoint, non-touching) intervals of a union range. Value equality,
/// so two unions with the same intervals are equal records.
/// </summary>
public sealed class RangeIntervals : IReadOnlyList<RangeInterval>, IEquatable<RangeIntervals> {
    private readonly RangeInterval[] _items;

    internal RangeIntervals(IEnumerable<RangeInterval> items) => _items = [.. items];

    public int Count => _items.Length;

    public RangeInterval this[int index] => _items[index];

    public bool Equals(RangeIntervals? other) =>
        other is not null && _items.AsSpan().SequenceEqual(other._items);

    public override bool Equals(object? obj) => obj is RangeIntervals o && Equals(o);

    public override int GetHashCode() {
        var h = new HashCode();
        foreach (var i in _items) h.Add(i);
        return h.ToHashCode();
    }

    public IEnumerator<RangeInterval> GetEnumerator() => ((IEnumerable<RangeInterval>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A numeric/ordered range. <see cref="Minimum"/>/<see cref="Maximum"/> are always the HULL, so
/// a reader that can only see one interval is sound for "which values are possible". A join of
/// disjoint ranges additionally carries <see cref="Intervals"/>, the exact union (#367 A-4);
/// null means a plain single interval.
/// </summary>
/// <remarks>
/// The bounds are declared (DSL-parsed) values: a <c>Number</c>/<c>Int</c>/<c>Int64</c> bound is a
/// <see cref="long"/>, and a <c>Decimal</c> or any other non-Number-family bound is a
/// <see cref="decimal"/> — never a <see cref="double"/> (#643). Derived bounds (analysis arithmetic)
/// carry other types.
/// </remarks>
public sealed record RangeConstraint(object? Minimum, object? Maximum) : Constraint {
    public RangeIntervals? Intervals { get; init; }
}