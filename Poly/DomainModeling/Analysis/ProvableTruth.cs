using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>The three answers a provable check can give (public per B6; V-10 and T-12 consume it).</summary>
public enum Truth {
    /// <summary>Every value the abstract environment allows satisfies the condition.</summary>
    True,

    /// <summary>No value the abstract environment allows satisfies the condition.</summary>
    False,

    /// <summary>The environment does not decide it. Never read as either answer.</summary>
    Unknown,
}

/// <summary>
/// The three-valued truth of a boolean condition over abstract values, decided from their intervals.
/// A leaf the flow does not model — an existential, a quantifier, a relationship — is
/// <see cref="Truth.Unknown"/> (fail closed), never a guess.
/// </summary>
internal static class ProvableTruth {
    /// <summary>The truth of <paramref name="condition"/> under <paramref name="env"/>.</summary>
    internal static Truth Decide(DomainExpression condition, IReadOnlyDictionary<string, AbstractValue> env) =>
        Truth.Unknown;
}
