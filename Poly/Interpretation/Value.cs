using Poly.Interpretation.Operators;
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
    
    /// <summary>
    /// Creates a member access operation for accessing a member of this value.
    /// </summary>
    /// <param name="memberName">The name of the member to access (property, field, or method).</param>
    /// <returns>A <see cref="MemberAccess"/> operator representing the member access.</returns>
    public Value GetMember(string memberName) => new MemberAccess(this, memberName);

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
    
}