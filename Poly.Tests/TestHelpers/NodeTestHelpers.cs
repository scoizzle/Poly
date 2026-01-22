using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.LinqExpressions;
using Poly.Interpretation.SemanticAnalysis;

using Expr = System.Linq.Expressions.Expression;
using Exprs = System.Linq.Expressions;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Helper methods for testing Node-based expressions using the middleware interpreter pattern.
/// </summary>
public static class NodeTestHelpers
{
    /// <summary>
    /// Creates a standard interpreter for testing that applies semantic analysis and compiles to LINQ expressions.
    /// </summary>
    public static Interpreter<Expr> CreateTestInterpreter()
    {
        return new InterpreterBuilder<Expr>()
            .UseSemanticAnalysis()
            .UseLinqExpressionCompilation()
            .Build();
    }

    /// <summary>
    /// Builds a LINQ Expression Tree from a node using the standard test interpreter pipeline.
    /// </summary>
    /// <param name="node">The node to transform.</param>
    /// <returns>A LINQ Expression representation.</returns>
    public static Expr BuildExpression(this Node node)
    {
        var interpreter = CreateTestInterpreter();
        var result = interpreter.Interpret(node);
        return result.Value;
    }

    /// <summary>
    /// Builds a LINQ Expression Tree from a node with a custom inline middleware for debugging/inspection.
    /// </summary>
    /// <param name="node">The node to transform.</param>
    /// <param name="customMiddleware">Optional custom middleware to insert in the pipeline.</param>
    /// <returns>A LINQ Expression representation.</returns>
    public static Expr BuildExpression(this Node node, Func<InterpretationContext<Expr>, Node, TransformationDelegate<Expr>, Expr>? customMiddleware = null)
    {
        var builder = new InterpreterBuilder<Expr>();
        
        if (customMiddleware != null)
        {
            builder.Use(customMiddleware);
        }
        
        var interpreter = builder
            .UseSemanticAnalysis()
            .UseLinqExpressionCompilation()
            .Build();
        
        var result = interpreter.Interpret(node);
        return result.Value;
    }

    /// <summary>
    /// Builds a LINQ Expression and collects generated parameter expressions based on declared parameters.
    /// </summary>
    /// <param name="node">The node to transform.</param>
    /// <param name="parameters">Parameter declarations (node, CLR type) to register before interpretation.</param>
    /// <returns>Tuple of expression and generated parameter expressions.</returns>
    public static (Expr Expression, Exprs.ParameterExpression[] Parameters) BuildExpressionWithParameters(
        this Node node,
        params (Parameter param, Type clrType)[] parameters)
    {
        var interpreter = new InterpreterBuilder<Expr>()
            .UseSemanticAnalysis()
            .UseLinqExpressionCompilation()
            .Build();

        IInterpreterResultProvider<Expr> pipeline = interpreter;

        foreach (var (param, clrType) in parameters)
        {
            pipeline = pipeline.WithParameter(param, clrType);
        }

        var result = pipeline.Interpret(node);
        var parameterExpressions = result.GetParameters().ToArray();
        return (result.Value, parameterExpressions);
    }

    /// <summary>
    /// Compiles a node into a delegate, registering provided parameters and using emitted parameter expressions.
    /// </summary>
    public static TDelegate CompileLambda<TDelegate>(this Node node, params (Parameter param, Type clrType)[] parameters)
        where TDelegate : Delegate
    {
        var (expression, parameterExpressions) = node.BuildExpressionWithParameters(parameters);
        return (TDelegate)System.Linq.Expressions.Expression.Lambda(expression, parameterExpressions).Compile();
    }

}
