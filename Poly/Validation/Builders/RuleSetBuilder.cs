using Poly.Validation.Rules;

namespace Poly.Validation.Builders;

/// <summary>
/// Provides a fluent API for building a RuleSet for a specific type.
/// </summary>
/// <typeparam name="T">The type to build validation rules for.</typeparam>
public sealed class RuleSetBuilder<T> {
    private readonly List<Rule> _rules = new();

    /// <summary>
    /// Adds validation rules for a specific property.
    /// </summary>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="propertySelector">Expression selecting the property to validate.</param>
    /// <param name="constraintsBuilder">Action to configure constraints for the property.</param>
    /// <returns>This builder for method chaining.</returns>
    public RuleSetBuilder<T> Member<TProperty>(
        Expression<Func<T, TProperty>> propertySelector, 
        Action<ConstraintSetBuilder<TProperty>> constraintsBuilder) {
        
        ArgumentNullException.ThrowIfNull(propertySelector);
        ArgumentNullException.ThrowIfNull(constraintsBuilder);

        var propertyName = GetMemberName(propertySelector);
        var constraintSetBuilder = new ConstraintSetBuilder<TProperty>(propertyName);
        constraintsBuilder(constraintSetBuilder);
        
        var combinedRule = constraintSetBuilder.Build();
        var propertyRule = new PropertyConstraintRule(propertyName, combinedRule);
        
        _rules.Add(propertyRule);
        return this;
    }

    /// <summary>
    /// Adds a custom rule to the rule set.
    /// </summary>
    /// <param name="rule">The rule to add.</param>
    /// <returns>This builder for method chaining.</returns>
    public RuleSetBuilder<T> AddRule(Rule rule) {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
        return this;
    }

    /// <summary>
    /// Builds the final RuleSet with all configured rules.
    /// </summary>
    /// <returns>A compiled RuleSet.</returns>
    public RuleSet<T> Build() => new RuleSet<T>(_rules);

    /// <summary>
    /// Extracts the property name from a member access expression.
    /// </summary>
    private static string GetMemberName<TMember>(Expression<Func<T, TMember>> memberExpression) {
        return memberExpression.Body switch {
            MemberExpression me => me.Member.Name,
            UnaryExpression { Operand: MemberExpression me } => me.Member.Name,
            _ => throw new ArgumentException("Expression must be a member access", nameof(memberExpression))
        };
    }
}
