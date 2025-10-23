using Poly.Extensions;
using Poly.Interpretation;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

sealed class ClrTypePropertyInterpretationAccessor(Value instance, ClrTypeProperty property) : Value {
    public Value Instance { get; init; } = instance ?? throw new ArgumentNullException(nameof(instance));
    public ClrTypeProperty Property { get; init; } = property ?? throw new ArgumentNullException(nameof(property));

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => Property.MemberType;

    public override Expression BuildExpression(InterpretationContext context) {
        var instanceExpression = Instance.BuildExpression(context);


        if (Property.PropertyInfo.IsStatic() && instanceExpression is ConstantExpression constExpr && constExpr.Value is null) {
            return Expression.Property(null, Property.PropertyInfo);
        }


        return Expression.Property(instanceExpression, Property.Name);
    }

    public override string ToString() => $"{Instance}.{Property.Name}";
}