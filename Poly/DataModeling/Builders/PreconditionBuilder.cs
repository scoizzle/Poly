using Poly.DataModeling.Mutations;
using Poly.Validation;

namespace Poly.DataModeling.Builders;

/// <summary>
/// Fluent builder for creating mutation preconditions that can reference target properties,
/// parameters, and nested members.
/// </summary>
public sealed class PreconditionBuilder {
    private ValueSource? _valueSource;

    /// <summary>
    /// Starts building a precondition that references a property of the target type.
    /// </summary>
    /// <param name="propertyName">The name of the property on the target type.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public PreconditionBuilder Property(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        _valueSource = new PropertyValue(propertyName);
        return this;
    }

    /// <summary>
    /// Starts building a precondition that references a parameter.
    /// </summary>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public PreconditionBuilder Parameter(string parameterName)
    {
        ArgumentNullException.ThrowIfNull(parameterName);
        _valueSource = new ParameterValue(parameterName);
        return this;
    }

    /// <summary>
    /// Starts building a precondition that references a constant value.
    /// </summary>
    /// <param name="value">The constant value.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public PreconditionBuilder Constant(object? value)
    {
        _valueSource = new ConstantValue(value);
        return this;
    }

    /// <summary>
    /// Accesses a nested member of the current value source.
    /// </summary>
    /// <param name="memberName">The name of the member to access.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no value source has been set.</exception>
    public PreconditionBuilder Member(string memberName)
    {
        ArgumentNullException.ThrowIfNull(memberName);

        if (_valueSource == null)
            throw new InvalidOperationException("Cannot access a member without first specifying a value source using Property(), Parameter(), or Constant().");

        // _valueSource = new MemberAccessValue(_valueSource, memberName);
        return this;
    }

    /// <summary>
    /// Builds the precondition with the specified constraint.
    /// </summary>
    /// <param name="constraint">The constraint to apply to the value source.</param>
    /// <returns>A <see cref="MutationCondition"/> representing the precondition.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no value source has been set.</exception>
    public MutationCondition Must(Constraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);

        if (_valueSource == null)
            throw new InvalidOperationException("Cannot create a precondition without specifying a value source using Property(), Parameter(), or Constant().");

        return new MutationCondition(_valueSource, constraint);
    }

    /// <summary>
    /// Returns a <see cref="MutationConditionBuilder"/> for creating strongly-typed constraints
    /// using fluent methods like GreaterThan(), LessThan(), EqualTo(), etc.
    /// </summary>
    /// <returns>A <see cref="MutationConditionBuilder"/> for building constraints.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no value source has been set.</exception>
    public MutationConditionBuilder MustBe()
    {
        if (_valueSource == null)
            throw new InvalidOperationException("Cannot create a precondition without specifying a value source using Property(), Parameter(), or Constant().");

        return new MutationConditionBuilder(_valueSource);
    }
}