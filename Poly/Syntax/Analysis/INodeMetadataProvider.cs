namespace Poly.Syntax.Analysis;

public interface INodeMetadataProvider {
    TMetadata? GetMetadata<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata;
}