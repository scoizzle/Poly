using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Introspection;

/// <summary>User-defined conversion operator from a source type to a destination type.</summary>
public enum ConversionOperatorKind {
    Implicit,
    Explicit
}

public static class TypeDefinitionExtensions {
    /// <summary>The conversion from <paramref name="source"/> into <paramref name="destination"/> that
    /// <paramref name="system"/> decides; interim (removed by T9), with no system, the destination's own door.</summary>
    internal static ITypeConversion? ConversionInto(ITypeDefinition destination, ITypeDefinition source, ITypeSystem? system) =>
        system is null ? destination.ResolveConversionFrom(source) : system.Conversion(source, destination);

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
        /// <summary>
        /// Gets the best-matching method overloads for the given name and argument types.
        /// </summary>
        /// <param name="name">The method name.</param>
        /// <param name="argumentTypes">The types of the arguments to match against.</param>
        /// <returns>The best-matching methods, or an empty set if none found.</returns>
        /// <param name="system">The type system whose conversions rank the candidates (#998 T4b). Interim, removed by T9:
        /// null asks each parameter type's own door.</param>
        public IEnumerable<ITypeMethod> FindMatchingMethodOverloads(
            string name,
            IEnumerable<ITypeDefinition> argumentTypes,
            ITypeSystem? system = null) {
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
            ITypeSystem? system = null) {
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
            ITypeSystem? system = null) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(indexParameterTypes);

            return typeDefinition.Properties
                .Where(static property => property.Name == "Item" && property.Parameters.Any())
                .WithParameterTypes(system, indexParameterTypes);
        }

        /// <summary>
        /// Gets the element type exposed by this type's indexer shape or sequence semantics.
        /// </summary>
        /// <param name="indexParameterTypes">Index argument types that select the indexer. With none, the element type is
        /// a type system's rule, so this overload throws: pass the system (#998 T8).</param>
        /// <returns>The selected indexer's type, or null when none matches.</returns>
        public ITypeDefinition? GetElementType(params ITypeDefinition[] indexParameterTypes) =>
            typeDefinition.GetElementType(null, indexParameterTypes);

        /// <summary>As <see cref="GetElementType(ITypeDefinition[])"/>, with the indexer chosen by
        /// <paramref name="system"/>'s conversions (#998 T4b); with no index arguments, <paramref name="system"/>'s element
        /// type (required). Interim, removed by T9: with index arguments, a null system asks each parameter type's
        /// door.</summary>
        public ITypeDefinition? GetElementType(ITypeSystem? system, params ITypeDefinition[] indexParameterTypes) {
            ArgumentNullException.ThrowIfNull(typeDefinition);
            ArgumentNullException.ThrowIfNull(indexParameterTypes);

            if (indexParameterTypes.Length > 0) {
                return typeDefinition.FindMatchingIndexers(indexParameterTypes, system).FirstOrDefault()?.MemberTypeDefinition;
            }
            // An element type with no index arguments is the target's rule (C#'s positional indexer, #998 T6c): with no
            // system there is no one to ask, so the question fails closed (T8 removed the pick over a type's own answers).
            if (system is null)
                throw new ArgumentException("The element type of a sequence is its type system's rule: pass the system.", nameof(system));
            return system.ElementType(typeDefinition);
        }

        /// <summary>
        /// Determines if values of <paramref name="other"/> can be assigned to this type: no conversion is needed
        /// (<see cref="ITypeDefinition.IsAssignableWithoutConversion"/>), or a declared implicit operator does it.
        /// A conversion the platform applies itself (numeric widening) is not assignment; ask
        /// <see cref="ITypeDefinition.ResolveConversionFrom"/> for it.
        /// </summary>
        /// <param name="system">The type system that decides (#998 T4b). Interim, removed by T9: null asks this
        /// definition's own doors.</param>
        public bool IsAssignableFrom(ITypeDefinition other, ITypeSystem? system = null) {
            ArgumentNullException.ThrowIfNull(other);
            return (system?.IsAssignableWithoutConversion(typeDefinition, other) ?? typeDefinition.IsAssignableWithoutConversion(other))
                || ConversionInto(typeDefinition, other, system) is { Kind: ConversionOperatorKind.Implicit, Method: not null };
        }

        /// <summary>
        /// Determines if this type can be assigned to <paramref name="other"/>.
        /// </summary>
        public bool IsAssignableTo(ITypeDefinition other, ITypeSystem? system = null) {
            ArgumentNullException.ThrowIfNull(other);
            return other.IsAssignableFrom(typeDefinition, system);
        }
    }
}