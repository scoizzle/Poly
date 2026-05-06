using Poly.Data.Modeling.Validation;
using Poly.Introspection;

namespace Poly.Data.Modeling.TypeSystem;

public sealed record CanonicalBuiltInTypeDefinition(string Name, TypeCategory Category, IReadOnlyList<Constraint>? Constraints = null);

public static class CanonicalBuiltInTypeCatalog {
    public static IReadOnlyList<CanonicalBuiltInTypeDefinition> Definitions { get; } =
    [
        new("Boolean", TypeCategory.Primitive),
        new("Number",  TypeCategory.Primitive | TypeCategory.Numeric),
        new("Text",    TypeCategory.Primitive | TypeCategory.Text),
        new("Date",    TypeCategory.Primitive | TypeCategory.Temporal),
        new("Time",    TypeCategory.Primitive | TypeCategory.Temporal | TypeCategory.Duration),
        new("DateTime",TypeCategory.Primitive | TypeCategory.Temporal | TypeCategory.Instant),
        new("Duration",TypeCategory.Primitive | TypeCategory.Temporal | TypeCategory.Duration),
        new("Uuid",    TypeCategory.Primitive | TypeCategory.Identifier),
        new("Binary",  TypeCategory.Primitive | TypeCategory.Binary)
    ];

    public static void AddToMutation(Domain.Mutation mutation) {
        ArgumentNullException.ThrowIfNull(mutation);

        foreach (var definition in Definitions) {
            if (mutation.Domain.FindPrimitive(definition.Name) is not null) {
                continue;
            }

            mutation.AddType(new Primitive(mutation.Domain, definition.Name, definition.Category, definition.Constraints));
        }
    }
}