namespace Poly.Validation.Builders;

public sealed class RuleSetBuilder<T> {
    private readonly List<Rule> _rules = new();

    private static string GetMemberName<TMember>(Expression<Func<T, TMember>> member) {
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