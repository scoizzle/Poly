using Poly.DataModeling.Mutations;
using Poly.Validation;
using Poly.Validation.Constraints;
using Poly.Interpretation;
using Poly.Interpretation.Operators.Comparison;
using Poly.Interpretation.Operators.Equality;

namespace Poly.DataModeling.Builders;

/// <summary>
/// Fluent builder for creating constraints on mutation preconditions using strongly-typed methods.
/// </summary>
public sealed class MutationConditionBuilder {
    private readonly ValueSource _valueSource;

    internal MutationConditionBuilder(ValueSource valueSource) {
        _valueSource = valueSource;
    }

    /// <summary>
    /// Creates a precondition that checks if the value is null.
    /// </summary>
    public MutationCondition Null() {
        return new MutationCondition(_valueSource, new EqualityConstraint(null!));
    }

    /// <summary>
    /// Creates a precondition that checks if the value is not null.
    /// </summary>
    public MutationCondition NotNull() {
        return new MutationCondition(_valueSource, new NotNullConstraint());
    }

    /// <summary>
    /// Creates a precondition that checks if the value equals a constant.
    /// </summary>
    public MutationCondition EqualTo(object value) {
        ArgumentNullException.ThrowIfNull(value);
        return new MutationCondition(_valueSource, new EqualityConstraint(value));
    }

    /// <summary>
    /// Creates a precondition that checks if the value equals another value source.
    /// </summary>
    public MutationCondition EqualTo(ValueSource valueSource) {
        ArgumentNullException.ThrowIfNull(valueSource);
        return new MutationCondition(_valueSource, new ValueSourceComparisonConstraint(ComparisonType.Equal, valueSource));
    }

    /// <summary>
    /// Creates a precondition that checks if the value is greater than a constant.
    /// </summary>
    public MutationCondition GreaterThan(object value) {
        ArgumentNullException.ThrowIfNull(value);
        return new MutationCondition(_valueSource, new RangeConstraint(minValue: value, maxValue: null) {
            MinValue = CreateExclusiveMin(value)
        });
    }

    /// <summary>
    /// Creates a precondition that checks if the value is greater than another value source.
    /// </summary>
    public MutationCondition GreaterThan(ValueSource valueSource) {
        ArgumentNullException.ThrowIfNull(valueSource);
        return new MutationCondition(_valueSource, new ValueSourceComparisonConstraint(ComparisonType.GreaterThan, valueSource));
    }

    /// <summary>
    /// Creates a precondition that checks if the value is greater than or equal to a constant.
    /// </summary>
    public MutationCondition GreaterThanOrEqualTo(object value) {
        ArgumentNullException.ThrowIfNull(value);
        return new MutationCondition(_valueSource, new RangeConstraint(minValue: value, maxValue: null));
    }

    /// <summary>
    /// Creates a precondition that checks if the value is greater than or equal to another value source.
    /// </summary>
    public MutationCondition GreaterThanOrEqualTo(ValueSource valueSource) {
        ArgumentNullException.ThrowIfNull(valueSource);
        return new MutationCondition(_valueSource, new ValueSourceComparisonConstraint(ComparisonType.GreaterThanOrEqual, valueSource));
    }

    /// <summary>
    /// Creates a precondition that checks if the value is less than a constant.
    /// </summary>
    public MutationCondition LessThan(object value) {
        ArgumentNullException.ThrowIfNull(value);
        return new MutationCondition(_valueSource, new RangeConstraint(minValue: null, maxValue: value) {
            MaxValue = CreateExclusiveMax(value)
        });
    }

    /// <summary>
    /// Creates a precondition that checks if the value is less than another value source.
    /// </summary>
    public MutationCondition LessThan(ValueSource valueSource) {
        ArgumentNullException.ThrowIfNull(valueSource);
        return new MutationCondition(_valueSource, new ValueSourceComparisonConstraint(ComparisonType.LessThan, valueSource));
    }

    /// <summary>
    /// Creates a precondition that checks if the value is less than or equal to a constant.
    /// </summary>
    public MutationCondition LessThanOrEqualTo(object value) {
        ArgumentNullException.ThrowIfNull(value);
        return new MutationCondition(_valueSource, new RangeConstraint(minValue: null, maxValue: value));
    }

    /// <summary>
    /// Creates a precondition that checks if the value is less than or equal to another value source.
    /// </summary>
    public MutationCondition LessThanOrEqualTo(ValueSource valueSource) {
        ArgumentNullException.ThrowIfNull(valueSource);
        return new MutationCondition(_valueSource, new ValueSourceComparisonConstraint(ComparisonType.LessThanOrEqual, valueSource));
    }

    /// <summary>
    /// Creates a precondition that checks if the value is within a range.
    /// </summary>
    public MutationCondition InRange(object minValue, object maxValue) {
        return new MutationCondition(_valueSource, new RangeConstraint(minValue, maxValue));
    }

    /// <summary>
    /// Creates a precondition that checks if the string length is within a range.
    /// </summary>
    public MutationCondition WithLength(int? minLength = null, int? maxLength = null) {
        return new MutationCondition(_valueSource, new LengthConstraint(minLength, maxLength));
    }

    /// <summary>
    /// Creates a precondition that checks if the string length is at least the specified minimum.
    /// </summary>
    public MutationCondition WithMinLength(int minLength) {
        return new MutationCondition(_valueSource, new LengthConstraint(minLength, null));
    }

    /// <summary>
    /// Creates a precondition that checks if the string length is at most the specified maximum.
    /// </summary>
    public MutationCondition WithMaxLength(int maxLength) {
        return new MutationCondition(_valueSource, new LengthConstraint(null, maxLength));
    }

    private static object CreateExclusiveMin(object value) {
        // For numeric types, we need to add a small epsilon for exclusive comparison
        // For now, we'll use a simple approach - in practice, you might want to handle different numeric types
        return value switch {
            int i => i + 1,
            long l => l + 1,
            double d => d + double.Epsilon,
            _ => value
        };
    }

    private static object CreateExclusiveMax(object value) {
        return value switch {
            int i => i - 1,
            long l => l - 1,
            double d => d - double.Epsilon,
            _ => value
        };
    }
}

/// <summary>
/// Comparison type for building comparative preconditions.
/// </summary>
internal enum ComparisonType {
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

/// <summary>
/// A constraint that compares the current value against another value source.
/// </summary>
internal sealed class ValueSourceComparisonConstraint : Constraint {
    private readonly ComparisonType _comparisonType;
    private readonly ValueSource _rightValueSource;

    public ValueSourceComparisonConstraint(ComparisonType comparisonType, ValueSource rightValueSource) {
        _comparisonType = comparisonType;
        _rightValueSource = rightValueSource ?? throw new ArgumentNullException(nameof(rightValueSource));
    }

    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        var left = context.Value;
        var right = BuildValueFromSource(_rightValueSource, context);

        return _comparisonType switch {
            ComparisonType.Equal => new Equal(left, right),
            ComparisonType.GreaterThan => new GreaterThan(left, right),
            ComparisonType.GreaterThanOrEqual => new GreaterThanOrEqual(left, right),
            ComparisonType.LessThan => new LessThan(left, right),
            ComparisonType.LessThanOrEqual => new LessThanOrEqual(left, right),
            _ => throw new InvalidOperationException($"Unknown comparison type: {_comparisonType}")
        };
    }

    private static Value BuildValueFromSource(ValueSource source, RuleBuildingContext context) {
        return source switch {
            ConstantValue cv => new Literal(cv.Value),
            ParameterValue pv => new Variable(pv.Name),
            PropertyValue prop => context.Value.GetMember(prop.PropertyName),
            MemberAccessValue mav => BuildValueFromSource(mav.Source, context).GetMember(mav.MemberName),
            _ => throw new InvalidOperationException($"Unknown value source type: {source.GetType().Name}")
        };
    }

    public override string ToString() {
        var op = _comparisonType switch {
            ComparisonType.Equal => "==",
            ComparisonType.GreaterThan => ">",
            ComparisonType.GreaterThanOrEqual => ">=",
            ComparisonType.LessThan => "<",
            ComparisonType.LessThanOrEqual => "<=",
            _ => "?"
        };
        return $"value {op} {_rightValueSource}";
    }
}
