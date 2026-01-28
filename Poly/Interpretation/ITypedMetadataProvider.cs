namespace Poly.Interpretation;

public interface ITypedMetadataProvider
{
    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class;
}