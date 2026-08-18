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

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Extra output files from the analyzed domain. Libraries register these on the
/// session builder. The compiler asks them only after analysis succeeds.
/// </summary>
public interface IArtifactContributor {
    /// <summary>Produces additional files for <paramref name="domain"/>, or an empty
    /// list when this contributor has nothing to emit for the analyzed domain.</summary>
    IReadOnlyList<(string FileName, string Source)> Contribute(Domain domain, AnalysisResult analysis);
}