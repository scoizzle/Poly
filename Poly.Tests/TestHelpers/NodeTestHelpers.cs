using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Interpretation.SemanticAnalysis;
using Poly.Introspection;
using Expr = System.Linq.Expressions.Expression;
using Exprs = System.Linq.Expressions;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Helper methods for testing Node-based expressions using the proper middleware pattern.
/// </summary>
public static class NodeTestHelpers
{
    /// <summary>
    /// Builds a LINQ Expression Tree from a node using the middleware transformation pipeline.
    /// Applies semantic analysis recursively followed by LINQ expression transformation.
    /// </summary>
    /// <param name="node">The node to transform.</param>
    /// <param name="context">The interpretation context.</param>
    /// <returns>A LINQ Expression representation.</returns>
    public static Expr BuildExpression(this Node node, InterpretationContext context)
    {
        // Step 1: Recursively run semantic analysis on the entire tree
        AnalyzeNodeTree(context, node);
        
        // Step 2: Transform to LINQ expression
        var transformer = new LinqExpressionTransformer();
        transformer.SetContext(context);
        return node.Transform(transformer);
    }

    /// <summary>
    /// Recursively analyzes a node tree, applying semantic analysis to each node.
    /// </summary>
    private static void AnalyzeNodeTree(InterpretationContext context, Node node)
    {
        var semanticMiddleware = new SemanticAnalysisMiddleware<Expr>();
        semanticMiddleware.Transform(context, node, (ctx, n) => Expr.Empty());
        
        // Recursively analyze child nodes
        switch (node)
        {
            case Add add:
                AnalyzeNodeTree(context, add.LeftHandValue);
                AnalyzeNodeTree(context, add.RightHandValue);
                break;
            case Subtract sub:
                AnalyzeNodeTree(context, sub.LeftHandValue);
                AnalyzeNodeTree(context, sub.RightHandValue);
                break;
            case Multiply mul:
                AnalyzeNodeTree(context, mul.LeftHandValue);
                AnalyzeNodeTree(context, mul.RightHandValue);
                break;
            case Divide div:
                AnalyzeNodeTree(context, div.LeftHandValue);
                AnalyzeNodeTree(context, div.RightHandValue);
                break;
            case Modulo mod:
                AnalyzeNodeTree(context, mod.LeftHandValue);
                AnalyzeNodeTree(context, mod.RightHandValue);
                break;
            case UnaryMinus minus:
                AnalyzeNodeTree(context, minus.Operand);
                break;
            case And and:
                AnalyzeNodeTree(context, and.LeftHandValue);
                AnalyzeNodeTree(context, and.RightHandValue);
                break;
            case Or or:
                AnalyzeNodeTree(context, or.LeftHandValue);
                AnalyzeNodeTree(context, or.RightHandValue);
                break;
            case Not not:
                AnalyzeNodeTree(context, not.Value);
                break;
            case Equal eq:
                AnalyzeNodeTree(context, eq.LeftHandValue);
                AnalyzeNodeTree(context, eq.RightHandValue);
                break;
            case NotEqual ne:
                AnalyzeNodeTree(context, ne.LeftHandValue);
                AnalyzeNodeTree(context, ne.RightHandValue);
                break;
            case LessThan lt:
                AnalyzeNodeTree(context, lt.LeftHandValue);
                AnalyzeNodeTree(context, lt.RightHandValue);
                break;
            case LessThanOrEqual lte:
                AnalyzeNodeTree(context, lte.LeftHandValue);
                AnalyzeNodeTree(context, lte.RightHandValue);
                break;
            case GreaterThan gt:
                AnalyzeNodeTree(context, gt.LeftHandValue);
                AnalyzeNodeTree(context, gt.RightHandValue);
                break;
            case GreaterThanOrEqual gte:
                AnalyzeNodeTree(context, gte.LeftHandValue);
                AnalyzeNodeTree(context, gte.RightHandValue);
                break;
            case MemberAccess ma:
                AnalyzeNodeTree(context, ma.Value);
                break;
            case MethodInvocation mi:
                AnalyzeNodeTree(context, mi.Target);
                foreach (var arg in mi.Arguments)
                {
                    AnalyzeNodeTree(context, arg);
                }
                break;
            case IndexAccess ia:
                AnalyzeNodeTree(context, ia.Value);
                foreach (var arg in ia.Arguments)
                {
                    AnalyzeNodeTree(context, arg);
                }
                break;
            case Conditional cond:
                AnalyzeNodeTree(context, cond.Condition);
                AnalyzeNodeTree(context, cond.IfTrue);
                AnalyzeNodeTree(context, cond.IfFalse);
                break;
            case Coalesce coal:
                AnalyzeNodeTree(context, coal.LeftHandValue);
                AnalyzeNodeTree(context, coal.RightHandValue);
                break;
            case TypeCast cast:
                AnalyzeNodeTree(context, cast.Operand);
                break;
            case Block block:
                foreach (var stmt in block.Nodes)
                {
                    AnalyzeNodeTree(context, stmt);
                }
                break;
            case Assignment assignment:
                AnalyzeNodeTree(context, assignment.Value);
                break;
            // Leaf nodes (constants, parameters, variables) don't need child analysis
        }
    }

    /// <summary>
    /// Gets the ParameterExpression for a Parameter node using context-based caching.
    /// This ensures the same Parameter node always maps to the same ParameterExpression within that context.
    /// </summary>
    /// <param name="parameter">The parameter to convert.</param>
    /// <param name="context">The interpretation context managing parameter expressions.</param>
    /// <returns>A ParameterExpression.</returns>
    public static Exprs.ParameterExpression GetParameterExpression(this Parameter parameter, InterpretationContext context)
    {
        return LinqExpressionTransformer.GetParameterExpression(parameter, context);
    }

    /// <summary>
    /// Gets the resolved type definition for a node by running semantic analysis.
    /// </summary>
    /// <param name="node">The node to get the type for.</param>
    /// <param name="context">The interpretation context.</param>
    /// <returns>The type definition, or null if unknown.</returns>
    public static ITypeDefinition? GetResolvedType(this Node node, InterpretationContext context)
    {
        // Run semantic analysis on the entire tree
        AnalyzeNodeTree(context, node);
        
        return context.GetResolvedType(node);
    }
}
