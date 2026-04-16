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
            IEnumerable<ITypeDefinition> argumentTypes) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(argumentTypes);

            return typeDefinition.Methods.WithName(name).WithParameterTypes(argumentTypes);
        }

        /// <summary>
        /// Gets the best-matching constructors for the given argument types.
        /// </summary>
        /// <param name="argumentTypes">The types of the arguments to match against.</param>
        /// <returns>The best-matching constructors, or an empty set if none found.</returns>
        public IEnumerable<ITypeConstructor> FindMatchingConstructors(
            IEnumerable<ITypeDefinition> argumentTypes) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(argumentTypes);

            return typeDefinition.Constructors.WithParameterTypes(argumentTypes);
        }

        /// <summary>
        /// Gets the best-matching indexer properties for the given index argument types.
        /// </summary>
        /// <param name="indexParameterTypes">The types of the index arguments to match against.</param>
        /// <returns>The best-matching indexers, or an empty set if none found.</returns>
        public IEnumerable<ITypeProperty> FindMatchingIndexers(
            IEnumerable<ITypeDefinition> indexParameterTypes) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(indexParameterTypes);

            return typeDefinition.Properties
                .Where(static property => property is { Parameters: not null, Name: "Item" })
                .WithParameterTypes(indexParameterTypes);
        }

        /// <summary>
        /// Gets the element type exposed by this type's indexer shape or sequence semantics.
        /// </summary>
        /// <param name="indexParameterTypes">Optional index argument types used to select the appropriate indexer.</param>
        /// <returns>The resolved element type, or null if no element type can be determined.</returns>
        public ITypeDefinition? GetElementType(params ITypeDefinition[] indexParameterTypes) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(indexParameterTypes);

            if (indexParameterTypes.Length > 0) {
                return typeDefinition.FindMatchingIndexers(indexParameterTypes).FirstOrDefault()?.MemberTypeDefinition;
            }

            var positionalIndexer = typeDefinition.Properties
                .Where(static property => property is { Parameters: not null, Name: "Item" })
                .Select(static property => new {
                    Property = property,
                    Parameters = property.Parameters!.ToArray(),
                    ReturnsObject = property.MemberTypeDefinition.TryGetRuntimeType(out var runtimeType) && runtimeType == typeof(object)
                })
                .Where(candidate => candidate.Parameters.All(static parameter => parameter.ParameterTypeDefinition.TypeCategory.Is(TypeCategory.Integer)))
                .OrderBy(candidate => candidate.Parameters.Length)
                .ThenBy(candidate => candidate.ReturnsObject)
                .FirstOrDefault();

            if (positionalIndexer != null && (typeDefinition.TypeCategory.IsCollection || !typeDefinition.TypeCategory.Is(TypeCategory.Keyed))) {
                return positionalIndexer.Property.MemberTypeDefinition;
            }

            if (!typeDefinition.TryGetRuntimeType(out var reflectedType)) {
                return null;
            }

            var enumerableType = GetGenericEnumerableType(reflectedType);
            if (enumerableType != null) {
                var elementClrType = enumerableType.GetGenericArguments()[0];
                return CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(elementClrType);
            }

            return typeof(IEnumerable).IsAssignableFrom(reflectedType)
                ? CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(object))
                : null;
        }

        /// <summary>
        /// Determines if values of <paramref name="other"/> can be assigned to this type.
        /// </summary>
        /// <remarks>
        /// Default implementation walks the base type chain and interface list. Implementations
        /// can override with more precise or faster logic.
        /// </remarks>
        public bool IsAssignableFrom(ITypeDefinition other) {
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
        public bool IsAssignableTo(ITypeDefinition other) {
            ArgumentNullException.ThrowIfNull(other);
            return other.IsAssignableFrom(typeDefinition);
        }

        private static Type? GetGenericEnumerableType(Type type) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
                return type;
            }

            return type
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }
    }
}