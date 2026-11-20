namespace Poly.Introspection;

/// <summary>User-defined conversion operator from a source type to a destination type.</summary>
public enum ConversionOperatorKind {
    Implicit,
    Explicit
}

public static class TypeDefinitionExtensions {
    /// <summary>The conversion from <paramref name="source"/> into <paramref name="destination"/> that
    /// <paramref name="system"/> decides (#998 T9: no definition answers it).</summary>
    internal static ITypeConversion? ConversionInto(ITypeDefinition destination, ITypeDefinition source, ITypeSystem system) =>
        system.Conversion(source, destination);

    /// <summary>
    /// The one type-identity notion: two definitions are the same type when one system made both and that system says
    /// so (<see cref="ITypeSystem.IsSame"/>; #998 T9, decision §2.4). <see cref="IsAssignableFrom"/>, <c>GetOperator</c>,
    /// <c>GetCommonType</c> and overload scoring share it.
    /// </summary>
    internal static bool IsSameType(ITypeDefinition a, ITypeDefinition b) =>
        a is not null && b is not null && ReferenceEquals(a.System, b.System) && a.System.IsSame(a, b);

    /// <summary>The platform-neutral part of assignability: the same type, a base type, or an interface.</summary>
    internal static bool IsAssignableByHierarchy(ITypeDefinition destination, ITypeDefinition source) {
        if (IsSameType(destination, source))
            return true;
        for (var current = source.BaseType; current != null; current = current.BaseType) {
            if (destination == current)
                return true;
        }
        return source.Interfaces.Any(i => destination == i);
    }

    extension(ITypeDefinition typeDefinition) {
        /// <summary>The fields the type declares: a view over <see cref="ITypeDefinition.Members"/> (#998 T11).</summary>
        public IEnumerable<ITypeField> Fields => typeDefinition.Members.OfType<ITypeField>();

        /// <summary>The properties the type declares, indexers included: a view over its members (#998 T11).</summary>
        public IEnumerable<ITypeProperty> Properties => typeDefinition.Members.OfType<ITypeProperty>();

        /// <summary>The methods the type declares: a view over its members (#998 T11).</summary>
        public IEnumerable<ITypeMethod> Methods => typeDefinition.Members.OfType<ITypeMethod>();

        /// <summary>The constructors the type declares, a static one included: a view over its members (#998 T11).</summary>
        public IEnumerable<ITypeConstructor> Constructors => typeDefinition.Members.OfType<ITypeConstructor>();

        /// <summary>
        /// Gets the best-matching method overloads for the given name and argument types.
        /// </summary>
        /// <param name="name">The method name.</param>
        /// <param name="argumentTypes">The types of the arguments to match against.</param>
        /// <returns>The best-matching methods, or an empty set if none found.</returns>
        /// <param name="system">The type system whose conversions rank the candidates (#998 T4b).</param>
        public IEnumerable<ITypeMethod> FindMatchingMethodOverloads(
            string name,
            IEnumerable<ITypeDefinition> argumentTypes,
            ITypeSystem system) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(argumentTypes);

            return typeDefinition.Methods.WithName(name).WithParameterTypes(system, argumentTypes);
        }

        /// <summary>
        /// Gets the best-matching constructors for the given argument types.
        /// </summary>
        /// <param name="argumentTypes">The types of the arguments to match against.</param>
        /// <returns>The best-matching constructors, or an empty set if none found.</returns>
        public IEnumerable<ITypeConstructor> FindMatchingConstructors(
            IEnumerable<ITypeDefinition> argumentTypes,
            ITypeSystem system) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(argumentTypes);

            return typeDefinition.Constructors.WithParameterTypes(system, argumentTypes);
        }

        /// <summary>
        /// Gets the best-matching indexer properties for the given index argument types.
        /// </summary>
        /// <param name="indexParameterTypes">The types of the index arguments to match against.</param>
        /// <returns>The best-matching indexers, or an empty set if none found.</returns>
        public IEnumerable<ITypeProperty> FindMatchingIndexers(
            IEnumerable<ITypeDefinition> indexParameterTypes,
            ITypeSystem system) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(indexParameterTypes);

            return typeDefinition.Properties
                .Where(static property => property.Name == "Item" && property.Parameters.Any())
                .WithParameterTypes(system, indexParameterTypes);
        }

        /// <summary>The element type exposed by this type's indexer shape or sequence semantics, asked of
        /// <paramref name="system"/> (#998 T6c, T9); with index arguments, the indexer they select by its conversions.</summary>
        public ITypeDefinition? GetElementType(ITypeSystem system, params ITypeDefinition[] indexParameterTypes) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(indexParameterTypes);

            if (indexParameterTypes.Length > 0) {
                return typeDefinition.FindMatchingIndexers(indexParameterTypes, system).FirstOrDefault()?.MemberTypeDefinition;
            }
            ArgumentNullException.ThrowIfNull(system);
            return system.ElementType(typeDefinition);
        }

        /// <summary>
        /// Determines if values of <paramref name="other"/> can be assigned to this type: no conversion is needed
        /// (<see cref="ITypeSystem.IsAssignableWithoutConversion"/>), or a declared implicit operator does it.
        /// A conversion the platform applies itself (numeric widening) is not assignment; ask
        /// <see cref="ITypeSystem.Conversion"/> for it.
        /// </summary>
        /// <param name="system">The type system that decides (#998 T4b).</param>
        public bool IsAssignableFrom(ITypeDefinition other, ITypeSystem system) {
            ArgumentNullException.ThrowIfNull(other);
            return system.IsAssignableWithoutConversion(typeDefinition, other)
                || ConversionInto(typeDefinition, other, system) is { Kind: ConversionOperatorKind.Implicit, Method: not null };
        }

        /// <summary>
        /// Determines if this type can be assigned to <paramref name="other"/>.
        /// </summary>
        public bool IsAssignableTo(ITypeDefinition other, ITypeSystem system) {
            ArgumentNullException.ThrowIfNull(other);
            return other.IsAssignableFrom(typeDefinition, system);
        }
    }
}