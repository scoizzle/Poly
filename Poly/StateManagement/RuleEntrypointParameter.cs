using Poly.Interpretation;

namespace Poly.StateManagement;

public sealed class RuleInterpretationContext {
    private readonly Variable _entryPoint;
    private readonly Context _interpretationContext;

    public RuleInterpretationContext() {
        _interpretationContext = new Context();
        _entryPoint = _interpretationContext.AddParameter("@obj");
    }

    internal Context GetContext() => _interpretationContext;
    internal Value GetEntryPoint() => _entryPoint;
    internal Value GetMemberAccess(string memberName) => throw new NotImplementedException();