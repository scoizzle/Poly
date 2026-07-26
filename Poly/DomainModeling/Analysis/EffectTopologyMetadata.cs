using Poly.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores cross-entity effect topology (create-in, cross-entity invoke, subscriptions).
/// Produced by <see cref="EffectTopologyPass"/>.
/// </summary>
public sealed record EffectTopologyMetadata(EffectTopology Topology) : IAnalysisMetadata;