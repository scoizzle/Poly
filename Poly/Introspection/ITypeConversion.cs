namespace Poly.Introspection;

/// <summary>
/// A conversion a platform states from <see cref="SourceTypeDefinition"/> to <see cref="ResultTypeDefinition"/>: an
/// implicit or an explicit one, computed either by a declared member (<see cref="Method"/>) or by the platform itself.
/// Implementations should be immutable and safe for concurrent reads.
/// </summary>
public interface ITypeConversion {
    /// <summary>
    /// Gets the kind of conversion this denotes.
    /// </summary>
    ConversionOperatorKind Kind { get; }

    /// <summary>
    /// Gets the type converted from.
    /// </summary>
    ITypeDefinition SourceTypeDefinition { get; }

    /// <summary>
    /// Gets the type converted to.
    /// </summary>
    ITypeDefinition ResultTypeDefinition { get; }

    /// <summary>
    /// Gets the member that computes the conversion, or null when the platform applies it itself.
    /// </summary>
    ITypeMethod? Method { get; }
}