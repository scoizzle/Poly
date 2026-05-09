namespace Poly.Data.Modeling.Recipes.Contracts;

public sealed record ContractImportOptions {
    public string? ContractName { get; init; }
    public ContractSourceKind SourceKind { get; init; } = ContractSourceKind.ExternalProvider;
    public ContractEndpointDirection DefaultDirection { get; init; } = ContractEndpointDirection.Outbound;
    public Func<string, string>? EndpointNameTransform { get; init; }
    public Func<string, string>? TypeNameTransform { get; init; }
    public bool IncludeMethodsWithoutPayload { get; init; }
}