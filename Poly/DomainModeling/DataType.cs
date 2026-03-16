using Poly.Validation;

namespace Poly.DomainModeling;

public sealed record DataType(
    string Name,
    IEnumerable<DataProperty> Properties,
    IEnumerable<Rule> Rules,
    IEnumerable<Mutations.Mutation> Mutations,
    Identity? Identity = null,
    Lifecycle? Lifecycle = null
);