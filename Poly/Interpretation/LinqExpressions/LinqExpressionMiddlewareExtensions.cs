using System.Linq.Expressions;

namespace Poly.Interpretation.LinqExpressions;

public static class LinqExpressionMiddlewareExtensions
{
    extension(InterpreterBuilder<Expression> builder) {
        /// <summary>
        /// Adds middleware to compile abstract syntax tree nodes into LINQ expression trees.
        /// </summary>
        /// <returns>>The updated interpreter builder.</returns>
        public InterpreterBuilder<Expression> WithLinqExpressionCompilation() => builder.Use(new LinqExpressionMiddleware());
    }

    extension(InterpretationResult<Expression> result) {
        /// <summary>
        /// Gets the LINQ expression metadata from the interpretation result, if available.
        /// </summary>
        /// <returns>>The LINQ expression metadata; otherwise, null.</returns>
        public LinqMetadata? GetMetadata() => result.GetMetadata<LinqMetadata>();
    }
}
