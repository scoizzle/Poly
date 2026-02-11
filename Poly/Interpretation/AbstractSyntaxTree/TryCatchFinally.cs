namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a try-catch-finally statement that handles exceptions.
/// </summary>
/// <remarks>
/// The try block is executed; if an exception occurs, it is matched against catch clauses.
/// The finally block (if present) is guaranteed to execute regardless of normal or exceptional completion.
/// At least one catch or finally clause must be present.
/// </remarks>
public sealed record TryCatchFinally(Node TryBlock, IReadOnlyList<CatchClause>? CatchClauses = null, Node? FinallyBlock = null) : Operator {
    public override IEnumerable<Node?> Children =>
        [TryBlock, .. (CatchClauses ?? new List<CatchClause>()).SelectMany(c => c.Children), FinallyBlock];

    /// <inheritdoc />
    public override string ToString()
    {
        var catches = CatchClauses != null ? string.Join(" ", CatchClauses.Select(c => c.ToString())) : "";
        var finallyStr = FinallyBlock is not null ? $" finally {{ {FinallyBlock} }}" : "";
        return $"try {{ {TryBlock} }} {catches}{finallyStr}";
    }
}

/// <summary>
/// Represents a single catch clause in a try-catch-finally statement.
/// </summary>
/// <remarks>
/// A catch clause specifies the exception type to handle and the body to execute when an exception of that type is raised.
/// The optional variable name binds the caught exception for use within the body.
/// </remarks>
public sealed record CatchClause(Node? ExceptionType, string? VariableName, Node Body) {
    public IEnumerable<Node?> Children => [ExceptionType, Body];

    /// <inheritdoc />
    public override string ToString()
    {
        var exceptionPart = ExceptionType is not null ? ExceptionType.ToString() : "Exception";
        var varPart = VariableName is not null ? $" {VariableName}" : "";
        return $"catch ({exceptionPart}{varPart}) {{ {Body} }}";
    }
}