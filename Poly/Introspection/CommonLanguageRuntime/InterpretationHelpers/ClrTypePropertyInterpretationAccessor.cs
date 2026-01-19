using Poly.Extensions;
using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using System.Linq.Expressions;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

internal sealed record ClrTypePropertyInterpretationAccessor(Node Instance, ClrTypeProperty Property) : Node {
    public override TResult Transform<TResult>(ITransformer<TResult> transformer)
    {
        // Special handling for Expression transformers
        if (transformer is ITransformer<Expression> exprTransformer)
        {
            var propertyInfo = Property.PropertyInfo;
            
            // For static properties, instance must be null
            if (propertyInfo.GetMethod?.IsStatic == true || propertyInfo.SetMethod?.IsStatic == true)
            {
                var propertyExpr = Expression.Property(null, propertyInfo);
                return (TResult)(object)propertyExpr;
            }
            else
            {
                var instanceExpr = Instance.Transform(exprTransformer);
                var propertyExpr = Expression.Property(instanceExpr, propertyInfo);
                return (TResult)(object)propertyExpr;
            }
        }
        
        throw new NotSupportedException($"ClrTypePropertyInterpretationAccessor transformation is not supported for {typeof(TResult).Name}.");
    }

    public override string ToString() => $"{Instance}.{Property.Name}";
}