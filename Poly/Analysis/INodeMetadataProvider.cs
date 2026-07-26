namespace Poly.Analysis;

/// <summary>
/// Provides access to analysis metadata keyed (optionally) by node.
/// When <c>node</c> is <c>null</c>, the metadata is indexed under
/// <see cref="NodeId.Empty"/> — a sentinel for pass-level or
/// analysis-level data not tied to any single AST node.
/// </summary>
public interface INodeMetadataProvider {
    TMetadata? GetMetadata<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata;
}