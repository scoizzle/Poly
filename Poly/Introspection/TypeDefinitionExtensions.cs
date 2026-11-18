using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Introspection;

/// <summary>User-defined conversion operator from a source type to a destination type.</summary>
public enum ConversionOperatorKind {
    Implicit,
    Explicit
}

public static class TypeDefinitionExtensions {
    /// <summary>
    /// True when the type is one of its platform's ground types: its values are truth values, text, numbers, times,
    /// identifiers, bytes, or any value at all, not parts the model sees into. Derived from the type's own answers;
    /// no platform states it.
    /// </summary>
    public static bool IsGround(this ITypeDefinition type) =>
        type.IsBoolean || type.IsText || type.IsNumber || type.IsTemporal || type.IsUniqueIdentifier || type.IsBinary
        || type.IsTopType;

    /// <summary>
    /// The one type-identity notion, answered by the platform: <see cref="ITypeDefinition.IsSameTypeAs"/>.
    /// <see cref="IsAssignableFrom"/>, <c>GetOperator</c>, <c>GetCommonType</c> and overload scoring share it.
    /// </summary>
    internal static bool IsSameType(ITypeDefinition a, ITypeDefinition b) =>
        a is not null && a.IsSameTypeAs(b);

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
                .Where(static property => property.Name == "Item" && property.Parameters.Any())
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
                .Where(static property => property.Name == "Item" && property.Parameters.Any())
                .Select(static property => new {
                    Property = property,
                    Parameters = property.Parameters.ToArray(),
                    ReturnsObject = property.MemberTypeDefinition.IsTopType
                })
                .Where(candidate => candidate.Parameters.All(static parameter =>
                    parameter.ParameterTypeDefinition is { IsNumber: true, IsIntegral: true, IsNativeSized: false }))
                .OrderBy(candidate => candidate.Parameters.Length)
                .ThenBy(candidate => candidate.ReturnsObject)
                .FirstOrDefault();

            if (positionalIndexer != null && typeDefinition.IsCollectionShape) {
                return positionalIndexer.Property.MemberTypeDefinition;
            }

            return typeDefinition.SequenceElementType;
        }

        /// <summary>
        /// Determines if values of <paramref name="other"/> can be assigned to this type: no conversion is needed
        /// (<see cref="ITypeDefinition.IsAssignableWithoutConversion"/>), or a declared implicit operator does it.
        /// A conversion the platform applies itself (numeric widening) is not assignment; ask
        /// <see cref="ITypeDefinition.ResolveConversionFrom"/> for it.
        /// </summary>
        public bool IsAssignableFrom(ITypeDefinition other) {
            ArgumentNullException.ThrowIfNull(other);
            return typeDefinition.IsAssignableWithoutConversion(other)
                || typeDefinition.ResolveConversionFrom(other) is { Kind: ConversionOperatorKind.Implicit, Method: not null };
        }

        /// <summary>
        /// Determines if this type can be assigned to <paramref name="other"/>.
        /// </summary>
        public bool IsAssignableTo(ITypeDefinition other) {
            ArgumentNullException.ThrowIfNull(other);
            return other.IsAssignableFrom(typeDefinition);
        }
    }
}