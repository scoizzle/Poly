using System.Linq.Expressions;

using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;

namespace Poly.Interpretation.SemanticAnalysis;

/// <summary>
/// Middleware that enriches AST nodes with semantic information (resolved types, members, etc.).
/// </summary>
public sealed class SemanticAnalysisMiddleware<TResult> : ITransformationMiddleware<TResult> {
    public TResult Transform(InterpretationContext<TResult> context, Node node, TransformationDelegate<TResult> next)
    {
        if (context.GetResolvedType(node) is null) {
            var resolvedType = ResolveNodeType(context, node);
            if (resolvedType != null) {
                context.SetResolvedType(node, resolvedType);
            }
        }

        return next(context, node);
    }

    private static ITypeDefinition? ResolveNodeType(InterpretationContext<TResult> context, Node node)
    {
        return node switch {
            // Constants have their type directly available
            Constant c => context.TypeDefinitionProviders.GetTypeDefinition(c.Value?.GetType() ?? typeof(object)),

            // Parameters: resolve from type hint or fail (pre-resolved types are handled by Transform's early return)
            Parameter p => ResolveParameterType(context, p),

            // Variables need to be looked up in the scope
            Variable v => v.Value is null ? context.TypeDefinitionProviders.GetTypeDefinition<object>() : ResolveNodeType(context, v.Value),

            // Arithmetic operations - use numeric type promotion
            Add add => ResolveArithmeticType(context, add.LeftHandValue, add.RightHandValue),
            Subtract sub => ResolveArithmeticType(context, sub.LeftHandValue, sub.RightHandValue),
            Multiply mul => ResolveArithmeticType(context, mul.LeftHandValue, mul.RightHandValue),
            Divide div => ResolveArithmeticType(context, div.LeftHandValue, div.RightHandValue),
            Modulo mod => ResolveArithmeticType(context, mod.LeftHandValue, mod.RightHandValue),
            UnaryMinus minus => ResolveNodeType(context, minus.Operand),

            // Boolean and comparison operations always return bool
            And => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),
            Or => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),
            Not => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),
            Equal => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),
            NotEqual => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),
            LessThan => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),
            LessThanOrEqual => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),
            GreaterThan => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),
            GreaterThanOrEqual => context.TypeDefinitionProviders.GetTypeDefinition<bool>(),

            // Member access - resolve through member lookup
            MemberAccess memberAccess => ResolveMemberAccessType(context, memberAccess),

            // Method invocation - resolve return type
            MethodInvocation methodInv => ResolveMethodInvocationType(context, methodInv),

            // Index access - resolve element type
            IndexAccess indexAccess => ResolveIndexAccessType(context, indexAccess),

            TypeReference typeRef => context.TypeDefinitionProviders.GetTypeDefinition(typeRef.TypeName),
            // Type cast: resolve target type from type name
            TypeCast cast => ResolveNodeType(context, cast.TargetTypeReference),

            // Conditional returns the type of the ifTrue branch
            Conditional cond => ResolveNodeType(context, cond.IfTrue),

            // Coalesce returns the type of the rightHandValue (non-nullable)
            Coalesce coal => ResolveNodeType(context, coal.RightHandValue),

            // Block returns the type of the last expression and seeds variable types from assignments
            Block block => ResolveBlockType(context, block),

            // Assignment returns the type of the value being assigned
            Assignment assign => ResolveAssignmentType(context, assign),

            _ => null
        };
    }

    private static ITypeDefinition? ResolveArithmeticType(
        InterpretationContext<TResult> context,
        Node left,
        Node right)
    {
        var leftType = ResolveNodeType(context, left);
        var rightType = ResolveNodeType(context, right);

        if (leftType == null || rightType == null)
            return null;

        if (context is InterpretationContext<Expression> expressionContext) {
            return NumericTypePromotion.GetPromotedType(expressionContext, leftType, rightType);
        }

        return leftType;
    }

    private static ITypeDefinition? ResolveMemberAccessType(
        InterpretationContext<TResult> context,
        MemberAccess memberAccess)
    {
        var instanceType = ResolveNodeType(context, memberAccess.Value);
        if (instanceType == null)
            return null;

        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        if (member != null) {
            context.SetResolvedMemberAndType(memberAccess, member, member.MemberTypeDefinition);
            return member.MemberTypeDefinition;
        }

        return null;
    }

    private static ITypeDefinition? ResolveMethodInvocationType(
        InterpretationContext<TResult> context,
        MethodInvocation methodInv)
    {
        var instanceType = ResolveNodeType(context, methodInv.Target);
        if (instanceType == null)
            return null;

        // Find method by name
        var methods = instanceType.Methods.WithName(methodInv.MethodName);

        // TODO: Implement overload resolution based on argument types
        var method = methods.FirstOrDefault();
        if (method != null) {
            context.SetResolvedMemberAndType(methodInv, method, method.MemberTypeDefinition);
            return method.MemberTypeDefinition;
        }

        return null;
    }

    private static ITypeDefinition? ResolveIndexAccessType(
        InterpretationContext<TResult> context,
        IndexAccess indexAccess)
    {
        var instanceType = ResolveNodeType(context, indexAccess.Value);
        if (instanceType == null)
            return null;

        // Check for indexer properties (properties with parameters)
        var indexer = instanceType.Properties
            .FirstOrDefault(p => p.Parameters != null && p.Parameters.Any());

        if (indexer != null) {
            context.SetResolvedMemberAndType(indexAccess, indexer, indexer.MemberTypeDefinition);
            return indexer.MemberTypeDefinition;
        }

        // Check for array element type
        if (instanceType.ReflectedType.IsArray) {
            var elementType = instanceType.ReflectedType.GetElementType();
            if (elementType != null) {
                return context.TypeDefinitionProviders.GetTypeDefinition(elementType);
            }
        }

        return null;
    }

    private static ITypeDefinition? ResolveAssignmentType(
        InterpretationContext<TResult> context,
        Assignment assignment)
    {
        var valueType = ResolveNodeType(context, assignment.Value);

        if (assignment.Destination is Variable variable && valueType != null) {
            context.SetResolvedType(variable, valueType);
        }

        return valueType;
    }

    private static ITypeDefinition? ResolveBlockType(
        InterpretationContext<TResult> context,
        Block block)
    {
        foreach (var variable in block.Variables.OfType<Variable>()) {
            var firstAssignment = block.Nodes.OfType<Assignment>().FirstOrDefault(a => ReferenceEquals(a.Destination, variable));

            if (firstAssignment != null) {
                var resolved = ResolveNodeType(context, firstAssignment.Value);
                if (resolved != null) {
                    context.SetResolvedType(variable, resolved);
                }
            }
            else if (variable.Value is not null) {
                var resolved = ResolveNodeType(context, variable.Value);
                if (resolved != null) {
                    context.SetResolvedType(variable, resolved);
                }
            }
        }

        return block.Nodes.Any()
            ? ResolveNodeType(context, block.Nodes.Last())
            : null;
    }

    private static ITypeDefinition? ResolveParameterType(InterpretationContext<TResult> context, Parameter parameter)
    {
        if (parameter.TypeReference is not null) {
            return ResolveNodeType(context, parameter.TypeReference);
        }

        // Otherwise, type must have been provided via AddParameter (pre-resolved in semantic cache)
        return null;
    }
}
