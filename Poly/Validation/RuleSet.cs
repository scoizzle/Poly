using Poly.Interpretation.SemanticAnalysis;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Validation.Rules;

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

        // Build the interpretation tree
        // var interpretationContext = new InterpretationContext();
        // var registry = ClrTypeDefinitionRegistry.Shared;
        // var typeDefinition = registry.GetTypeDefinition<T>()
        //     ?? throw new InvalidOperationException($"Type definition for {typeof(T).Name} not found.");

        // var buildingContext = new RuleBuildingContext(interpretationContext, typeDefinition);
        // RuleSetInterpretation = CombinedRules.BuildInterpretationTree(buildingContext);

        // // Build the expression tree using middleware pattern
        // // Run semantic analysis on the tree
        // var semanticMiddleware = new SemanticAnalysisMiddleware<Expr>();
        // semanticMiddleware.Transform(interpretationContext, RuleSetInterpretation, (ctx, n) => Expr.Empty());
        
        // // Transform to LINQ expression
        // var transformer = new LinqExpressionTransformer();
        // transformer.SetContext(interpretationContext);
        
        // // Ensure the entry point parameter is registered with transformer
        // // even when there are no rules (empty rule sets still need the parameter)
        // buildingContext.Value.Transform(transformer);
        
        // NodeTree = RuleSetInterpretation.Transform(transformer);

        // // Compile to a predicate - collect parameter expressions from transformer
        // var parameters = transformer.ParameterExpressions.ToArray();
        // var lambda = Expr.Lambda<Predicate<T>>(NodeTree, parameters);
        // Predicate = lambda.Compile();
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