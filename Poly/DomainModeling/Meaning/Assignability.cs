namespace Poly.DomainModeling.Meaning;

/// <summary>How a value of one <see cref="ExprType"/> relates to a target type's definition.</summary>
internal enum Assignability {
    /// <summary>No conversion exists.</summary>
    None,

    /// <summary>Same definition.</summary>
    Identity,

    /// <summary>An implicit conversion exists.</summary>
    Widening,

    /// <summary>
    /// An explicit conversion exists; whether it is permitted is the caller's decision.
    /// Not a rounding-mode promise (OQ-3 is T-12).
    /// </summary>
    RoundingNarrowing
}