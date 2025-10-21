
using Poly.Interpretation;
using Poly.StateManagement.Constraints;
using Poly.StateManagement.Validation.Rules;

namespace Poly.StateManagement;

public abstract class Rule {
    public abstract Value BuildInterpretationTree(RuleInterpretationContext context);
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

    public RuleSet<T> Build() => new(_rules);
}

public sealed class RuleSet<T> {
    public RuleSet(IEnumerable<Rule> rules) {
        CombinedRules = new AndRule(rules);
        (InterpretationTree, ExpressionTree, Predicate) = GetInterpretationTreeExpressionAndPredicate();
    }

    public Rule CombinedRules { get; }
    public Value InterpretationTree { get; }
    public Expression ExpressionTree { get; }
    public Predicate<T> Predicate { get; }

    private (Value, Expression, Predicate<T>) GetInterpretationTreeExpressionAndPredicate() {
        var ruleInterpretationContext = new RuleInterpretationContext<T>();
        var interpretationTree = CombinedRules.BuildInterpretationTree(ruleInterpretationContext);
        var expressionTree = ruleInterpretationContext.BuildExpression(CombinedRules);
        var paramInterpretation = ruleInterpretationContext.GetParameterExpression();
        var lambda = Expression.Lambda<Predicate<T>>(expressionTree, paramInterpretation);
        var predicate = lambda.Compile();
        return (interpretationTree, expressionTree, predicate);
    }

    public bool Test(T instance) => Predicate(instance);
}