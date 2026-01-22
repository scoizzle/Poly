using System.Collections.Generic;
using System.Linq.Expressions;
using Poly.Interpretation;

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
    public Expression Transform(InterpretationContext<Expression> context, Node node, TransformationDelegate<Expression> next)
    {

        // Transform child nodes through the pipeline first, then compile to LINQ Expression
        return node switch {
            // Leaf nodes - no recursion needed
            Constant constant => Expression.Constant(constant.Value),
            Variable variable => CompileVariable(context, variable),
            Parameter parameter => CompileParameter(context, parameter),

            // Binary arithmetic operations - transform children first
            Add add => CompileBinaryArithmetic(context, add.LeftHandValue, add.RightHandValue, Expression.Add),
            Subtract sub => CompileBinaryArithmetic(context, sub.LeftHandValue, sub.RightHandValue, Expression.Subtract),
            Multiply mul => CompileBinaryArithmetic(context, mul.LeftHandValue, mul.RightHandValue, Expression.Multiply),
            Divide div => CompileBinaryArithmetic(context, div.LeftHandValue, div.RightHandValue, Expression.Divide),
            Modulo mod => CompileBinaryArithmetic(context, mod.LeftHandValue, mod.RightHandValue, Expression.Modulo),
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
            Coalesce coalesce => CompileCoalesce(context, coalesce),

            // Block
            Block block => Expression.Block(
                block.Variables.Select(v => v switch
                    {
                        Variable variable => CompileVariable(context, variable),
                        Parameter parameter => CompileParameter(context, parameter),
                        _ => throw new InvalidOperationException("Block variables must be Variable or Parameter nodes.")
                    })
                    .ToArray(),
                block.Nodes.Select(n => context.Transform(n)).ToArray()),

            // Assignment
            Assignment assign => CompileAssignment(context, assign),

            _ => throw new InvalidOperationException($"Unsupported node type: {node.GetType().Name}")
        };
    }

    private Expression CompileAssignment(InterpretationContext<Expression> context, Assignment assignment)
    {
        Expression destination = assignment.Destination switch {
            Variable variable => CompileVariable(context, variable),
            Parameter parameter => CompileParameter(context, parameter),
            _ => context.Transform(assignment.Destination)
        };

        var valueExpr = context.Transform(assignment.Value);

        if (destination is ParameterExpression param && valueExpr.Type != param.Type) {
            valueExpr = Expression.Convert(valueExpr, param.Type);
        }

        return Expression.Assign(destination, valueExpr);
    }

    private Expression CompileBinaryArithmetic(
        InterpretationContext<Expression> context,
        Node leftNode,
        Node rightNode,
        Func<Expression, Expression, BinaryExpression> factory)
    {
        var leftExpr = context.Transform(leftNode);
        var rightExpr = context.Transform(rightNode);

        // Handle string concatenation explicitly
        if (leftExpr.Type == typeof(string) && rightExpr.Type == typeof(string)) {
            var concat = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })
                ?? throw new InvalidOperationException("string.Concat overload not found.");
            return Expression.Call(concat, leftExpr, rightExpr);
        }

        var semantics = context.GetSemanticProvider();
        if (semantics.GetResolvedType(leftNode) is ClrTypeDefinition leftType &&
            semantics.GetResolvedType(rightNode) is ClrTypeDefinition rightType)
        {
            var (convertedLeft, convertedRight) = NumericTypePromotion.ConvertToPromotedType(
                context,
                leftExpr,
                rightExpr,
                leftType,
                rightType);

            return factory(convertedLeft, convertedRight);
        }

        return factory(leftExpr, rightExpr);
    }

    private Expression CompileCoalesce(InterpretationContext<Expression> context, Coalesce coalesce)
    {
        var leftExpr = context.Transform(coalesce.LeftHandValue);
        var rightExpr = context.Transform(coalesce.RightHandValue);

        var semantics = context.GetSemanticProvider();
        var rightType = (semantics.GetResolvedType(coalesce.RightHandValue) as ClrTypeDefinition)?.Type ?? rightExpr.Type;

        // For value types, ensure the left side is nullable to allow coalesce.
        if (rightType.IsValueType && Nullable.GetUnderlyingType(rightType) is null) {
            var nullableRight = typeof(Nullable<>).MakeGenericType(rightType);
            leftExpr = leftExpr.Type == nullableRight ? leftExpr : Expression.Convert(leftExpr, nullableRight);
            rightExpr = rightExpr.Type == rightType ? rightExpr : Expression.Convert(rightExpr, rightType);
            return Expression.Coalesce(leftExpr, rightExpr);
        }

        // Reference types or nullable value types
        leftExpr = leftExpr.Type == rightType ? leftExpr : Expression.Convert(leftExpr, rightType);
        rightExpr = rightExpr.Type == rightType ? rightExpr : Expression.Convert(rightExpr, rightType);
        return Expression.Coalesce(leftExpr, rightExpr);
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

    private ParameterExpression CompileVariable(InterpretationContext<Expression> context, Variable variable)
    {
        var cache = context.Metadata.GetOrAdd(static () => new Dictionary<Variable, ParameterExpression>(ReferenceEqualityComparer.Instance));
        if (cache.TryGetValue(variable, out var existing)) {
            return existing;
        }

        var semanticProvider = context.GetSemanticProvider();
        var typeDef = semanticProvider.GetResolvedType(variable) as ClrTypeDefinition;
        var clrType = typeDef?.Type ?? typeof(object);
        var parameter = Expression.Variable(clrType, variable.Name);
        cache[variable] = parameter;
        return parameter;
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
