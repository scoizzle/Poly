using Poly.Introspection;

namespace Poly.Interpretation.Operators;

public class MemberAccess(Value value, string memberName) : Operator {
    public Value Value { get; } = value;
    public string MemberName { get; } = memberName;

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        ITypeDefinition typeDefinition = Value.GetTypeDefinition(context);
        ITypeMember member = typeDefinition.GetMember(MemberName)
            ?? throw new InvalidOperationException($"Member '{MemberName}' not found on type '{typeDefinition.Name}'.");
        return member.MemberTypeDefinition;
    }

    public override Expression BuildExpression(InterpretationContext context) {
        ITypeDefinition typeDefinition = Value.GetTypeDefinition(context);
        ITypeMember member = typeDefinition.GetMember(MemberName)
            ?? throw new InvalidOperationException($"Member '{MemberName}' not found on type '{typeDefinition.Name}'.");
        Value memberAccessor = member.GetMemberAccessor(Value);
        return memberAccessor.BuildExpression(context);
    }

    public override string ToString() => $"{Value}.{MemberName}";
}