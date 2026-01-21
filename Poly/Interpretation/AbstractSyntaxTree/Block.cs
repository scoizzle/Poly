namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a block expression that executes a sequence of expressions and returns the result of the last expression.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expr.Block(IEnumerable{Expr})"/> which executes expressions in sequence.
/// This is useful for combining multiple operations, variable declarations, and statements into a single expression.
/// The block's type is determined by the type of the last expression in the sequence.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Block : Operator {
    /// <summary>
    /// Gets the sequence of expressions to execute.
    /// </summary>
    public IReadOnlyList<Node> Nodes { get; }

    /// <summary>
    /// Gets the optional variables declared within this block's scope.
    /// </summary>
    public IReadOnlyList<Node> Variables { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Block"/> class with a sequence of expressions.
    /// </summary>
    /// <param name="expressions">The expressions to execute in sequence.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expressions"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="expressions"/> is empty.</exception>
    public Block(params Node[] expressions) : this(expressions, Array.Empty<Node>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Block"/> class with expressions and local variables.
    /// </summary>
    /// <param name="expressions">The expressions to execute in sequence.</param>
    /// <param name="variables">The variables declared within this block's scope.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expressions"/> or <paramref name="variables"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="expressions"/> is empty.</exception>
    public Block(IEnumerable<Node> expressions, IEnumerable<Node> variables)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        ArgumentNullException.ThrowIfNull(variables);

        Nodes = expressions.ToList().AsReadOnly();
        Variables = variables.ToList().AsReadOnly();

        if (Nodes.Count == 0) {
            throw new ArgumentException("Block must contain at least one expression.", nameof(expressions));
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{{ {string.Join("; ", Nodes)} }}";
    }
}