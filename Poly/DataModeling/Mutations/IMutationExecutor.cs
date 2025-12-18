namespace Poly.DataModeling.Mutations;

using System.Collections.Generic;

public interface IMutationExecutor {
    MutationResult Execute(
        DataModel model,
        Mutation mutation,
        IDictionary<string, object?> targetInstance,
        IDictionary<string, object?> parameters);
}

public sealed record MutationResult(bool Success, Poly.Validation.RuleEvaluationResult Validation, IDictionary<string, object?> UpdatedInstance);