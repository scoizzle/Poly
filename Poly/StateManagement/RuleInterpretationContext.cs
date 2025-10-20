using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.StateManagement;

public abstract class RuleInterpretationContext {
    protected const string EntryPointName = "@obj";
    protected readonly Variable _entryPoint;
    protected readonly Context _interpretationContext;

    public RuleInterpretationContext() {
        _interpretationContext = new Context();
        _entryPoint = _interpretationContext.DeclareVariable(EntryPointName);
    }

    internal Context GetContext() => _interpretationContext;
    internal Value GetEntryPoint() => _entryPoint;
    internal Value GetMemberAccessor(string memberName) => new MemberAccess(_entryPoint, memberName);

    public Expression BuildExpression(RuleSet rules) {
        ArgumentNullException.ThrowIfNull(rules);
        Value interpretationTree = rules.BuildInterpretationTree(this);
        return interpretationTree.BuildExpression(_interpretationContext);
    }
}

public sealed class RuleInterpretationContext<T> : RuleInterpretationContext {
    private readonly Parameter _parameterExpression;
    public RuleInterpretationContext() : base() {
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<T>();
        _parameterExpression = _interpretationContext.AddParameter<T>(EntryPointName);
    }

    public Predicate<T> CompilePredicate(RuleSet rules) {
        Expression expr = BuildExpression(rules);
        ParameterExpression param = _parameterExpression.BuildExpression(_interpretationContext);
        return Expression.Lambda<Predicate<T>>(expr, param).Compile();
    }
}