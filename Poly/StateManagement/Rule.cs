
using Poly.Interpretation;
using Poly.StateManagement.Constraints;
using Poly.StateManagement.Validation.Rules;

namespace Poly.StateManagement;

public abstract class Rule {
    public abstract Value BuildInterpretationTree(RuleInterpretationContext context);
}

public sealed class RuleEvaluationResult {
    private readonly List<string> _violations = new();

    public IReadOnlyList<string> Violations => _violations;
    public bool IsValid => _violations.Count == 0;

    public void AddError(string violation) {
        _violations.Add(violation);
    }
}

public sealed class ConstraintSetBuilder<T>(string propertyName) {
    internal readonly List<Constraint> _constraints = new();
    internal string PropertyName => propertyName;

    internal ConstraintSetBuilder<T> Add(Constraint constraint) {
        _constraints.Add(constraint);
        return this;
    }

    public Rule Build() => new AndRule(_constraints);
}

public static class NotNullConstraintBuilderExtensions {
    public static ConstraintSetBuilder<T?> NotNull<T>(this ConstraintSetBuilder<T?> builder) where T : class {
        return builder.Add(new NotNullConstraint(builder.PropertyName));
    }
}

public static class LengthConstraintBuilderExtensions {
    public static ConstraintSetBuilder<string> MinLength(this ConstraintSetBuilder<string> builder, int minLength) {
        LengthConstraint? existingLengthConstraint = builder._constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MinLength = minLength;
            return builder;
        }

        return builder.Add(new LengthConstraint(builder.PropertyName, minLength, null));
    }

    public static ConstraintSetBuilder<string> MaxLength(this ConstraintSetBuilder<string> builder, int maxLength) {
        LengthConstraint? existingLengthConstraint = builder._constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MaxLength = maxLength;
            return builder;
        }

        return builder.Add(new LengthConstraint(builder.PropertyName, null, maxLength));
    }

    public static ConstraintSetBuilder<T[]> MinLength<T>(this ConstraintSetBuilder<T[]> builder, int minLength) {
        LengthConstraint? existingLengthConstraint = builder._constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MinLength = minLength;
            return builder;
        }

        return builder.Add(new LengthConstraint(builder.PropertyName, minLength, null));
    }

    public static ConstraintSetBuilder<T[]> MaxLength<T>(this ConstraintSetBuilder<T[]> builder, int maxLength) {
        LengthConstraint? existingLengthConstraint = builder._constraints
            .OfType<LengthConstraint>()
            .FirstOrDefault();

        if (existingLengthConstraint != null) {
            existingLengthConstraint.MaxLength = maxLength;
            return builder;
        }

        return builder.Add(new LengthConstraint(builder.PropertyName, null, maxLength));
    }
}

public static class NumericConstraintSetBuilderExtensions {
    public static ConstraintSetBuilder<T> Minimum<T, TProp>(this ConstraintSetBuilder<T> builder, TProp value)
        where TProp : INumber<TProp> {
        RangeConstraint? existingRangeConstraint = builder._constraints
            .OfType<RangeConstraint>()
            .FirstOrDefault();

        if (existingRangeConstraint != null) {
            existingRangeConstraint.MinValue = value;
            return builder;
        }

        return builder.Add(new RangeConstraint(builder.PropertyName, value, null));
    }

    public static ConstraintSetBuilder<T> Maximum<T, TProp>(this ConstraintSetBuilder<T> builder, TProp value)
        where TProp : INumber<TProp> {
        RangeConstraint? existingRangeConstraint = builder._constraints
            .OfType<RangeConstraint>()
            .FirstOrDefault();

        if (existingRangeConstraint != null) {
            existingRangeConstraint.MaxValue = value;
            return builder;
        }

        return builder.Add(new RangeConstraint(builder.PropertyName, null, value));
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

    public RuleSetBuilder<T> Member<TProperty>(Expression<Func<T, TProperty>> propertyExpression, Action<ConstraintSetBuilder<TProperty>> constraintsBuilder) {
        var propertyName = GetMemberName(propertyExpression);
        var constraintSetBuilder = new ConstraintSetBuilder<TProperty>(propertyName);
        constraintsBuilder(constraintSetBuilder);
        _rules.Add(constraintSetBuilder.Build());
        return this;
    }

    public RuleSet<T> Build() => new(_rules);
}

public sealed class RuleSet<T> {
    public RuleSet(IEnumerable<Rule> rules) {
        CombinedRules = new AndRule(rules);

        var ruleInterpretationContext = new RuleInterpretationContext<T>();        
        InterpretationTree = CombinedRules.BuildInterpretationTree(ruleInterpretationContext);
        ExpressionTree = ruleInterpretationContext.BuildExpression(CombinedRules);

        var paramInterpretation = ruleInterpretationContext.GetParameterExpression();
        var lambda = Expression.Lambda<Predicate<T>>(ExpressionTree, paramInterpretation);
        Predicate = lambda.Compile();
    }

    public Rule CombinedRules { get; }
    public Value InterpretationTree { get; }
    public Expression ExpressionTree { get; }
    public Predicate<T> Predicate { get; }

    public bool Test(T instance) => Predicate(instance);
}