using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Validation;

public class RuleBuildingContext {
    protected const string EntryPointName = "@obj";
    protected readonly Variable _entryPoint;
    protected readonly InterpretationContext _interpretationContext;

    public RuleBuildingContext() {
        _interpretationContext = new InterpretationContext();
        _entryPoint = _interpretationContext.DeclareVariable(EntryPointName);
    }

    internal Value GetMemberAccessor(string memberName) => new MemberAccess(_entryPoint, memberName);

    public Expression BuildExpression(Rule rule) {
        var interpretable = rule.BuildInterpretationTree(this);
        return interpretable.BuildExpression(_interpretationContext);
    }
}

public sealed class RuleBuildingContext<T> : RuleBuildingContext {
    private readonly Parameter _parameterExpression;
    public RuleBuildingContext() : base() {
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<T>();
        _parameterExpression = _interpretationContext.AddParameter<T>(EntryPointName);
    }

    public ParameterExpression GetParameterExpression() => _parameterExpression.BuildExpression(_interpretationContext);
}
