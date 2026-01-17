namespace Poly.Interpretation.Expressions;

/// <summary>
/// Represents accessing a member (property or field) of an object.
/// </summary>
/// <param name="value">The expression representing the object whose member is being accessed.</param>
/// <param name="memberName">The name of the member to access.</param>
public sealed class MemberAccess(Interpretable value, string memberName) : Interpretable {
    public Interpretable Value { get; } = value;
    public string MemberName { get; } = memberName;

    /// <inheritdoc />
    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var instance = Value.Evaluate(builder);
        return builder.MemberGet(instance, MemberName);
    }

    public override string ToString() => $"{Value}.{MemberName}";
}