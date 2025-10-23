using Poly.Interpretation.Operators;
using Poly.Introspection;

namespace Poly.Interpretation;

public abstract class Value : Interpretable {
    public abstract ITypeDefinition GetTypeDefinition(InterpretationContext context);
    public Value GetMember(string memberName) => new MemberAccess(this, memberName);

    public static readonly Value Null = new Literal(null);
}
