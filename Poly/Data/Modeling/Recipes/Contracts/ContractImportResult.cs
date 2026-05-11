using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Recipes.Contracts;

public sealed record ContractImportResult(
    ImportedContract Contract,
    IReadOnlyList<ContractEndpoint> Endpoints,
    IReadOnlyList<DomainType> CreatedTypes,
    AnalysisResult Analysis);