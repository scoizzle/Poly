using Poly.DataModeling.TypeExpressions;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Validation;
using Poly.Validation.Rules;

namespace Poly.DataModeling;

/// <summary>
/// Compiles a DataType definition with its property constraints into a validation predicate.
/// Bridges the DataModeling system with the Validation system to enable runtime validation
/// of instances against data model rules.
/// </summary>
public sealed class DataTypeValidator<T> {
    private readonly DataType _dataType;

    /// <summary>
    /// Initializes a new DataTypeValidator for the specified DataType definition.
    /// </summary>
    /// <param name="dataType">The data type definition containing property constraints and rules.</param>
    public DataTypeValidator(DataType dataType)
    {
        ArgumentNullException.ThrowIfNull(dataType);
        _dataType = dataType;

        // Build rules from DataType definition
        var rules = BuildRules();

        // Get the CLR type definition for T
        var registry = ClrTypeDefinitionRegistry.Shared;
        var typeDefinition = registry.GetTypeDefinition<T>()
            ?? throw new InvalidOperationException($"Type definition for {typeof(T).Name} not found.");

        // Build the interpretation tree from all rules
        var buildingContext = new RuleBuildingContext(typeDefinition);
        CombinedRule = new AndRule(rules);
        RuleInterpretation = CombinedRule.BuildInterpretationTree(buildingContext);

        // Analyze and compile
        var analyzer = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseVariableScopeValidator()
            .Build();

        AnalysisResult = analyzer.Analyze(RuleInterpretation);
        var generator = new LinqExpressionGenerator(AnalysisResult);

        ExpressionTree = generator.Compile(RuleInterpretation);

        // Collect parameters and build the predicate
        var parameterExpressions = generator.GetParameters().ToList();

        // Ensure we have the main parameter for the type being validated
        var mainParam = (Parameter)buildingContext.Value;
        var mainParamExpr = parameterExpressions.FirstOrDefault(p => p.Name == mainParam.Name);
        if (mainParamExpr == null) {
            mainParamExpr = Expr.Parameter(typeof(T), mainParam.Name);
            parameterExpressions.Clear();
            parameterExpressions.Add(mainParamExpr);
        }

        Predicate = Expr.Lambda<Predicate<T>>(ExpressionTree, parameterExpressions).Compile();
    }

    /// <summary>
    /// Gets the DataType definition being validated against.
    /// </summary>
    public DataType DataType => _dataType;

    /// <summary>
    /// Gets the combined rule representing all validation rules.
    /// </summary>
    public Rule CombinedRule { get; }

    /// <summary>
    /// Gets the interpretation tree (AST) representation of the validation rules.
    /// </summary>
    public Node RuleInterpretation { get; }

    /// <summary>
    /// Gets the analysis result from semantic analysis of the rules.
    /// </summary>
    public AnalysisResult AnalysisResult { get; }

    /// <summary>
    /// Gets the LINQ expression tree representation of the validation.
    /// </summary>
    public Expr ExpressionTree { get; }

    /// <summary>
    /// Gets the compiled predicate function.
    /// </summary>
    public Predicate<T> Predicate { get; }

    /// <summary>
    /// Validates the specified instance against the DataType constraints and rules.
    /// </summary>
    /// <param name="instance">The instance to validate.</param>
    /// <returns>True if all constraints and rules are satisfied; otherwise, false.</returns>
    public bool Validate(T instance) => Predicate(instance);

    /// <summary>
    /// Builds validation rules from the DataType definition.
    /// </summary>
    private IEnumerable<Rule> BuildRules()
    {
        // Convert property constraints to PropertyConstraintRules
        foreach (var property in _dataType.Properties) {
            var constraints = property.Constraints.ToList();
            if (constraints.Count == 0) continue;

            // Combine all constraints for this property into an AndRule
            Rule propertyRule = constraints.Count == 1
                ? constraints[0]
                : new AndRule(constraints);

            yield return new PropertyConstraintRule(property.Name, propertyRule);
        }

        // Add type-level rules
        foreach (var rule in _dataType.Rules) {
            yield return rule;
        }
    }

    public override string ToString() => CombinedRule?.ToString() ?? $"Validator for {_dataType.Name}";
}

/// <summary>
/// Factory for creating DataTypeValidator instances.
/// </summary>
public static class DataTypeValidator {
    /// <summary>
    /// Creates a validator for the specified DataType and CLR type.
    /// </summary>
    /// <typeparam name="T">The CLR type to validate instances of.</typeparam>
    /// <param name="dataType">The data type definition to validate against.</param>
    /// <returns>A compiled validator.</returns>
    public static DataTypeValidator<T> Create<T>(DataType dataType) => new DataTypeValidator<T>(dataType);
}