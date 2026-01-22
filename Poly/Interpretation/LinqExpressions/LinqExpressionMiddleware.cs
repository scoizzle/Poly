using System.Linq.Expressions;

using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Interpretation.SemanticAnalysis;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.LinqExpressions;

/// <summary>
/// Terminal middleware that compiles AST nodes to LINQ Expressions.
/// Handles both orchestration (recursive child transformation) and translation (single-node LINQ compilation).
/// </summary>
public sealed class LinqExpressionMiddleware : ITransformationMiddleware<Expression>
{
    private readonly Dictionary<string, ParameterExpression> _parameterExpressions = new();
    public Expression Transform(InterpretationContext<Expression> context, Node node, TransformationDelegate<Expression> next)
    {

        // Transform child nodes through the pipeline first, then compile to LINQ Expression
        return node switch {
            // Leaf nodes - no recursion needed
            Constant constant => Expression.Constant(constant.Value),
            Variable variable => variable.Value is null ? Expression.Constant(null) : context.Transform(variable.Value),
            Parameter parameter => CompileParameter(context, parameter),

            // Binary arithmetic operations - transform children first
            Add add => Expression.Add(context.Transform(add.LeftHandValue), context.Transform(add.RightHandValue)),
            Subtract sub => Expression.Subtract(context.Transform(sub.LeftHandValue), context.Transform(sub.RightHandValue)),
            Multiply mul => Expression.Multiply(context.Transform(mul.LeftHandValue), context.Transform(mul.RightHandValue)),
            Divide div => Expression.Divide(context.Transform(div.LeftHandValue), context.Transform(div.RightHandValue)),
            Modulo mod => Expression.Modulo(context.Transform(mod.LeftHandValue), context.Transform(mod.RightHandValue)),
            // Unary operations
            UnaryMinus minus => Expression.Negate(context.Transform(minus.Operand)),
            Not not => Expression.Not(context.Transform(not.Value)),

            // Comparison operations
            Equal eq => Expression.Equal(context.Transform(eq.LeftHandValue), context.Transform(eq.RightHandValue)),
            NotEqual neq => Expression.NotEqual(context.Transform(neq.LeftHandValue), context.Transform(neq.RightHandValue)),
            LessThan lt => Expression.LessThan(context.Transform(lt.LeftHandValue), context.Transform(lt.RightHandValue)),
            LessThanOrEqual lte => Expression.LessThanOrEqual(context.Transform(lte.LeftHandValue), context.Transform(lte.RightHandValue)),
            GreaterThan gt => Expression.GreaterThan(context.Transform(gt.LeftHandValue), context.Transform(gt.RightHandValue)),
            GreaterThanOrEqual gte => Expression.GreaterThanOrEqual(context.Transform(gte.LeftHandValue), context.Transform(gte.RightHandValue)),

            // Boolean operations
            And and => Expression.AndAlso(context.Transform(and.LeftHandValue), context.Transform(and.RightHandValue)),
            Or or => Expression.OrElse(context.Transform(or.LeftHandValue), context.Transform(or.RightHandValue)),
            // Conditional
            Conditional cond => Expression.Condition(
                context.Transform(cond.Condition),
                context.Transform(cond.IfTrue),
                context.Transform(cond.IfFalse)),

            // Member and index access
            MemberAccess member => Expression.PropertyOrField(context.Transform(member.Value), member.MemberName),
            IndexAccess index => CompileIndexAccess(context, index),

            // Method invocation
            MethodInvocation method => Expression.Call(
                method.Target != null ? context.Transform(method.Target) : null!,
                method.MethodName,
                Type.EmptyTypes,
                method.Arguments.Select(arg => context.Transform(arg)).ToArray()),

            // Type reference
            TypeReference => Expression.Constant(null),

            // Type cast
            TypeCast cast => CompileTypeCast(context, cast),

            // Coalesce
            Coalesce coalesce => Expression.Coalesce(
                context.Transform(coalesce.LeftHandValue),
                context.Transform(coalesce.RightHandValue)),

            // Block
            Block block => Expression.Block(
                block.Variables.Select(v => (ParameterExpression)context.Transform(v)).ToArray(),
                block.Nodes.Select(n => context.Transform(n)).ToArray()),

            // Assignment
            Assignment assign => Expression.Assign(
                (ParameterExpression)context.Transform(assign.Destination),
                context.Transform(assign.Value)),

            _ => throw new InvalidOperationException($"Unsupported node type: {node.GetType().Name}")
        };
    }

    private Type GetClrType(ISemanticInfoProvider semantics, Node node)
    {
        if (semantics.GetResolvedType(node) is not ClrTypeDefinition typeDef)
            throw new InvalidOperationException($"Type for node '{node}' was not resolved by semantic analysis.");
            
        return typeDef.Type;
    }

    private ParameterExpression CompileParameter(InterpretationContext<Expression> context, Parameter parameter)
    {
        return context.GetOrAddLinqParameter(parameter, () =>
        {
            var semanticProvider = context.GetSemanticProvider();
            var type = GetClrType(semanticProvider, parameter);
            return Expression.Parameter(type, parameter.Name);
        });
    }

    private Expression CompileIndexAccess(InterpretationContext<Expression> context, IndexAccess indexAccess)
    {
        var target = context.Transform(indexAccess.Value);
        var indices = indexAccess.Arguments.Select(arg => context.Transform(arg)).ToArray();

        if (target.Type.IsArray)
        {
            return Expression.ArrayIndex(target, indices);
        }
        else
        {
            var indexerProperty = target.Type.GetProperties()
                .FirstOrDefault(p => p.GetIndexParameters().Length > 0);

            if (indexerProperty != null)
            {
                return Expression.MakeIndex(target, indexerProperty, indices);
            }

            return Expression.ArrayIndex(target, indices);
        }
    }

    private Expression CompileTypeCast(InterpretationContext<Expression> context, TypeCast typeCast)
    {
        var semanticProvider = context.GetSemanticProvider();
        var operand = context.Transform(typeCast.Operand);
        var type = GetClrType(semanticProvider, typeCast);
        return typeCast.IsChecked
            ? Expression.ConvertChecked(operand, type)
            : Expression.Convert(operand, type);
    }
}
