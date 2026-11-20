using Poly.Introspection.CommonLanguageRuntime;


namespace Poly.Interpretation.CSharp;

/// <summary>
/// C#'s view of a CLR type in a tree, whose checks ask C#'s system, so it lives on the C# surface rather than in
/// Introspection, which names no AST (#998 T13), or in the AST, which names no <c>typeof</c> (I8d). A reference states
/// the platform's definition (<see cref="TypeDefinitionReference"/>), never a spelling of the type's name: two types
/// that share a full name are still different types, and an implementer is assignable to the interface it implements.
/// A nullable is an <see cref="OptionalTypeReference"/> over its underlying reference. No node carries a
/// <c>System.Type</c>. The text a reference prints is unchanged, which <c>ClrReferencePrintTests</c> pins.
/// </summary>
internal static class TypeReferenceExtensions {
    extension(TypeReference) {
        /// <summary>A reference to <typeparamref name="T"/>.</summary>
        public static Node To<T>() => TypeReference.To(typeof(T));

        /// <summary>A reference to <paramref name="type"/>: an optional over its underlying reference when it is
        /// nullable, else the platform's own definition of the type.</summary>
        public static Node To(Type type) {
            ArgumentNullException.ThrowIfNull(type);
            if (Nullable.GetUnderlyingType(type) is { } underlying)
                return new OptionalTypeReference(TypeReference.To(underlying));
            return new TypeDefinitionReference(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type));
        }
    }

    extension(Node? reference) {
        /// <summary>True when the reference states a definition the platform's <paramref name="type"/> accepts
        /// without a conversion: the type itself, a subtype or an implementer. A name is not a type, so a reference
        /// that only spells one denotes nothing.</summary>
        public bool Denotes(Type type) {
            ArgumentNullException.ThrowIfNull(type);
            return reference is TypeDefinitionReference { TypeDefinition: var stated }
                && CSharpTypeSystem.Shared.IsAssignableWithoutConversion(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type), stated);
        }

        /// <summary>True when the reference states exactly the platform's definition of <paramref name="type"/>:
        /// identity, where <see cref="Denotes"/> is assignability.</summary>
        public bool States(Type type) {
            ArgumentNullException.ThrowIfNull(type);
            return reference is TypeDefinitionReference { TypeDefinition: var stated }
                && TypeDefinitionExtensions.IsSameType(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type), stated);
        }
    }
}