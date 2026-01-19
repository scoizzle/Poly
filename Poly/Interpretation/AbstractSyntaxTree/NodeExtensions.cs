using Poly.Introspection;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Interpretation.SemanticAnalysis;

namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Extension methods for <see cref="Node"/> that provide a fluent API for building expressions.
/// </summary>
public static class NodeExtensions
{
    #region Member Access

    /// <summary>
    /// Creates a member access operation for accessing a member of this expression.
    /// </summary>
    /// <param name="instance">The instance expression.</param>
    /// <param name="memberName">The name of the member to access (property, field, or method).</param>
    /// <returns>A <see cref="MemberAccess"/> operator representing the member access.</returns>
    public static MemberAccess GetMember(this Node instance, string memberName) => new MemberAccess(instance, memberName);

    /// <summary>
    /// Creates an index access operation for accessing an indexed member of this expression.
    /// </summary>
    /// <param name="instance">The instance expression.</param>
    /// <param name="indices">The index arguments.</param>
    /// <returns>An <see cref="IndexAccess"/> operator representing the index access.</returns>
    public static IndexAccess Index(this Node instance, params Node[] indices) => new IndexAccess(instance, indices);

    /// <summary>
    /// Creates a method invocation operation for calling a method on this expression.
    /// </summary>
    /// <param name="instance">The instance expression.</param>
    /// <param name="methodName">The name of the method to invoke.</param>
    /// <param name="arguments">The method arguments.</param>
    /// <returns>An <see cref="MethodInvocation"/> representing the method invocation.</returns>
    public static MethodInvocation Invoke(this Node instance, string methodName, params Node[] arguments) => new MethodInvocation(instance, methodName, arguments);

    #endregion

    #region Arithmetic Operations

    /// <summary>
    /// Creates an addition operation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to add to this expression.</param>
    /// <returns>An <see cref="Add"/> operator.</returns>
    public static Add Add(this Node left, Node right) => new Add(left, right);

    /// <summary>
    /// Creates a subtraction operation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to subtract from this expression.</param>
    /// <returns>A <see cref="Subtract"/> operator.</returns>
    public static Subtract Subtract(this Node left, Node right) => new Subtract(left, right);

    /// <summary>
    /// Creates a multiplication operation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to multiply with this expression.</param>
    /// <returns>A <see cref="Multiply"/> operator.</returns>
    public static Multiply Multiply(this Node left, Node right) => new Multiply(left, right);

    /// <summary>
    /// Creates a division operation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to divide this expression by.</param>
    /// <returns>A <see cref="Divide"/> operator.</returns>
    public static Divide Divide(this Node left, Node right) => new Divide(left, right);

    /// <summary>
    /// Creates a modulo operation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The divisor expression.</param>
    /// <returns>A <see cref="Modulo"/> operator.</returns>
    public static Modulo Modulo(this Node left, Node right) => new Modulo(left, right);

    /// <summary>
    /// Creates a unary negation operation.
    /// </summary>
    /// <param name="operand">The operand.</param>
    /// <returns>A <see cref="UnaryMinus"/> operator.</returns>
    public static UnaryMinus Negate(this Node operand) => new UnaryMinus(operand);

    #endregion

    #region Comparison Operations

    /// <summary>
    /// Creates a greater-than comparison.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to compare against.</param>
    /// <returns>A <see cref="GreaterThan"/> operator.</returns>
    public static GreaterThan GreaterThan(this Node left, Node right) => new GreaterThan(left, right);

    /// <summary>
    /// Creates a greater-than-or-equal comparison.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to compare against.</param>
    /// <returns>A <see cref="GreaterThanOrEqual"/> operator.</returns>
    public static GreaterThanOrEqual GreaterThanOrEqual(this Node left, Node right) => new GreaterThanOrEqual(left, right);

    /// <summary>
    /// Creates a less-than comparison.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to compare against.</param>
    /// <returns>A <see cref="LessThan"/> operator.</returns>
    public static LessThan LessThan(this Node left, Node right) => new LessThan(left, right);

    /// <summary>
    /// Creates a less-than-or-equal comparison.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to compare against.</param>
    /// <returns>A <see cref="LessThanOrEqual"/> operator.</returns>
    public static LessThanOrEqual LessThanOrEqual(this Node left, Node right) => new LessThanOrEqual(left, right);

    #endregion

    #region Equality Operations

    /// <summary>
    /// Creates an equality comparison.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to compare against.</param>
    /// <returns>An <see cref="Equal"/> operator.</returns>
    public static Equal Equal(this Node left, Node right) => new Equal(left, right);

    /// <summary>
    /// Creates a not-equal comparison.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to compare against.</param>
    /// <returns>A <see cref="NotEqual"/> operator.</returns>
    public static NotEqual NotEqual(this Node left, Node right) => new NotEqual(left, right);

    #endregion

    #region Boolean Operations

    /// <summary>
    /// Creates a logical AND operation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to AND with this expression.</param>
    /// <returns>An <see cref="And"/> operator.</returns>
    public static And And(this Node left, Node right) => new And(left, right);

    /// <summary>
    /// Creates a logical OR operation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The expression to OR with this expression.</param>
    /// <returns>An <see cref="Or"/> operator.</returns>
    public static Or Or(this Node left, Node right) => new Or(left, right);

    /// <summary>
    /// Creates a logical NOT operation.
    /// </summary>
    /// <param name="operand">The operand.</param>
    /// <returns>A <see cref="Not"/> operator.</returns>
    public static Not Not(this Node operand) => new Not(operand);

    #endregion

    #region Conditional and Coalesce Operations

    /// <summary>
    /// Creates a conditional (ternary) expression.
    /// </summary>
    /// <param name="condition">The condition expression.</param>
    /// <param name="ifTrue">The value to return if this condition is true.</param>
    /// <param name="ifFalse">The value to return if this condition is false.</param>
    /// <returns>A <see cref="Conditional"/> operator.</returns>
    public static Conditional Conditional(this Node condition, Node ifTrue, Node ifFalse) => new Conditional(condition, ifTrue, ifFalse);

    /// <summary>
    /// Creates a null-coalescing operation.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="fallback">The fallback value if this value is null.</param>
    /// <returns>A <see cref="Coalesce"/> operator.</returns>
    public static Coalesce Coalesce(this Node left, Node fallback) => new Coalesce(left, fallback);

    #endregion

    #region Type Operations

    /// <summary>
    /// Creates a type cast operation.
    /// </summary>
    /// <param name="operand">The operand.</param>
    /// <param name="targetType">The type to cast to.</param>
    /// <param name="isChecked">Whether to use checked conversion.</param>
    /// <returns>A <see cref="TypeCast"/> operator.</returns>
    public static TypeCast CastTo(this Node operand, ITypeDefinition targetType, bool isChecked = false) => new TypeCast(operand, targetType, isChecked);

    #endregion

    #region Assignment

    /// <summary>
    /// Creates an assignment operation.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>An <see cref="Assignment"/> operator.</returns>
    public static Assignment Assign(this Node destination, Node value) => new Assignment(destination, value);

    #endregion

    #region Static Factory Methods

    /// <summary>
    /// A predefined null literal expression.
    /// </summary>
    public static Constant<object?> Null = new Constant<object?>(null);

    /// <summary>
    /// A predefined literal representing the boolean value <c>true</c>.
    /// </summary>
    public static Constant<bool> True = new Constant<bool>(true);

    /// <summary>
    /// A predefined literal representing the boolean value <c>false</c>.
    /// </summary>
    public static Constant<bool> False = new Constant<bool>(false);

    /// <summary>
    /// Creates a literal expression wrapping the specified constant.
    /// </summary>
    /// <typeparam name="T">The type of the literal value.</typeparam>
    /// <param name="value">The constant value to wrap.</param>
    /// <returns>A literal expression representing the specified constant.</returns>
    public static Constant<T> Wrap<T>(T value) => new Constant<T>(value);

    #endregion

    #region Temporary Compatibility (for migration to middleware architecture)

    /// <summary>
    /// TEMPORARY: Builds a LINQ Expression Tree representation of this node.
    /// This is a compatibility shim for existing code that still uses the old BuildNode pattern.
    /// In the middleware architecture, BuildNode is delegated to ITransformer implementations.
    /// </summary>
    /// <param name="node">The node to transform.</param>
    /// <param name="context">The interpretation context.</param>
    /// <returns>A LINQ Expression representation.</returns>
    [Obsolete("Use the middleware interpreter with ITransformer implementations instead. This method will be removed when migration is complete.")]
    public static Expr BuildNode(this Node node, InterpretationContext context)
    {
        // Run semantic analysis on the entire tree first
        AnalyzeNodeTree(context, node);
        
        // Create transformer with context
        var transformer = context.Transformer as LinqExpressionTransformer 
            ?? new LinqExpressionTransformer();
        
        if (transformer is LinqExpressionTransformer linq)
        {
            linq.SetContext(context);
        }
        
        return node.Transform(transformer);
    }
    
    private static void AnalyzeNodeTree(InterpretationContext context, Node node)
    {
        var semanticMiddleware = new SemanticAnalysis.SemanticAnalysisMiddleware<Expr>();
        semanticMiddleware.Transform(context, node, (ctx, n) => Expr.Empty());
        
        // Recursively analyze child nodes
        switch (node)
        {
            case Add add:
                AnalyzeNodeTree(context, add.LeftHandValue);
                AnalyzeNodeTree(context, add.RightHandValue);
                break;
            case Subtract sub:
                AnalyzeNodeTree(context, sub.LeftHandValue);
                AnalyzeNodeTree(context, sub.RightHandValue);
                break;
            case Multiply mul:
                AnalyzeNodeTree(context, mul.LeftHandValue);
                AnalyzeNodeTree(context, mul.RightHandValue);
                break;
            case Divide div:
                AnalyzeNodeTree(context, div.LeftHandValue);
                AnalyzeNodeTree(context, div.RightHandValue);
                break;
            case Modulo mod:
                AnalyzeNodeTree(context, mod.LeftHandValue);
                AnalyzeNodeTree(context, mod.RightHandValue);
                break;
            case UnaryMinus minus:
                AnalyzeNodeTree(context, minus.Operand);
                break;
            case And and:
                AnalyzeNodeTree(context, and.LeftHandValue);
                AnalyzeNodeTree(context, and.RightHandValue);
                break;
            case Or or:
                AnalyzeNodeTree(context, or.LeftHandValue);
                AnalyzeNodeTree(context, or.RightHandValue);
                break;
            case Not not:
                AnalyzeNodeTree(context, not.Value);
                break;
            case Equal eq:
                AnalyzeNodeTree(context, eq.LeftHandValue);
                AnalyzeNodeTree(context, eq.RightHandValue);
                break;
            case NotEqual ne:
                AnalyzeNodeTree(context, ne.LeftHandValue);
                AnalyzeNodeTree(context, ne.RightHandValue);
                break;
            case LessThan lt:
                AnalyzeNodeTree(context, lt.LeftHandValue);
                AnalyzeNodeTree(context, lt.RightHandValue);
                break;
            case LessThanOrEqual lte:
                AnalyzeNodeTree(context, lte.LeftHandValue);
                AnalyzeNodeTree(context, lte.RightHandValue);
                break;
            case GreaterThan gt:
                AnalyzeNodeTree(context, gt.LeftHandValue);
                AnalyzeNodeTree(context, gt.RightHandValue);
                break;
            case GreaterThanOrEqual gte:
                AnalyzeNodeTree(context, gte.LeftHandValue);
                AnalyzeNodeTree(context, gte.RightHandValue);
                break;
            case MemberAccess ma:
                AnalyzeNodeTree(context, ma.Value);
                break;
            case MethodInvocation mi:
                AnalyzeNodeTree(context, mi.Target);
                foreach (var arg in mi.Arguments)
                    AnalyzeNodeTree(context, arg);
                break;
            case IndexAccess ia:
                AnalyzeNodeTree(context, ia.Value);
                foreach (var arg in ia.Arguments)
                    AnalyzeNodeTree(context, arg);
                break;
            case TypeCast tc:
                AnalyzeNodeTree(context, tc.Operand);
                break;
            case Conditional cond:
                AnalyzeNodeTree(context, cond.Condition);
                AnalyzeNodeTree(context, cond.IfTrue);
                AnalyzeNodeTree(context, cond.IfFalse);
                break;
            case Coalesce coal:
                AnalyzeNodeTree(context, coal.LeftHandValue);
                AnalyzeNodeTree(context, coal.RightHandValue);
                break;
            case Assignment assign:
                AnalyzeNodeTree(context, assign.Destination);
                AnalyzeNodeTree(context, assign.Value);
                break;
            case Block block:
                foreach (var expr in block.Nodes)
                    AnalyzeNodeTree(context, expr);
                foreach (var v in block.Variables)
                    AnalyzeNodeTree(context, v);
                break;
        }
    }

    /// <summary>
    /// TEMPORARY: Converts a Parameter node to a ParameterExpression for use with Expression.Lambda.
    /// In the new middleware architecture, use LinqExpressionTransformer.GetParameterExpression(parameter, context) instead.
    /// </summary>
    [Obsolete("Use LinqExpressionTransformer.GetParameterExpression(parameter, context) with the interpretation context instead.")]
    public static Exprs.ParameterExpression ToParameterExpression(this Parameter parameter)
    {
        throw new InvalidOperationException(
            "ToParameterExpression() requires an InterpretationContext. " +
            "Use LinqExpressionTransformer.GetParameterExpression(parameter, context) instead.");
    }

    /// <summary>
    /// TEMPORARY: Gets the type definition of a node.
    /// This is a compatibility shim for existing code.
    /// In the middleware architecture, type resolution happens via semantic analysis middleware.
    /// </summary>
    /// <param name="node">The node to get the type of.</param>
    /// <param name="context">The interpretation context.</param>
    /// <returns>The type definition, or null if unknown.</returns>
    [Obsolete("Use semantic analysis middleware to resolve types. This method will be removed when migration is complete.")]
    public static ITypeDefinition? GetTypeDefinition(this Node node, InterpretationContext context)
    {
        // Use semantic analysis to resolve type
        var semanticMiddleware = new SemanticAnalysis.SemanticAnalysisMiddleware<ITypeDefinition>();
        
        // Run semantic analysis
        semanticMiddleware.Transform(context, node, (ctx, n) => null!);
        
        // Return the resolved type
        return context.GetResolvedType(node);
    }

    #endregion
}