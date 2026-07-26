using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores the entity dependency graph — directed edges from navigations + subscriptions.
/// Produced by <see cref="CrossReferencePass"/>.
/// </summary>
public sealed record EntityDependencyGraphMetadata(
    IReadOnlyList<EntityDependencyEdge> Edges,
    IReadOnlyList<string> CycleEntityNames
) : IAnalysisMetadata;

/// <summary>
/// A directed dependency edge from one entity to another.
/// </summary>
/// <param name="From">The entity that depends on <paramref name="To"/>.</param>
/// <param name="To">The entity that <paramref name="From"/> depends on.</param>
/// <param name="Kind">Why this edge exists.</param>
public sealed record EntityDependencyEdge(
    string From,
    string To,
    string Kind
);