using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Stores per-entity action metadata (parameters, return types, effective policies, stage transitions).
/// Produced by <see cref="BehaviorPass"/>.
/// </summary>
public sealed record BehaviorMetadata(BehaviorModel Behavior) : IAnalysisMetadata;