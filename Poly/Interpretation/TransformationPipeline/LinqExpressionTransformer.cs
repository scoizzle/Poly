using System.Linq.Expressions;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Interpretation.SemanticAnalysis;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation;

/// <summary>
/// Transformer that compiles AST expressions to System.Linq.Expressions.Expression trees.
/// Uses C# semantics by default (blocks don't return values, assignments don't return values).
/// </summary>
public class LinqExpressionTransformer : ITransformer<Expression>
{
    private readonly Dictionary<string, ParameterExpression> _variables = new();
    private InterpretationContext? _context;

    public static LinqExpressionTransformer Shared { get; } = new();

    public IEnumerable<ParameterExpression> ParameterExpressions => _variables.Values;

    /// <summary>
    /// Sets the interpretation context to use for semantic analysis during transformation.
    /// </summary>
    public void SetContext(InterpretationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Gets the ParameterExpression for a Parameter node from the given context.
    /// Ensures the same Parameter node always maps to the same ParameterExpression within that context.
    /// </summary>
    /// <param name="parameter">The parameter node to get the expression for.</param>
    /// <param name="context">The interpretation context managing parameter expressions.</param>
    /// <returns>The cached or newly created ParameterExpression.</returns>
    public static ParameterExpression GetParameterExpression(Parameter parameter, InterpretationContext context)
    {
        return context.GetOrCreateParameterExpression(parameter);
    }

    /// <inheritdoc />
    public Expression Unit => Expression.Empty();

    // ITransformer implementation
    public Expression Transform<T>(Constant<T> constant)
    {
        return Expression.Constant(constant.Value);
    }

    public Expression Transform(Variable variable)
    {
        if (!_variables.TryGetValue(variable.Name, out var paramExpr))
        {
            throw new InvalidOperationException($"Variable '{variable.Name}' is not declared.");
        }
        return paramExpr;
    }

    public Expression Transform(Parameter parameter)
    {
        // Use context-based caching to ensure the same Parameter node
        // always maps to the same ParameterExpression within this context
        if (_context == null)
        {
            throw new InvalidOperationException(
                "LinqExpressionTransformer.SetContext() must be called before transforming parameters.");
        }

        var paramExpr = GetParameterExpression(parameter, _context);
        _variables[parameter.Name] = paramExpr;
        return paramExpr;
    }

    public Expression Transform(Add add)
    {
        var left = Transform(add.LeftHandValue);
        var right = Transform(add.RightHandValue);
        
        // Use type promotion if context is available
        if (_context != null)
        {
            var leftType = _context.GetResolvedType(add.LeftHandValue);
            var rightType = _context.GetResolvedType(add.RightHandValue);
            
            if (leftType != null && rightType != null)
            {
                var (promotedLeft, promotedRight) = NumericTypePromotion.ConvertToPromotedType(
                    _context, left, right, leftType, rightType);
                return Expression.Add(promotedLeft, promotedRight);
            }
        }
        
        return Expression.Add(left, right);
    }

    public Expression Transform(Subtract subtract)
    {
        var left = Transform(subtract.LeftHandValue);
        var right = Transform(subtract.RightHandValue);
        
        // Use type promotion if context is available
        if (_context != null)
        {
            var leftType = _context.GetResolvedType(subtract.LeftHandValue);
            var rightType = _context.GetResolvedType(subtract.RightHandValue);
            
            if (leftType != null && rightType != null)
            {
                var (promotedLeft, promotedRight) = NumericTypePromotion.ConvertToPromotedType(
                    _context, left, right, leftType, rightType);
                return Expression.Subtract(promotedLeft, promotedRight);
            }
        }
        
        return Expression.Subtract(left, right);
    }

    public Expression Transform(Multiply multiply)
    {
        var left = Transform(multiply.LeftHandValue);
        var right = Transform(multiply.RightHandValue);
        
        // Use type promotion if context is available
        if (_context != null)
        {
            var leftType = _context.GetResolvedType(multiply.LeftHandValue);
            var rightType = _context.GetResolvedType(multiply.RightHandValue);
            
            if (leftType != null && rightType != null)
            {
                var (promotedLeft, promotedRight) = NumericTypePromotion.ConvertToPromotedType(
                    _context, left, right, leftType, rightType);
                return Expression.Multiply(promotedLeft, promotedRight);
            }
        }
        
        return Expression.Multiply(left, right);
    }

    public Expression Transform(Divide divide)
    {
        var left = Transform(divide.LeftHandValue);
        var right = Transform(divide.RightHandValue);
        
        // Use type promotion if context is available
        if (_context != null)
        {
            var leftType = _context.GetResolvedType(divide.LeftHandValue);
            var rightType = _context.GetResolvedType(divide.RightHandValue);
            
            if (leftType != null && rightType != null)
            {
                var (promotedLeft, promotedRight) = NumericTypePromotion.ConvertToPromotedType(
                    _context, left, right, leftType, rightType);
                return Expression.Divide(promotedLeft, promotedRight);
            }
        }
        
        return Expression.Divide(left, right);
    }

    public Expression Transform(Modulo modulo)
    {
        var left = Transform(modulo.LeftHandValue);
        var right = Transform(modulo.RightHandValue);
        
        // Use type promotion if context is available
        if (_context != null)
        {
            var leftType = _context.GetResolvedType(modulo.LeftHandValue);
            var rightType = _context.GetResolvedType(modulo.RightHandValue);
            
            if (leftType != null && rightType != null)
            {
                var (promotedLeft, promotedRight) = NumericTypePromotion.ConvertToPromotedType(
                    _context, left, right, leftType, rightType);
                return Expression.Modulo(promotedLeft, promotedRight);
            }
        }
        
        return Expression.Modulo(left, right);
    }

    public Expression Transform(UnaryMinus unaryMinus)
    {
        var operand = Transform(unaryMinus.Operand);
        return Expression.Negate(operand);
    }

    public Expression Transform(Equal equal)
    {
        var left = Transform(equal.LeftHandValue);
        var right = Transform(equal.RightHandValue);
        return Expression.Equal(left, right);
    }

    public Expression Transform(NotEqual notEqual)
    {
        var left = Transform(notEqual.LeftHandValue);
        var right = Transform(notEqual.RightHandValue);
        return Expression.NotEqual(left, right);
    }

    public Expression Transform(LessThan lessThan)
    {
        var left = Transform(lessThan.LeftHandValue);
        var right = Transform(lessThan.RightHandValue);
        return Expression.LessThan(left, right);
    }

    public Expression Transform(LessThanOrEqual lessThanOrEqual)
    {
        var left = Transform(lessThanOrEqual.LeftHandValue);
        var right = Transform(lessThanOrEqual.RightHandValue);
        return Expression.LessThanOrEqual(left, right);
    }

    public Expression Transform(GreaterThan greaterThan)
    {
        var left = Transform(greaterThan.LeftHandValue);
        var right = Transform(greaterThan.RightHandValue);
        return Expression.GreaterThan(left, right);
    }

    public Expression Transform(GreaterThanOrEqual greaterThanOrEqual)
    {
        var left = Transform(greaterThanOrEqual.LeftHandValue);
        var right = Transform(greaterThanOrEqual.RightHandValue);
        return Expression.GreaterThanOrEqual(left, right);
    }

    public Expression Transform(And and)
    {
        var left = Transform(and.LeftHandValue);
        var right = Transform(and.RightHandValue);
        return Expression.AndAlso(left, right);
    }

    public Expression Transform(Or or)
    {
        var left = Transform(or.LeftHandValue);
        var right = Transform(or.RightHandValue);
        return Expression.OrElse(left, right);
    }

    public Expression Transform(Not not)
    {
        var operand = Transform(not.Value);
        return Expression.Not(operand);
    }

    public Expression Transform(Conditional conditional)
    {
        var test = Transform(conditional.Condition);
        var ifTrue = Transform(conditional.IfTrue);
        var ifFalse = Transform(conditional.IfFalse);
        return Expression.Condition(test, ifTrue, ifFalse);
    }

    public Expression Transform(MemberAccess memberAccess)
    {
        var target = Transform(memberAccess.Value);
        return Expression.PropertyOrField(target, memberAccess.MemberName);
    }

    public Expression Transform(IndexAccess indexAccess)
    {
        var target = Transform(indexAccess.Value);
        var indices = indexAccess.Arguments.Select(Transform).ToArray();
        
        // Check if it's an array (use ArrayIndex) or an indexer property (use MakeIndex)
        if (target.Type.IsArray)
        {
            return Expression.ArrayIndex(target, indices);
        }
        else
        {
            // Use PropertyOrField indexer for non-arrays
            // Try to find an indexer property
            var indexerProperty = target.Type.GetProperties()
                .FirstOrDefault(p => p.GetIndexParameters().Length > 0);
            
            if (indexerProperty != null)
            {
                return Expression.MakeIndex(target, indexerProperty, indices);
            }
            
            // Fallback to array index for arrays
            return Expression.ArrayIndex(target, indices);
        }
    }

    public Expression Transform(MethodInvocation invocation)
    {
        var target = Transform(invocation.Target);
        var arguments = invocation.Arguments.Select(Transform).ToArray();
        return Expression.Call(target, invocation.MethodName, Type.EmptyTypes, arguments);
    }

    public Expression Transform(Assignment assignment)
    {
        var left = Transform(assignment.Destination);
        var right = Transform(assignment.Value);

        // In C#, assignment is a statement that returns the assignment itself
        return Expression.Assign(left, right);
    }

    public Expression Transform(Block block)
    {
        var expressions = block.Nodes.OfType<Node>().Select(Transform).ToArray();
        var variables = block.Variables.Select(v => (ParameterExpression)Transform(v)).ToArray();

        // In C#, blocks execute statements but don't return the last expression's value
        return Expression.Block(variables, expressions);
    }

    public Expression Transform(TypeCast typeCast)
    {
        var operand = Transform(typeCast.Operand);
        var type = typeCast.TargetType.ReflectedType;
        return typeCast.IsChecked
            ? Expression.ConvertChecked(operand, type)
            : Expression.Convert(operand, type);
    }

    public Expression Transform(Coalesce coalesce)
    {
        var left = Transform(coalesce.LeftHandValue);
        var right = Transform(coalesce.RightHandValue);
        return Expression.Coalesce(left, right);
    }

    private Expression Transform(Node expression) => expression.Transform(this);

    /// <summary>
    /// TEMPORARY: Gets the type of a node for compatibility with existing code.
    /// </summary>
    [Obsolete("Use semantic analysis middleware for type information.")]
    public ITypeDefinition? GetNodeType(Node node)
    {
        // In the middleware architecture, type information comes from semantic analysis middleware
        // This method is here only for backward compatibility
        return null;
    }
}
