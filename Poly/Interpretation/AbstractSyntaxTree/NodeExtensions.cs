using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;

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
    /// <param name="targetTypeName">The name of the type to cast to.</param>
    /// <param name="isChecked">Whether to use checked conversion.</param>
    /// <returns>A <see cref="TypeCast"/> operator.</returns>
    public static TypeCast CastTo(this Node operand, string targetTypeName, bool isChecked = false) => new TypeCast(operand, new TypeReference(targetTypeName), isChecked);

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
    public static Constant Null = new Constant(null);

    /// <summary>
    /// A predefined literal representing the boolean value <c>true</c>.
    /// </summary>
    public static Constant True = new Constant(true);

    /// <summary>
    /// A predefined literal representing the boolean value <c>false</c>.
    /// </summary>
    public static Constant False = new Constant(false);

    /// <summary>
    /// Creates a literal expression wrapping the specified constant.
    /// </summary>
    /// <typeparam name="T">The type of the literal value.</typeparam>
    /// <param name="value">The constant value to wrap.</param>
    /// <returns>A literal expression representing the specified constant.</returns>
    public static Constant Wrap(object? value) => new Constant(value);

    #endregion
}