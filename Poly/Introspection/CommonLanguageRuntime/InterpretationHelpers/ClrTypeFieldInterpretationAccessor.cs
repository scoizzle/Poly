using Poly.Interpretation;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

internal sealed class ClrTypeFieldInterpretationAccessor(Value instance, ClrTypeField field) : Value {
    public Value Instance { get; init; } = instance ?? throw new ArgumentNullException(nameof(instance));
    public ClrTypeField Field { get; init; } = field ?? throw new ArgumentNullException(nameof(field));

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => ((ITypeMember)Field).MemberTypeDefinition;

    public override Expression BuildExpression(InterpretationContext context)
    {
        var instanceExpression = Instance.BuildExpression(context);

        if (Field.FieldInfo.IsStatic && instanceExpression is ConstantExpression constExpr && constExpr.Value is null) {
            return Expression.Field(null, Field.FieldInfo);
        }

        return Expression.Field(instanceExpression, Field.FieldInfo);
    }

    public override string ToString() => $"{Instance}.{Field.Name}";
}