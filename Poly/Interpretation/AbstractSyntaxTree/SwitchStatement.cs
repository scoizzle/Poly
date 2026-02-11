namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a switch statement that conditionally executes one of many branches based on a value.
/// </summary>
/// <remarks>
/// A value is matched against one or more case patterns, and the corresponding case body is executed.
/// A default case may be executed if no other cases match. All case bodies should have compatible types.
/// </remarks>
public sealed record SwitchStatement(Node Value, IReadOnlyList<SwitchCase> Cases, Node? DefaultCase = null) : Operator {
    public override IEnumerable<Node?> Children => [Value, .. Cases.SelectMany(c => c.Children), DefaultCase];

    /// <inheritdoc />
    public override string ToString()
    {
        var cases = string.Join(" ", Cases.Select(c => c.ToString()));
        var defaultStr = DefaultCase is not null ? $" default: {DefaultCase}" : "";
        return $"switch ({Value}) {{ {cases}{defaultStr} }}";
    }
}

/// <summary>
/// Represents a single case in a switch statement.
/// </summary>
/// <remarks>
/// A switch case matches a specific value (or set of values) and executes the associated body.
/// </remarks>
public sealed record SwitchCase(Node Pattern, Node Body) {
    public IEnumerable<Node?> Children => [Pattern, Body];

    /// <inheritdoc />
    public override string ToString() => $"case {Pattern}: {Body}";
}