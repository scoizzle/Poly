namespace Poly.Interpretation;

/// <summary>
/// Metadata objects implementing this interface can clear cached data for specific nodes.
/// Used during incremental analysis to invalidate stale metadata when nodes are modified.
/// </summary>
public interface IAnalysisMetadata {
    /// <summary>
    /// Clears all cached data associated with the specified node.
    /// </summary>
    /// <param name="nodeId">The ID of the node whose metadata should be cleared.</param>
    void ClearNodeCache(NodeId nodeId) { }
}