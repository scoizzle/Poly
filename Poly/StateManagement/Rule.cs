
using Poly.Interpretation.Operators.Comparison;
using Poly.StateManagement.Constraints;
using Poly.StateManagement.Validation.Rules;

namespace Poly.StateManagement;

public abstract class Rule {
    public abstract Interpretation.Value BuildInterpretationTree(RuleInterpretationContext context);
}

public sealed class ConstraintSetBuilder<T>(string propertyName) {
    private readonly List<Constraint> _constraints = new();
    internal string PropertyName => propertyName;

    internal ConstraintSetBuilder<T> Add(Constraint constraint) {
        _constraints.Add(constraint);
        return this;
    }

    public ConstraintSetBuilder<T> NotNull() {
        return Add(new NotNullConstraint(PropertyName));
    }

    public ConstraintSetBuilder<T> MinLength(int minLength) {
        LengthConstraint? existingLengthConstraint = _constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MinLength = minLength;
            return this; ;
        }

        return Add(new LengthConstraint(PropertyName, minLength, null));
    }
    
    public ConstraintSetBuilder<T> MaxLength(int maxLength) {
        LengthConstraint? existingLengthConstraint = _constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MaxLength = maxLength;
            return this;
        }

        return Add(new LengthConstraint(PropertyName, null, maxLength));
    }

    public Rule Build() => new AndRule(_constraints);
}

public static class NumericConstraintSetBuilderExtensions {
    public static ConstraintSetBuilder<T> Minimum<T, TProp>(this ConstraintSetBuilder<T> builder, TProp value)
        where TProp : INumber<TProp> {
        return builder.Add(new MinValueConstraint(builder.PropertyName, value));
    }

    public static ConstraintSetBuilder<T> Maximum<T, TProp>(this ConstraintSetBuilder<T> builder, TProp value)
        where TProp : INumber<TProp> {
        return builder.Add(new MaxValueConstraint(builder.PropertyName, value));
    }
}

public sealed class RuleSetBuilder<T> {
    private readonly List<Rule> _rules = new();

    private string GetMemberName<TMember>(Expression<Func<T, TMember>> member) {
        var memberExpr = member.Body switch {
            MemberExpression me => me,
            UnaryExpression ue when ue.Operand is MemberExpression me => me,
            _ => throw new ArgumentException("Expression must be a member access", nameof(member))
        };

        return memberExpr.Member.Name;
    }

    public RuleSetBuilder<T> Member<TProperty>(Expression<Func<T, TProperty>> propertyExpression, Action<ConstraintSetBuilder<T>> constraintsBuilder) {
        var propertyName = GetMemberName(propertyExpression);
        var constraintSetBuilder = new ConstraintSetBuilder<T>(propertyName);
        constraintsBuilder(constraintSetBuilder);
        _rules.Add(constraintSetBuilder.Build());
        return this;
    }

    public RuleSet Build() => new(_rules);
}

public sealed class RuleSet(IEnumerable<Rule> rules) : IEnumerable<Rule> {
    private readonly IEnumerable<Rule> _rules = rules;

    public Interpretation.Value BuildInterpretationTree(RuleInterpretationContext context) {
        AndRule combinedRule = new(_rules);
        return combinedRule.BuildInterpretationTree(context);
    }

    public IEnumerator<Rule> GetEnumerator() {
        return _rules.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return ((IEnumerable)_rules).GetEnumerator();
    }
}