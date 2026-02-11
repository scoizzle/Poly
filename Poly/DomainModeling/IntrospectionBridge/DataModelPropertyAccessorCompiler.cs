using System.Linq.Expressions;

using Poly.Interpretation.LinqExpressions;

namespace Poly.DomainModeling.IntrospectionBridge;

/// <summary>
/// Compiles <see cref="DataModelPropertyAccessor"/> nodes to dictionary indexer access expressions.
/// </summary>
/// <remarks>
/// Transforms property access on DataModel types (backed by IDictionary&lt;string, object&gt;) 
/// into the appropriate dictionary indexer: <c>dict["PropertyName"]</c>
/// </remarks>
public sealed class DataModelPropertyAccessorCompiler : INodeCompiler {
    public bool TryCompile(Node node, Func<Node, Expression> compileChild, out Expression? expression)
    {
        if (node is not DataModelPropertyAccessor accessor) {
            expression = null;
            return false;
        }

        // Compile the instance (should be a dictionary)
        var instanceExpr = compileChild(accessor.Instance);

        // Generate dictionary indexer access: dict["PropertyName"]
        var indexer = instanceExpr.Type.GetProperty("Item");
        if (indexer != null) {
            expression = Expression.Property(
                instanceExpr,
                indexer,
                Expression.Constant(accessor.PropertyName)
            );
        }
        else {
            // Fallback: try MakeIndex
            expression = Expression.MakeIndex(
                instanceExpr,
                indexer,
                [Expression.Constant(accessor.PropertyName)]
            );
        }

        // Cast to the expected member type if needed
        if (expression.Type == typeof(object) && accessor.MemberType.ReflectedType != typeof(object)) {
            expression = Expression.Convert(expression, accessor.MemberType.ReflectedType);
        }

        return true;
    }
}