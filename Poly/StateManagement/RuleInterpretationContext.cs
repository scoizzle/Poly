using Poly.Interpretation;
using Poly.Interpretation.Operators;

namespace Poly.StateManagement;

public sealed class RuleInterpretationContext {
    private readonly Variable _entryPoint;
    private readonly Context _interpretationContext;

    public RuleInterpretationContext() {
        _interpretationContext = new Context();
        _entryPoint = _interpretationContext.DeclareVariable("@obj");
    }

    internal Context GetContext() => _interpretationContext;
    internal Value GetEntryPoint() => _entryPoint;
    internal Value GetMemberAccessor(string memberName) => new MemberAccess(_entryPoint, memberName);
}