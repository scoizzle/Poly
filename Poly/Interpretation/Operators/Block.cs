using Poly.Introspection;

namespace Poly.Interpretation.Operators;

/// <summary>
/// Represents a block expression that executes a sequence of expressions and returns the result of the last expression.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Block(IEnumerable{Expression})"/> which executes expressions in sequence.
/// This is useful for combining multiple operations, variable declarations, and statements into a single expression.
/// The block's type is determined by the type of the last expression in the sequence.
/// </remarks>
public sealed class Block : Operator {
    /// <summary>
    /// Gets the sequence of expressions to execute.
    /// </summary>
    public IReadOnlyList<Interpretable> Expressions { get; }

    /// <summary>
    /// Gets the optional variables declared within this block's scope.
    /// </summary>
    public IReadOnlyList<ParameterExpression> Variables { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Block"/> class with a sequence of expressions.
    /// </summary>
    /// <param name="expressions">The expressions to execute in sequence.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expressions"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="expressions"/> is empty.</exception>
    public Block(params Interpretable[] expressions) : this(expressions, Array.Empty<ParameterExpression>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Block"/> class with expressions and local variables.
    /// </summary>
    /// <param name="expressions">The expressions to execute in sequence.</param>
    /// <param name="variables">The variables declared within this block's scope.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expressions"/> or <paramref name="variables"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="expressions"/> is empty.</exception>
    public Block(IEnumerable<Interpretable> expressions, IEnumerable<ParameterExpression> variables)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        ArgumentNullException.ThrowIfNull(variables);

        Expressions = expressions.ToList().AsReadOnly();
        Variables = variables.ToList().AsReadOnly();

        if (Expressions.Count == 0) {
            throw new ArgumentException("Block must contain at least one expression.", nameof(expressions));
        }
    }

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context)
    {
        // The block's type is the type of the last expression
        var lastExpression = Expressions[^1];

        if (lastExpression is Value lastValue) {
            return lastValue.GetTypeDefinition(context);
        }

        // If the last expression is not a Value, we can't determine its type
        // Default to void/object
        return context.GetTypeDefinition<object>()
            ?? throw new InvalidOperationException("Unable to determine block type.");
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context)
    {
        context.PushScope();
        try {
            var builtExpressions = Expressions
                .Select(expr => expr.BuildExpression(context))
                .ToList();

            return Variables.Count > 0
                ? Expression.Block(Variables, builtExpressions)
                : Expression.Block(builtExpressions);
        }
        finally {
            context.PopScope();
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{{ {string.Join("; ", Expressions)} }}";
    }
}