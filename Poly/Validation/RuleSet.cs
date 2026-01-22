using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.SemanticAnalysis;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Validation.Rules;
using Poly.Interpretation.LinqExpressions;

namespace Poly.Validation;

/// <summary>
/// Represents a compiled set of validation rules for a specific type.
/// </summary>
/// <typeparam name="T">The type to validate.</typeparam>
public sealed class RuleSet<T> {
    /// <summary>
    /// Initializes a new instance of the RuleSet class.
    /// </summary>
    /// <param name="rules">The collection of rules to combine.</param>
    public RuleSet(IEnumerable<Rule> rules)
    {
        // Combine all rules into a single AndRule
        CombinedRules = new AndRule(rules);
        var registry = ClrTypeDefinitionRegistry.Shared;
        var typeDefinition = registry.GetTypeDefinition<T>()
            ?? throw new InvalidOperationException($"Type definition for {typeof(T).Name} not found.");

        var buildingContext = new RuleBuildingContext(typeDefinition);
        RuleSetInterpretation = CombinedRules.BuildInterpretationTree(buildingContext);

        var interpreter = new InterpreterBuilder<Expr>()
            .UseSemanticAnalysis()
            .UseLinqExpressionCompilation()
            .Build();

        var result = interpreter
            .WithParameter<T>((Parameter)buildingContext.Value)
            .Interpret(RuleSetInterpretation);

        NodeTree = result.Value;
        Predicate = Expr.Lambda<Predicate<T>>(NodeTree, result.GetParameters()).Compile();
    }

    /// <summary>
    /// Gets the combined rule representing all validation rules.
    /// </summary>
    public Rule CombinedRules { get; }

    /// <summary>
    /// Gets the interpretation tree representation of the rule set.
    /// </summary>
    public Node RuleSetInterpretation { get; }

    /// <summary>
    /// Gets the LINQ expression tree representation of the rule set.
    /// </summary>
    public Expr NodeTree { get; }

    /// <summary>
    /// Gets the compiled predicate function.
    /// </summary>
    public Predicate<T> Predicate { get; }

    /// <summary>
    /// Tests whether the specified instance satisfies all rules.
    /// </summary>
    /// <param name="instance">The instance to test.</param>
    /// <returns>True if all rules are satisfied; otherwise, false.</returns>
    public bool Test(T instance) => Predicate(instance);

    /// <summary>
    /// Returns a string representation of the combined rules.
    /// </summary>
    public override string ToString() => CombinedRules?.ToString() ?? string.Empty;
}