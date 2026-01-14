using Poly.Interpretation;

namespace Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

internal sealed class ClrTypeIndexInterpretationAccessor(Value instance, ClrTypeProperty indexProperty, params IEnumerable<Value> indexParameters) : Value {
    public Value Instance { get; } = instance ?? throw new ArgumentNullException(nameof(instance));
    public ClrTypeProperty IndexProperty { get; } = indexProperty ?? throw new ArgumentNullException(nameof(indexProperty));
    public IEnumerable<Value> IndexParameters { get; } = indexParameters ?? throw new ArgumentNullException(nameof(indexParameters));

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => ((ITypeMember)IndexProperty).MemberTypeDefinition;

    public override Expression BuildExpression(InterpretationContext context) {
        var instanceExpression = Instance.BuildExpression(context);
        var indexExpressions = IndexParameters.Select(p => p.BuildExpression(context)).ToArray();

        return Expression.MakeIndex(instanceExpression, IndexProperty.PropertyInfo, indexExpressions);
    }

    public override string ToString() => $"{Instance}[{string.Join(", ", IndexParameters)}]";
}