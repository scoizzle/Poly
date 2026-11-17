namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR host's factory for a tree reference to a runtime type. It builds the neutral nodes the tree already has —
/// <see cref="NamedTypeReference"/> for a name (with its generic arguments), <see cref="OptionalTypeReference"/> for a
/// nullable, and <see cref="PrimitiveTypeReference"/> for <c>object</c> — so no node in the tree carries a
/// <c>System.Type</c>. The shapes match what <c>ClrTypeReference</c> printed, which <c>ClrReferencePrintTests</c> pins.
/// </summary>
public static class ClrTypeReferences {
    /// <summary>A reference to <typeparamref name="T"/>.</summary>
    public static Node To<T>() => Of(typeof(T));

    /// <summary>A reference to <paramref name="type"/>: a nullable, <c>object</c>, a generic (name plus arguments), or a
    /// plain name. A nested type's name is dotted, as C# requires it.</summary>
    public static Node Of(Type type) {
        ArgumentNullException.ThrowIfNull(type);
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return new OptionalTypeReference(Of(underlying));
        if (type == typeof(object))
            return new PrimitiveTypeReference(PrimitiveType.Structure);
        if (type.IsGenericType) {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments().Select(Of).ToArray();
            return new NamedTypeReference(definition.Name, definition.Namespace, arguments);
        }
        return new NamedTypeReference(type.FullName ?? type.Name);
    }
}