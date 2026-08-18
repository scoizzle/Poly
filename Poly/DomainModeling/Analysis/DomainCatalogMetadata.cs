using Poly.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Name→member catalog published by <see cref="DomainCatalogPass"/>.
/// Product lookups go through <see cref="DomainSemanticLookupExtensions"/>.
/// </summary>
internal sealed record DomainCatalogMetadata(
    Domain Domain,
    DomainTypeLookupMetadata Types,
    RelationshipLookupMetadata Relationships,
    MutationTargetIndexMetadata Index,
    IReadOnlyDictionary<string, ActionResolutionMetadata> ActionsByEntityName
) : IAnalysisMetadata;