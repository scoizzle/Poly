namespace Poly.Interpretation;

public interface INodeMetadataProvider {
    public TMetadata? GetMetadata<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata;
}