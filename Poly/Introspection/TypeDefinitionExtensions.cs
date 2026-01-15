using Poly.Introspection.Extensions;

namespace Poly.Introspection;

public static class TypeDefinitionExtensions {
    /// <summary>
    /// Gets the best-matching method overloads for the given name and argument types.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="argumentTypes">The types of the arguments to match against.</param>
    /// <returns>The best-matching methods, or an empty set if none found.</returns>
    public static IEnumerable<ITypeMethod> FindMatchingMethodOverloads(
        this ITypeDefinition typeDefinition,
        string name,
        IEnumerable<ITypeDefinition> argumentTypes
    ) {
        ArgumentNullException.ThrowIfNull(typeDefinition);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(argumentTypes);

        return typeDefinition.Methods.WithName(name).WithParameterTypes(argumentTypes);
    }
}