namespace Poly.Interpretation;

/// <summary>
/// Equality comparer that uses reference equality (ReferenceEquals) instead of value equality.
/// Useful for caching keyed on node references where identity matters, not value.
/// </summary>
public sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    /// <summary>
    /// Gets the singleton instance of the ReferenceEqualityComparer.
    /// </summary>
    public static ReferenceEqualityComparer Instance { get; } = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object? obj) => obj?.GetHashCode() ?? 0;
}

/// <summary>
/// Generic version of reference equality comparer for use with generic types.
/// </summary>
public sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
{
    /// <summary>
    /// Gets the singleton instance of the ReferenceEqualityComparer{T}.
    /// </summary>
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T? obj) => obj?.GetHashCode() ?? 0;
}
