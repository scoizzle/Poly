namespace Poly.DomainModeling.Contradictions;

/// <summary>What is incompatible about a flow. One flow can carry several kinds.</summary>
public enum ContradictionKind { WidthNarrowing, ScaleNarrowing, Nullability, TypeCoercion }

/// <remarks>
/// <see cref="Kinds"/> is empty for a resolved or withdrawn claim whose facets now agree;
/// those entries stay in the bag as history and do not block.
/// </remarks>
public sealed record ContradictionEntry(
    string Name,
    IReadOnlyList<ContradictionKind> Kinds,
    string SourceElementPath,
    string TargetElementPath,
    ContradictionShape SourceShape,
    ContradictionShape TargetShape,
    IReadOnlyList<string> ClaimIds,
    ContradictionStatus Status);

/// <summary>The engine's verdict on the bound claims. Not a diagnostic: it survives a clean analysis.</summary>
public sealed record ContradictionBag(IReadOnlyList<ContradictionEntry> Entries) {
    public static ContradictionBag Empty { get; } = new([]);

    public IReadOnlyList<ContradictionEntry> Open => [.. Entries.Where(e => e.Status == ContradictionStatus.Open)];
}