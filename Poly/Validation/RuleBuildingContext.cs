using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Introspection;

namespace Poly.Validation;

public sealed record RuleBuildingContext {
    private const string EntryPointName = "@value";
    private const string ContextName = "@context";

    public RuleBuildingContext(InterpretationContext interpretationContext, ITypeDefinition entryPointTypeDefinition) {
        Value = interpretationContext.AddParameter(EntryPointName, entryPointTypeDefinition);
        RuleEvaluationContext = interpretationContext.AddParameter<RuleEvaluationContext>(ContextName);
    }

    /// <summary>
    /// Gets the value being validated in the current context (used for constraints).
    /// For property constraints, use GetMemberAccessor to access specific properties.
    /// For type rules, this is the property value.
    /// </summary>
    public Value Value { get; private init; }

    /// <summary>
    /// Gets the result value of the rule interpretation.
    /// </summary>
    public Value RuleEvaluationContext { get; }

    /// <summary>
    /// Creates a new context with the property value as the entry point
    /// </summary>
    /// <param name="propertyName">The name of the property to scope the context to.</param>
    /// <returns></returns>
    internal RuleBuildingContext GetPropertyContext(string propertyName) => this with { Value = new MemberAccess(Value, propertyName) };

    /// <summary>
    /// Wraps a condition in a call to the RuleEvaluationContext.Evaluate method.
    /// </summary>
    /// <param name="condition"></param>
    /// <returns>A value representing the result of evaluating the condition within the rule evaluation context.</returns>
    internal Value Test(Value condition, Func<ValidationError>? errorFactory = null) {
        ArgumentNullException.ThrowIfNull(condition);
        if (errorFactory is null) return condition;

        return new InvocationOperator(
            RuleEvaluationContext,
            nameof(Validation.RuleEvaluationContext.Evaluate),
            [condition, new Literal(errorFactory) ]
        );
    }
}
