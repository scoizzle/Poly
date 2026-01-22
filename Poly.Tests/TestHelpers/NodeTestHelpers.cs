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
    /// Helper to create a Parameter expression from a Parameter node.
    /// Used to match the parameter expressions created during interpretation.
    /// </summary>
    public static Exprs.ParameterExpression GetParameterExpression(this Parameter param)
    {
        // Extract the type from the type hint if present, otherwise default to object
        var type = GetTypeFromHint(param.TypeReference switch {
            TypeReference tr => tr.TypeName,
            null => null,
            _ => param.TypeReference.ToString()
        }) ?? typeof(object);

        return Expr.Parameter(type, param.Name);
    }

    /// <summary>
    /// Helper to create Parameter expressions for multiple parameters.
    /// </summary>
    public static Exprs.ParameterExpression[] GetParameterExpressions(params Parameter[] parameters)
    {
        return parameters.Select(p => p.GetParameterExpression()).ToArray();
    }

    /// <summary>
    /// Resolves a type from a type hint string.
    /// </summary>
    private static Type? GetTypeFromHint(string? typeHint)
    {
        if (string.IsNullOrWhiteSpace(typeHint))
            return null;

        return typeHint switch
        {
            "System.Int32" => typeof(int),
            "System.Double" => typeof(double),
            "System.String" => typeof(string),
            "System.Boolean" => typeof(bool),
            "System.Decimal" => typeof(decimal),
            "System.Single" => typeof(float),
            "System.Int64" => typeof(long),
            "System.Int16" => typeof(short),
            "System.Byte" => typeof(byte),
            "System.SByte" => typeof(sbyte),
            "System.UInt32" => typeof(uint),
            "System.UInt64" => typeof(ulong),
            "System.DateTime" => typeof(DateTime),
            "System.DateOnly" => typeof(DateOnly),
            "System.TimeOnly" => typeof(TimeOnly),
            "System.Guid" => typeof(Guid),
            _ => null
        };
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

}
