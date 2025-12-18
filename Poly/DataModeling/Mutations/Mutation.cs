namespace Poly.DataModeling.Mutations;

using Poly.DataModeling.Mutations.Effects;
using Poly.Validation;

public sealed record Mutation(
    string Name,
    string TargetTypeName,
    IEnumerable<MutationParameter> Parameters,
    IEnumerable<MutationCondition> Preconditions,
    IEnumerable<Effect> Effects
);

public sealed record MutationCondition(ValueSource ValueSource, Constraint Constraint);