using Poly.Interpretation.Operators;
using Poly.Introspection;

namespace Poly.Interpretation;

public abstract class Value {
    public abstract ITypeDefinition GetTypeDefinition(Context context);
    public abstract Expression BuildExpression(Context context);

    public Value GetMember(string memberName) => new MemberAccess(this, memberName);
}