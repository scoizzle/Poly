namespace Poly.Introspection;

public static class TypeDefinitionExtensions {
    extension(ITypeDefinition typeDefinition) {
        /// <summary>
        /// Gets the best-matching method overloads for the given name and argument types.
        /// </summary>
        /// <param name="name">The method name.</param>
        /// <param name="argumentTypes">The types of the arguments to match against.</param>
        /// <returns>The best-matching methods, or an empty set if none found.</returns>
        public IEnumerable<ITypeMethod> FindMatchingMethodOverloads(
            string name,
            IEnumerable<ITypeDefinition> argumentTypes)
        {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(argumentTypes);

            return typeDefinition.Methods.WithName(name).WithParameterTypes(argumentTypes);
        }

        /// <summary>
        /// Determines if values of <paramref name="other"/> can be assigned to this type.
        /// </summary>
        /// <remarks>
        /// Default implementation walks the base type chain and interface list. Implementations
        /// can override with more precise or faster logic.
        /// </remarks>
        public bool IsAssignableFrom(ITypeDefinition other)
        {
            ArgumentNullException.ThrowIfNull(other);
            if (typeDefinition == other) return true;

            var current = other.BaseType;
            while (current != null) {
                if (typeDefinition == current) return true;
                current = current.BaseType;
            }

            if (other.Interfaces.Any(i => typeDefinition == i)) return true;

            return false;
        }

        /// <summary>
        /// Determines if this type can be assigned to <paramref name="other"/>.
        /// </summary>
        public bool IsAssignableTo(ITypeDefinition other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return other.IsAssignableFrom(typeDefinition);
        }
    }
}