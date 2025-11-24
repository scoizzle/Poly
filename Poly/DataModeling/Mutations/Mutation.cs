namespace Poly.DataModeling.Mutations;

using Poly.Validation;
using Poly.DataModeling.Mutations.Effects;

public sealed record Mutation(
    string Name,
    string TargetTypeName,
    IEnumerable<MutationParameter> Parameters,
    IEnumerable<MutationCondition> Preconditions,
    IEnumerable<Effect> Effects
);

public sealed record MutationCondition(string PropertyName, Constraint Constraint);
