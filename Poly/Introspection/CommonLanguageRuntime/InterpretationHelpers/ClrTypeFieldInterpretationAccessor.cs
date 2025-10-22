using Poly.Interpretation;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

sealed class ClrTypeFieldInterpretationAccessor(Value instance, ClrTypeField field) : Value {
    public Value Instance { get; init; } = instance ?? throw new ArgumentNullException(nameof(instance));
    public ClrTypeField Field { get; init; } = field ?? throw new ArgumentNullException(nameof(field));

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => Field.MemberType;

    public override Expression BuildExpression(InterpretationContext context) {
        var instanceExpression = Instance.BuildExpression(context);
        return Expression.Field(instanceExpression, Field.FieldInfo);
    }

    public override string ToString() => $"{Instance}.{Field.Name}";
}
