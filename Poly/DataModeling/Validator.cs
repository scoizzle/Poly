using Poly.DataModeling.Interpretation;
using Poly.Interpretation;
using Poly.Validation;
using Poly.Validation.Rules;

namespace Poly.DataModeling;

public sealed class Validator {
    private readonly DataModel _model;

    public Validator(DataModel model) {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    public RuleEvaluationResult Validate(string typeName, IDictionary<string, object?> instance) {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(instance);

        var evaluationContext = new RuleEvaluationContext();

        var dataType = _model.Types.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (dataType == null) {
            evaluationContext.AddError(new ValidationError("", "type.notfound", $"Type '{typeName}' not found in data model."));
            return evaluationContext.GetResult();
        }

        var interpretationContext = new InterpretationContext();
        _model.RegisterIn(interpretationContext);

        var entryPointTypeDefinition = interpretationContext.GetTypeDefinition(typeName);
        if (entryPointTypeDefinition == null) {
            evaluationContext.AddError(new ValidationError("", "type.notfound", $"Type definition for '{typeName}' not found in interpretation context."));
            return evaluationContext.GetResult();
        }

        var ruleBuildingContext = new RuleBuildingContext(interpretationContext, entryPointTypeDefinition);
        var rules = new List<Rule>();

        foreach (var property in dataType.Properties) {

            var combinedRules = new AndRule(property.Constraints);
            var rule = new PropertyConstraintRule(property.Name, combinedRules);
            rules.Add(rule);
        }

        rules.AddRange(dataType.Rules);

        var combinedRuleSet = new AndRule(rules);
        var ruleSetInterpretation = combinedRuleSet.BuildInterpretationTree(ruleBuildingContext);
        var expressionTree = ruleSetInterpretation.BuildExpression(interpretationContext);

        var parameters = interpretationContext.Parameters.Select(e => e.BuildExpression(interpretationContext)).ToArray();
        var lambda = Expression.Lambda<Func<IDictionary<string, object?>, RuleEvaluationContext, bool>>(expressionTree, parameters);

        var compiledRule = lambda.Compile();
        var isValid = compiledRule(instance, evaluationContext);

        return evaluationContext.GetResult();
    }
}