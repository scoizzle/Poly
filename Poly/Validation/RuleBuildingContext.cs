using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Introspection;

namespace Poly.Validation;

public sealed record RuleBuildingContext {
    private const string EntryPointName = "@value";
    private const string ResultName = "@result";

    public RuleBuildingContext(InterpretationContext interpretationContext, ITypeDefinition entryPointTypeDefinition) {
        Value = interpretationContext.AddParameter(EntryPointName, entryPointTypeDefinition);
        RuleEvaluationContext = interpretationContext.AddParameter<RuleEvaluationResult>(ResultName);
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
}
