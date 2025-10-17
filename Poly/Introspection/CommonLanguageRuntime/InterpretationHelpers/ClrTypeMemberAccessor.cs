using Poly.Interpretation;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

sealed class ClrTypeMemberInterpretationAccessor(Value instance, ClrTypeMember member) : Value {
    public Value Instance { get; init; } = instance ?? throw new ArgumentNullException(nameof(instance));
    public ClrTypeMember Member { get; init; } = member ?? throw new ArgumentNullException(nameof(member));

    public override ITypeDefinition GetTypeDefinition(Context context) => Member.MemberType;

    public override Expression BuildExpression(Context context) {
        var instanceExpression = Instance.BuildExpression(context);
        return Expression.PropertyOrField(instanceExpression, Member.Name);
    }

    public override string ToString() => $"{Instance}.{Member.Name}";
}