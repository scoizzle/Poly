using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Language;
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

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Temporal concepts (clocks, duration units) on existing expression shapes.
/// Loaded via <see cref="ExtensionCatalog"/>; meaning is session-scoped.
/// </summary>
public sealed class TemporalLibrary : IDomainLibrary {
    public string Id => "temporal";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        TemporalDispatchRegistration.Populate(builder.Meaning);
        builder.ExpressionForms.RegisterBinaryFold(new DateOperationFold());
        TemporalExpressionPrintBinders.Register(builder.ExpressionForms);
        builder.ExpressionForms.RegisterGrammarContributor(TemporalExpressionPrintBinders.ContributeGrammarPatterns);
    }
}