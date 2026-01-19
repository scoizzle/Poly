using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using System.Linq.Expressions;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

internal sealed record ClrTypeIndexInterpretationAccessor(Node Instance, ClrTypeProperty IndexProperty, params IEnumerable<Node> IndexParameters) : Node {
    public override TResult Transform<TResult>(ITransformer<TResult> transformer)
    {
        // Special handling for Expression transformers
        if (transformer is ITransformer<Expression> exprTransformer)
        {
            var instanceExpr = Instance.Transform(exprTransformer);
            var indexExprs = IndexParameters.Select(idx => idx.Transform(exprTransformer)).ToArray();
            var propertyInfo = IndexProperty.PropertyInfo;
            var indexExpr = Expression.Property(instanceExpr, propertyInfo, indexExprs);
            
            return (TResult)(object)indexExpr;
        }
        
        throw new NotSupportedException($"ClrTypeIndexInterpretationAccessor transformation is not supported for {typeof(TResult).Name}.");
    }

    public override string ToString() => $"{Instance}[{string.Join(", ", IndexParameters)}]";
}