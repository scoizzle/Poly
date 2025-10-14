using Poly.Introspection;

namespace Poly.Interpretation;

public sealed class ClrTypeMemberAccessor(Value instance, ITypeMember member) : Value {
    public Value Instance { get; init; } = instance ?? throw new ArgumentNullException(nameof(instance));
    public ITypeMember Member { get; init; } = member ?? throw new ArgumentNullException(nameof(member));

    public override Expression BuildExpression(Context context) {
        var instanceExpression = Instance.BuildExpression(context);
        return Expression.PropertyOrField(instanceExpression, Member.Name);
    }

    public override string ToString() => $"{Instance}.{Member.Name}";
}