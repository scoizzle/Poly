namespace Poly.Interpretation.Analysis;

public sealed class AnalysisContext(ITypeDefinitionProvider typeDefinitions) : ITypedMetadataProvider {
    public TypedMetadataStore Metadata { get; } = new();
    public ITypeDefinitionProvider TypeDefinitions { get; } = typeDefinitions;

    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class => Metadata.Get<TMetadata>();
}
