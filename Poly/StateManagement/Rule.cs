
using Poly.StateManagement.Validation.Rules;

namespace Poly.StateManagement;

public abstract record Rule {
    public abstract Interpretation.Value BuildInterpretationTree(RuleInterpretationContext context);
}

public sealed class ConstraintSetBuilder<T>(string propertyName) {
    private readonly List<Constraint> _constraints = new();

    public ConstraintSetBuilder<T> NotNull() {
        _constraints.Add(new NotNullConstraint(propertyName));
        return this;
    }

    public Rule Build() => new AndRule(_constraints);
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

    public RuleSetBuilder<T> Property<TProperty>(Expression<Func<T, TProperty>> propertyExpression, Action<ConstraintSetBuilder<T>> constraintsBuilder) {
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