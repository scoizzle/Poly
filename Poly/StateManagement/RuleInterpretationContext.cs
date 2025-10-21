using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.StateManagement;

public class RuleInterpretationContext {
    protected const string EntryPointName = "@obj";
    protected readonly Variable _entryPoint;
    protected readonly Context _interpretationContext;

    public RuleInterpretationContext() {
        _interpretationContext = new Context();
        _entryPoint = _interpretationContext.DeclareVariable(EntryPointName);
    }

    internal Value GetMemberAccessor(string memberName) => new MemberAccess(_entryPoint, memberName);
    
    public Expression BuildExpression(Rule rule) {
        var interpretable = rule.BuildInterpretationTree(this);
        return interpretable.BuildExpression(_interpretationContext);
    }
}

public sealed class RuleInterpretationContext<T> : RuleInterpretationContext {
    private readonly Parameter _parameterExpression;
    public RuleInterpretationContext() : base() {
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<T>();
        _parameterExpression = _interpretationContext.AddParameter<T>(EntryPointName);
    }

    public ParameterExpression GetParameterExpression() => _parameterExpression.BuildExpression(_interpretationContext);
}