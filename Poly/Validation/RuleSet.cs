using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
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
        var registry = ClrTypeDefinitionRegistry.Shared;
        var typeDefinition = registry.GetTypeDefinition<T>()
            ?? throw new InvalidOperationException($"Type definition for {typeof(T).Name} not found.");

        var buildingContext = new RuleBuildingContext(typeDefinition);
        RuleSetInterpretation = CombinedRules.BuildInterpretationTree(buildingContext);

        var analyzer = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseVariableScopeValidator()
            .Build();

        var analysisResult = analyzer.Analyze(RuleSetInterpretation);
        var generator = new LinqExpressionGenerator(analysisResult);

        NodeTree = generator.Compile(RuleSetInterpretation);

        // Collect parameters generated during compilation
        var parameterExpressions = generator.GetParameters().ToList();

        // Ensure we have the main parameter for the type being validated
        // If it wasn't generated (e.g., due to empty rules), create it manually
        var mainParam = (Parameter)buildingContext.Value;
        var mainParamExpr = parameterExpressions.FirstOrDefault(p => p.Name == mainParam.Name);
        if (mainParamExpr == null) {
            mainParamExpr = Expr.Parameter(typeof(T), mainParam.Name);
            parameterExpressions.Clear();
            parameterExpressions.Add(mainParamExpr);
        }

        Predicate = Expr.Lambda<Predicate<T>>(NodeTree, parameterExpressions).Compile();
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