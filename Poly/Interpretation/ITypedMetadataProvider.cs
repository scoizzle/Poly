namespace Poly.Interpretation;

public interface ITypedMetadataProvider {
    public TMetadata? GetMetadata<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata;
}