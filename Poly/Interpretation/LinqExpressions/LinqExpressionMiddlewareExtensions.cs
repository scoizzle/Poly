using System.Linq.Expressions;

namespace Poly.Interpretation.LinqExpressions;

public static class LinqExpressionMiddlewareExtensions
{
    extension(InterpreterBuilder<Expression> builder) {
        /// <summary>
        /// Adds LINQ expression compilation middleware to the interpreter builder.
        /// </summary>
        /// <param name="configure">An action to configure the LINQ expression middleware.</param>
        /// <returns>The updated interpreter builder.</returns>
        public InterpreterBuilder<Expression> UseLinqExpressionCompilation(Action<LinqExpressionMiddleware>? configure)
        {
            var middleware = new LinqExpressionMiddleware();
            configure?.Invoke(middleware);
            return builder.Use(middleware);
        }

        /// <summary>
        /// Adds LINQ expression compilation middleware to the interpreter builder with default configuration.
        /// </summary>
        /// <returns>The updated interpreter builder.</returns>
        public InterpreterBuilder<Expression> UseLinqExpressionCompilation() =>
            builder.UseLinqExpressionCompilation(null);
    }

    extension(InterpretationResult<Expression> result) {
        /// <summary>
        /// Gets the LINQ expression metadata from the interpretation result, if available.
        /// </summary>
        /// <returns>The LINQ expression metadata; otherwise, null.</returns>
        public LinqMetadata? GetMetadata() => result.GetMetadata<LinqMetadata>();

        /// <summary>
        /// Gets the parameters defined in the LINQ expression metadata.
        /// </summary>
        /// <returns>The collection of parameter expressions; otherwise, an empty collection.</returns>
        public IEnumerable<ParameterExpression> GetParameters()
        {
            var metadata = result.GetMetadata();
            return metadata?.Parameters?.Values ?? Enumerable.Empty<ParameterExpression>();
        }
    }
}
