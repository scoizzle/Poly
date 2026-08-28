using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Introspection;

/// <summary>User-defined conversion operator from a source type to a destination type.</summary>
public enum ConversionOperatorKind {
    Implicit,
    Explicit
}

/// <summary>
/// A conversion operator discovered on a type definition.
/// <see cref="Method"/> is the operator to invoke; CLR <c>Methods</c> omit
/// <c>IsSpecialName</c> members, so this is the discovery door.
/// </summary>
public readonly record struct ConversionOperator(ConversionOperatorKind Kind, ITypeMethod Method);

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
            if (typeDefinition is ClrTypeDefinition clrTypeDef && other is ClrTypeDefinition otherClrTypeDef
                && clrTypeDef.RuntimeType.IsAssignableFrom(otherClrTypeDef.RuntimeType))
                return true;

            var current = other.BaseType;
            while (current != null) {
                if (typeDefinition == current) return true;
                current = current.BaseType;
            }

            if (other.Interfaces.Any(i => typeDefinition == i)) return true;

            return typeDefinition.GetConversionFrom(other)?.Kind is ConversionOperatorKind.Implicit;
        }

        /// <summary>
        /// Determines if this type can be assigned to <paramref name="other"/>.
        /// </summary>
        public bool IsAssignableTo(ITypeDefinition other) {
            ArgumentNullException.ThrowIfNull(other);
            return other.IsAssignableFrom(typeDefinition);
        }

        /// <summary>
        /// User-defined conversion from <paramref name="source"/> to this type, if any.
        /// Implicit is preferred when both exist. Inheritance is not a conversion
        /// operator — use <see cref="IsAssignableFrom"/>.
        /// CLR conversion operators live outside <see cref="ITypeDefinition.Methods"/>
        /// (those omit <c>IsSpecialName</c>); this is the shared discovery API.
        /// </summary>
        public ConversionOperator? GetConversionFrom(ITypeDefinition source) {
            ArgumentNullException.ThrowIfNull(source);
            if (typeDefinition is ClrTypeDefinition destClr && source is ClrTypeDefinition sourceClr) {
                if (destClr.FindConversionFrom(sourceClr, ConversionOperatorKind.Implicit) is { } implicitMethod)
                    return new ConversionOperator(ConversionOperatorKind.Implicit, implicitMethod);
                if (destClr.FindConversionFrom(sourceClr, ConversionOperatorKind.Explicit) is { } explicitMethod)
                    return new ConversionOperator(ConversionOperatorKind.Explicit, explicitMethod);
                return null;
            }

            if (FindModeledConversion(source, typeDefinition, implicitName: true) is { } modeledImplicit)
                return new ConversionOperator(ConversionOperatorKind.Implicit, modeledImplicit);
            if (FindModeledConversion(source, typeDefinition, implicitName: false) is { } modeledExplicit)
                return new ConversionOperator(ConversionOperatorKind.Explicit, modeledExplicit);
            return null;
        }

        private static ITypeMethod? FindModeledConversion(ITypeDefinition from, ITypeDefinition to, bool implicitName) {
            var name = implicitName ? "op_Implicit" : "op_Explicit";
            foreach (var candidate in new[] { from, to }) {
                foreach (var method in candidate.Methods) {
                    if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                        continue;
                    var parameters = method.Parameters.ToArray();
                    if (parameters.Length == 1
                        && method.MemberTypeDefinition == to
                        && parameters[0].ParameterTypeDefinition == from)
                        return method;
                }
            }
            return null;
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