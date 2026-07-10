namespace Poly.Interpretation;

/// <summary>
/// Equality comparer that uses reference equality (ReferenceEquals) instead of value equality.
/// Useful for caching keyed on node references where identity matters, not value.
/// NOTE: Not available via BCL in all target frameworks; kept as a local implementation
/// to avoid assembly reference dependencies.
/// </summary>
/// <example>
/// <code>
/// var cache = new Dictionary&lt;Node, SomeValue&gt;(ReferenceEqualityComparer.Instance);
/// </code>
/// </example>
public sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
    /// <summary>Gets the singleton instance.</summary>
    public static ReferenceEqualityComparer Instance { get; } = new();

    /// <summary>Returns true when both values are the same reference.</summary>
    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    /// <summary>Returns the hash code of the object reference (not the value).</summary>
    public int GetHashCode(object? obj) => obj?.GetHashCode() ?? 0;
}

/// <summary>
/// Generic reference equality comparer for typed dictionaries.
/// Uses <c>ReferenceEquals</c> for comparison instead of value equality.
/// </summary>
/// <typeparam name="T">The type of objects to compare. Must be a reference type.</typeparam>
/// <example>
/// <code>
/// var cache = new Dictionary&lt;Node, AnalysisResult&gt;(
///     ReferenceEqualityComparer&lt;Node&gt;.Instance);
/// </code>
/// </example>
public sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class {
    /// <summary>Gets the singleton instance for type <typeparamref name="T"/>.</summary>
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    /// <summary>Returns true when both values are the same reference.</summary>
    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T? obj) => obj?.GetHashCode() ?? 0;
}