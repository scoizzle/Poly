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
    /// True when <paramref name="type"/> exposes a sequence shape: a synthetic AST
    /// collection, or a CLR type that is an array or closes <c>IEnumerable&lt;&gt;</c>
    /// and is not itself a primitive (so <c>byte[]</c> stays binary). Used instead of
    /// a category flag because a shape is structural, not a declaration.
    /// </summary>
    internal static bool IsCollectionShape(ITypeDefinition type) {
        ArgumentNullException.ThrowIfNull(type);
        return type is AstCollectionTypeDefinition
            || (type is ClrTypeDefinition clr
                && ((ITypeDefinition)clr).PrimitiveType is null
                && (clr.RuntimeType.IsArray
                    || clr.RuntimeType.GetInterfaces().Any(static i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))));
    }

    /// <summary>
    /// The one type-identity notion: reference identity, two synthetic collections of one kind and element, or two
    /// resolutions of one generic parameter. Each resolve of a synthetic collection or a generic parameter builds a fresh
    /// instance, so reference equality alone never matches an omitted collection argument.
    /// <see cref="IsAssignableFrom"/>, <c>GetOperator</c>, <c>GetCommonType</c> and overload scoring share it.
    /// </summary>
    internal static bool IsSameType(ITypeDefinition a, ITypeDefinition b) =>
        ReferenceEquals(a, b) || SyntheticCollectionsMatch(a, b) || GenericParametersMatch(a, b);

    /// <summary>
    /// A generic parameter is its name on its declaring type. The definition holds no declaring member, so two
    /// method-level parameters of one name on one type are the same type here: a stated limitation, and no DSL construct
    /// declares a method-level type parameter.
    /// </summary>
    private static bool GenericParametersMatch(ITypeDefinition a, ITypeDefinition b) =>
        a is AstGenericParameterTypeDefinition left
        && b is AstGenericParameterTypeDefinition right
        && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && IsSameType(left.DeclaringType, right.DeclaringType);

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

    private static bool SyntheticCollectionsMatch(ITypeDefinition to, ITypeDefinition from) {
        if (to is not AstCollectionTypeDefinition left
            || from is not AstCollectionTypeDefinition right)
            return false;
        if (left.Kind != right.Kind)
            return false;
        // A nested collection's name does not carry its element's namespace, so it compares by this notion again.
        if (left.ElementType is AstCollectionTypeDefinition || right.ElementType is AstCollectionTypeDefinition)
            return IsSameType(left.ElementType, right.ElementType);
        return string.Equals(left.ElementType.Name, right.ElementType.Name, StringComparison.Ordinal)
            && string.Equals(left.ElementType.Namespace, right.ElementType.Namespace, StringComparison.Ordinal);
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
                    ReturnsObject = property.MemberTypeDefinition.TryGetRuntimeType(out var runtimeType) && runtimeType == typeof(object)
                })
                .Where(candidate => candidate.Parameters.All(static parameter =>
                    parameter.ParameterTypeDefinition.PrimitiveType is { } primitive && primitive.IsInteger()))
                .OrderBy(candidate => candidate.Parameters.Length)
                .ThenBy(candidate => candidate.ReturnsObject)
                .FirstOrDefault();

            if (positionalIndexer != null && TypeDefinitionExtensions.IsCollectionShape(typeDefinition)) {
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

        /// <summary>
        /// Returns true when values of this type are stored directly on an
        /// evaluation stack slot without heap indirection (numeric types and
        /// booleans).  Domain entities, strings, and structured types return false.
        /// </summary>
        public bool IsStackValue() => typeDefinition.PrimitiveType is { } pt
            ? pt.IsStackValue()
            : false;

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