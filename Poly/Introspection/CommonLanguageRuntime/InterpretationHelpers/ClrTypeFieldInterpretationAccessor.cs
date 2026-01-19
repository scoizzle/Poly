using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using System.Linq.Expressions;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

internal sealed record ClrTypeFieldInterpretationAccessor(Node Instance, ClrTypeField Field) : Node {
    public override TResult Transform<TResult>(ITransformer<TResult> transformer)
    {
        // Special handling for Expression transformers
        if (transformer is ITransformer<Expression> exprTransformer)
        {
            var fieldInfo = Field.FieldInfo;
            
            // For static fields, instance must be null
            if (fieldInfo.IsStatic)
            {
                var fieldExpr = Expression.Field(null, fieldInfo);
                return (TResult)(object)fieldExpr;
            }
            else
            {
                var instanceExpr = Instance.Transform(exprTransformer);
                var fieldExpr = Expression.Field(instanceExpr, fieldInfo);
                return (TResult)(object)fieldExpr;
            }
        }
        
        throw new NotSupportedException($"ClrTypeFieldInterpretationAccessor transformation is not supported for {typeof(TResult).Name}.");
    }

    public override string ToString() => $"{Instance}.{Field.Name}";
}