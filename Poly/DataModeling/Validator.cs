using System.Linq.Expressions;
using Poly.Interpretation.LinqInterpreter;
using Poly.Validation;
using Poly.Validation.Rules;

namespace Poly.DataModeling;

public sealed class Validator {
    private readonly DataModel _model;
    private readonly DataModelTypeDefinitionProvider _typeProvider;

    public Validator(DataModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _typeProvider = model.ToTypeDefinitionProvider();
    }

    public RuleEvaluationResult Validate(string typeName, IDictionary<string, object?> instance)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(instance);

        var evaluationContext = new RuleEvaluationContext();

        var dataType = _model.Types.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (dataType == null) {
            evaluationContext.AddError(new ValidationError("", "type.notfound", $"Type '{typeName}' not found in data model."));
            return evaluationContext.GetResult();
        }

        var entryPointTypeDefinition = _typeProvider.GetTypeDefinition(typeName);
        if (entryPointTypeDefinition == null) {
            evaluationContext.AddError(new ValidationError("", "type.notfound", $"Type definition for '{typeName}' not found."));
            return evaluationContext.GetResult();
        }

        var ruleBuildingContext = new RuleBuildingContext(entryPointTypeDefinition);
        var rules = new List<Rule>();

        foreach (var property in dataType.Properties) {
            var combinedRules = new AndRule(property.Constraints);
            var rule = new PropertyConstraintRule(property.Name, combinedRules);
            rules.Add(rule);
        }

        rules.AddRange(dataType.Rules);

        var combinedRuleSet = new AndRule(rules);
        var ruleSetInterpretation = combinedRuleSet.BuildInterpretationTree(ruleBuildingContext);
        
        var builder = new LinqExecutionPlanBuilder(_typeProvider);
        builder.Parameter("@value", entryPointTypeDefinition);
        var expressionTree = ruleSetInterpretation.Evaluate(builder);
        var param = builder.GetParameter("@value");

        var lambda = Expression.Lambda<Func<IDictionary<string, object?>, bool>>(expressionTree, param);
        var compiledRule = lambda.Compile();
        var isValid = compiledRule(instance);

        if (!isValid) {
            evaluationContext.AddError(new ValidationError("", "validation.failed", "Validation failed."));
        }

        return evaluationContext.GetResult();
    }
}