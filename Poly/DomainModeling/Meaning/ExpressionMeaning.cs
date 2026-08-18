using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
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

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Per-session meaning tables for library-owned expressions. Empty when the unit
/// did not load the owning library — pack IR then fails closed at rewrite/lower/check.
/// </summary>
public sealed class ExpressionMeaning {
    public static ExpressionMeaning Empty { get; } = new();

    public ExpressionDispatchRegistry<DomainExpression> Rewrite { get; } = new();

    public ExpressionDispatchRegistry<Node> Lowering { get; } = new();

    internal ExpressionDispatchRegistry<ExpressionTypeAnalyzer.TypeCategory> Inference { get; } = new();

    public ExpressionTypeCheckRegistry Checks { get; } = new();

    public ExpressionDefaultResolverRegistry Defaults { get; } = new();
}