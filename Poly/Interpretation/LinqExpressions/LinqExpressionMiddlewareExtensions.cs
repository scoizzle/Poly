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

    extension(InterpretationContext<Expression> context) {
        /// <summary>
        /// Gets the LINQ expression metadata from the interpretation context, if available.
        /// </summary>
        /// <returns>The LINQ expression metadata; otherwise, null.</returns>
        public LinqMetadata? GetLinqMetadata() => context.Metadata.Get<LinqMetadata>();

        internal ParameterExpression GetOrAddLinqParameter(Parameter param, Func<ParameterExpression> factory)
        {
            ArgumentNullException.ThrowIfNull(param);
            ArgumentNullException.ThrowIfNull(factory);

            var linqData = context.Metadata.GetOrAdd(static () => new LinqMetadata());
            if (!linqData.Parameters.TryGetValue(param.Name, out var expr))
            {
                expr = factory();
                linqData.Parameters[param.Name] = expr;
            }
            return expr;
        }
    }

    extension(InterpretationResult<Expression> result) {
        /// <summary>
        /// Gets the LINQ expression metadata from the interpretation result, if available.
        /// </summary>
        /// <returns>The LINQ expression metadata; otherwise, null.</returns>
        public LinqMetadata? GetLinqMetadata() => result.GetMetadata<LinqMetadata>();

        /// <summary>
        /// Gets the parameters defined in the LINQ expression metadata.
        /// </summary>
        /// <returns>The collection of parameter expressions; otherwise, an empty collection.</returns>
        public IEnumerable<ParameterExpression> GetParameters()
        {
            var metadata = result.GetLinqMetadata();
            return metadata?.Parameters?.Values ?? Enumerable.Empty<ParameterExpression>();
        }
    }
}
