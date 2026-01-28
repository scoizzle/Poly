namespace Poly.Interpretation.Analysis;

public sealed class AnalysisContext(ITypeDefinitionProvider typeDefinitions) : ITypedMetadataProvider {
    public TypedMetadataStore Metadata { get; } = new();
    public ITypeDefinitionProvider TypeDefinitions { get; } = typeDefinitions;

    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class => Metadata.Get<TMetadata>();

    public TMetadata GetOrAddMetadata<TMetadata>(Func<TMetadata> factory) where TMetadata : class => Metadata.GetOrAdd(factory);

    public void SetMetadata<TMetadata>(TMetadata metadata) where TMetadata : class => Metadata.Set(metadata);
}
