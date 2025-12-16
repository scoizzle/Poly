using Poly.Interpretation.Operators;
using Poly.Interpretation.Operators.Arithmetic;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Comparison;
using Poly.Interpretation.Operators.Equality;
using Poly.Introspection;

namespace Poly.Interpretation;

/// <summary>
/// Represents a value in an interpretation tree that has a known type and can be evaluated.
/// </summary>
/// <remarks>
/// Values are interpretable objects that represent typed data or operations that produce typed results.
/// This includes constants, variables, parameters, and operators. All values must be able to determine
/// their type at interpretation time.
/// </remarks>
public abstract class Value : Interpretable {
    /// <summary>
    /// Gets the type definition of this value within the given interpretation context.
    /// </summary>
    /// <param name="context">The interpretation context containing type information.</param>
    /// <returns>The type definition describing the type of this value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the type cannot be determined.</exception>
    public abstract ITypeDefinition GetTypeDefinition(InterpretationContext context);
    
    #region Member Access
    
    /// <summary>
    /// Creates a member access operation for accessing a member of this value.
    /// </summary>
    /// <param name="memberName">The name of the member to access (property, field, or method).</param>
    /// <returns>A <see cref="MemberAccess"/> operator representing the member access.</returns>
    public Value GetMember(string memberName) => new MemberAccess(this, memberName);

    /// <summary>
    /// Creates an index access operation for accessing an indexed member of this value.
    /// </summary>
    /// <param name="indices">The index arguments.</param>
    /// <returns>An <see cref="IndexAccess"/> operator representing the index access.</returns>
    public Value Index(params Value[] indices) => new IndexAccess(this, indices);

    /// <summary>
    /// Creates a method invocation operation for calling a method on this value.
    /// </summary>
    /// <param name="methodName">The name of the method to invoke.</param>
    /// <param name="arguments">The method arguments.</param>
    /// <returns>An <see cref="InvocationOperator"/> representing the method invocation.</returns>
    public Value Invoke(string methodName, params Value[] arguments) => new InvocationOperator(this, methodName, arguments);
    
    #endregion

    #region Arithmetic Operations
    
    /// <summary>
    /// Creates an addition operation.
    /// </summary>
    /// <param name="other">The value to add to this value.</param>
    /// <returns>An <see cref="Add"/> operator.</returns>
    public Value Add(Value other) => new Add(this, other);
    
    /// <summary>
    /// Creates a subtraction operation.
    /// </summary>
    /// <param name="other">The value to subtract from this value.</param>
    /// <returns>A <see cref="Subtract"/> operator.</returns>
    public Value Subtract(Value other) => new Subtract(this, other);
    
    /// <summary>
    /// Creates a multiplication operation.
    /// </summary>
    /// <param name="other">The value to multiply with this value.</param>
    /// <returns>A <see cref="Multiply"/> operator.</returns>
    public Value Multiply(Value other) => new Multiply(this, other);
    
    /// <summary>
    /// Creates a division operation.
    /// </summary>
    /// <param name="other">The value to divide this value by.</param>
    /// <returns>A <see cref="Divide"/> operator.</returns>
    public Value Divide(Value other) => new Divide(this, other);
    
    /// <summary>
    /// Creates a modulo operation.
    /// </summary>
    /// <param name="other">The divisor value.</param>
    /// <returns>A <see cref="Modulo"/> operator.</returns>
    public Value Modulo(Value other) => new Modulo(this, other);
    
    /// <summary>
    /// Creates a unary negation operation.
    /// </summary>
    /// <returns>A <see cref="UnaryMinus"/> operator.</returns>
    public Value Negate() => new UnaryMinus(this);
    
    #endregion

    #region Comparison Operations
    
    /// <summary>
    /// Creates a greater-than comparison.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>A <see cref="GreaterThan"/> operator.</returns>
    public Value GreaterThan(Value other) => new GreaterThan(this, other);
    
    /// <summary>
    /// Creates a greater-than-or-equal comparison.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>A <see cref="GreaterThanOrEqual"/> operator.</returns>
    public Value GreaterThanOrEqual(Value other) => new GreaterThanOrEqual(this, other);
    
    /// <summary>
    /// Creates a less-than comparison.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>A <see cref="LessThan"/> operator.</returns>
    public Value LessThan(Value other) => new LessThan(this, other);
    
    /// <summary>
    /// Creates a less-than-or-equal comparison.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>A <see cref="LessThanOrEqual"/> operator.</returns>
    public Value LessThanOrEqual(Value other) => new LessThanOrEqual(this, other);
    
    #endregion

    #region Equality Operations
    
    /// <summary>
    /// Creates an equality comparison.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>An <see cref="Equal"/> operator.</returns>
    public Value Equal(Value other) => new Equal(this, other);
    
    /// <summary>
    /// Creates a not-equal comparison.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>A <see cref="NotEqual"/> operator.</returns>
    public Value NotEqual(Value other) => new NotEqual(this, other);
    
    #endregion

    #region Boolean Operations
    
    /// <summary>
    /// Creates a logical AND operation.
    /// </summary>
    /// <param name="other">The value to AND with this value.</param>
    /// <returns>An <see cref="And"/> operator.</returns>
    public Value And(Value other) => new And(this, other);
    
    /// <summary>
    /// Creates a logical OR operation.
    /// </summary>
    /// <param name="other">The value to OR with this value.</param>
    /// <returns>An <see cref="Or"/> operator.</returns>
    public Value Or(Value other) => new Or(this, other);
    
    /// <summary>
    /// Creates a logical NOT operation.
    /// </summary>
    /// <returns>A <see cref="Not"/> operator.</returns>
    public Value Not() => new Not(this);
    
    #endregion

    #region Conditional and Coalesce Operations
    
    /// <summary>
    /// Creates a conditional (ternary) expression.
    /// </summary>
    /// <param name="ifTrue">The value to return if this condition is true.</param>
    /// <param name="ifFalse">The value to return if this condition is false.</param>
    /// <returns>A <see cref="Conditional"/> operator.</returns>
    public Value Conditional(Value ifTrue, Value ifFalse) => new Conditional(this, ifTrue, ifFalse);
    
    /// <summary>
    /// Creates a null-coalescing operation.
    /// </summary>
    /// <param name="fallback">The fallback value if this value is null.</param>
    /// <returns>A <see cref="Coalesce"/> operator.</returns>
    public Value Coalesce(Value fallback) => new Coalesce(this, fallback);
    
    #endregion

    #region Type Operations
    
    /// <summary>
    /// Creates a type cast operation.
    /// </summary>
    /// <param name="targetType">The type to cast to.</param>
    /// <param name="isChecked">Whether to use checked conversion.</param>
    /// <returns>A <see cref="TypeCast"/> operator.</returns>
    public Value CastTo(ITypeDefinition targetType, bool isChecked = false) => new TypeCast(this, targetType, isChecked);
    
    #endregion

    #region Assignment
    
    /// <summary>
    /// Creates an assignment operation.
    /// </summary>
    /// <param name="value">The value to assign.</param>
    /// <returns>An <see cref="Assignment"/> operator.</returns>
    public Value Assign(Value value) => new Assignment(this, value);
    
    #endregion

    #region Static Factory Methods
    
    /// <summary>
    /// A predefined null literal value.
    /// </summary>
    public static readonly Value Null = Wrap<object?>(null);

    /// <summary>
    /// A predefined literal representing the boolean value <c>true</c>.
    /// </summary>
    public static readonly Value True = Wrap(true);

    /// <summary>
    /// A predefined literal representing the boolean value <c>false</c>.
    /// </summary>
    public static readonly Value False = Wrap(false);

    /// <summary>
    /// Creates a literal value wrapping the specified constant.
    /// </summary>
    /// <typeparam name="T">The type of the literal value.</typeparam>
    /// <param name="value">The constant value to wrap.</param>
    /// <returns>A literal value representing the specified constant.</returns>
    public static Value Wrap<T>(T value) => new Literal<T>(value);
    
    #endregion
}
