using Poly.Ast.Nodes;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR host's factory for a tree reference to a runtime type. It states the platform's definition
/// (<see cref="TypeDefinitionReference"/>), never a spelling of the type's name: two types that share a full name are
/// still different types, and an implementer is assignable to the interface it implements. A nullable is an
/// <see cref="OptionalTypeReference"/> over its underlying reference. No node carries a <c>System.Type</c>.
/// The text a reference prints is unchanged, which <c>ClrReferencePrintTests</c> pins.
/// </summary>
public static class ClrTypeReferences {
    /// <summary>A reference to <typeparamref name="T"/>.</summary>
    public static Node To<T>() => Of(typeof(T));

    /// <summary>A reference to <paramref name="type"/>: an optional over its underlying reference when it is nullable,
    /// else the platform's own definition of the type.</summary>
    public static Node Of(Type type) {
        ArgumentNullException.ThrowIfNull(type);
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return new OptionalTypeReference(Of(underlying));
        return new TypeDefinitionReference(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type));
    }
}