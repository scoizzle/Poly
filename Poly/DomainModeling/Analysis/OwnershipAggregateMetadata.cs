namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores ownership hierarchy metadata: which entities own which.
/// Produced by <see cref="OwnershipAggregatePass"/>.
/// </summary>
public sealed record OwnershipAggregateMetadata(AggregateModel Aggregate) : IAnalysisMetadata;