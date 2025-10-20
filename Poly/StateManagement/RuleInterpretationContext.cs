using Poly.Interpretation;
using Poly.Interpretation.Operators;

namespace Poly.StateManagement;

public abstract class RuleInterpretationContext {
    private readonly Variable _entryPoint;
    private readonly Context _interpretationContext;

    public RuleInterpretationContext(Value entryPoint) {
        _interpretationContext = new Context();
        _entryPoint = _interpretationContext.DeclareVariable("@obj", entryPoint);
    }

    internal Context GetContext() => _interpretationContext;
    internal Value GetEntryPoint() => _entryPoint;
    internal Value GetMemberAccessor(string memberName) => new MemberAccess(_entryPoint, memberName);
}

public sealed class RuleInterpretationContext<T> : RuleInterpretationContext {
    public RuleInterpretationContext(T entryPoint) : base(new Literal(entryPoint)) { }
}