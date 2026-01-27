namespace Poly.Interpretation.Analysis;

public sealed record AnalysisResult : ITypedMetadataProvider {
    private readonly TypedMetadataStore _metadata;

    public AnalysisResult(TypedMetadataStore metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
    }

    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class => _metadata.Get<TMetadata>();
}
