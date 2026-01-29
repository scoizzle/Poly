namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a stable identifier for an AST node.
/// Used as a dictionary key for metadata storage and caching.
/// Ensures correct incremental analysis by providing node identity across parser runs.
/// </summary>
public readonly record struct NodeId {
    private NodeId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>
    /// The string value of the node identifier.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new unique node identifier using a GUID.
    /// Used for synthetic or programmatically-created nodes.
    /// </summary>
    public static NodeId NewId() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates a node identifier from source position.
    /// Recommended for incremental analysis - same position yields same ID across parses.
    /// </summary>
    /// <param name="line">Source line number (1-based).</param>
    /// <param name="column">Source column number (1-based).</param>
    /// <param name="name">Optional name hint for debugging.</param>
    public static NodeId FromPosition(int line, int column, string? name = null)
        => new(name != null ? $"{name}_{line}_{column}" : $"node_{line}_{column}");

    /// <summary>
    /// Creates a node identifier from structural hash.
    /// Useful for non-positional sources or when position tracking is unavailable.
    /// </summary>
    public static NodeId FromHash(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return new(Convert.ToHexString(hashBytes)[..16].ToLowerInvariant());
    }

    /// <summary>
    /// Parses a string into a NodeId.
    /// </summary>
    public static NodeId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new(value);
    }

    /// <summary>
    /// Returns the string representation of this node identifier.
    /// </summary>
    public override string ToString() => Value;

    /// <summary>
    /// Implicit conversion to string for compatibility.
    /// </summary>
    public static explicit operator string(NodeId id) => id.Value;
}