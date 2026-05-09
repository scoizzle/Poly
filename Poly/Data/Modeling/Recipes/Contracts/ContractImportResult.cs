using Poly.Data.Modeling.TypeSystem;
using Poly.Syntax.Analysis;

namespace Poly.Data.Modeling.Recipes.Contracts;

public sealed record ContractImportResult(
    ImportedContract Contract,
    IReadOnlyList<ContractEndpoint> Endpoints,
    IReadOnlyList<DomainType> CreatedTypes,
    AnalysisResult Analysis);