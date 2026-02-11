namespace Poly.Interpretation;

/// <summary>
/// Metadata objects implementing this interface can clear cached data for specific nodes.
/// Used during incremental analysis to invalidate stale metadata when nodes are modified.
/// </summary>
public interface IAnalysisMetadata {
}