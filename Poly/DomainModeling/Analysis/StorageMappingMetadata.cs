using Poly.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores storage mapping metadata: columns, navigations, FKs, keys, table names.
/// Produced by <see cref="StoragePass"/>.
/// </summary>
public sealed record StorageMappingMetadata(StorageModel Storage) : IAnalysisMetadata;