namespace Poly.DomainModeling.Ontology;

/// <summary>
/// Closed value type for annotation arguments. Only these types may appear
/// in <see cref="Annotation.Arguments"/> values, guaranteeing equality,
/// hash stability, and round-trip fidelity.
/// </summary>
public abstract record AnnotationValue;

/// <summary>A string annotation argument.</summary>
/// <param name="Value">The string value.</param>
public sealed record AnnotationString(string Value) : AnnotationValue;

/// <summary>A numeric annotation argument.</summary>
/// <param name="Value">The numeric value.</param>
public sealed record AnnotationNumber(double Value) : AnnotationValue;

/// <summary>A boolean annotation argument.</summary>
/// <param name="Value">The boolean value.</param>
public sealed record AnnotationBool(bool Value) : AnnotationValue;

/// <summary>A null annotation argument.</summary>
public sealed record AnnotationNull : AnnotationValue;