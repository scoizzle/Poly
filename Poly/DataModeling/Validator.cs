using System.Collections.Generic;
using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Validation;

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
            evaluationContext.AddError(new ValidationError("", "type.notfound", $"Type '{typeName}' not found in model."));
            return evaluationContext.GetResult();
        }

        var interpretationContext = new InterpretationContext();
        var ruleBuildingContext = new RuleBuildingContext(interpretationContext, dataType);

        return evaluationContext.GetResult();
    }
}
