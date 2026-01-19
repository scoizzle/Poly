using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Interpretation.SemanticAnalysis;

/// <summary>
/// Middleware that enriches AST nodes with semantic information (resolved types, members, etc.).
/// </summary>
public sealed class SemanticAnalysisMiddleware<TResult> : ITransformationMiddleware<TResult>
{
    public TResult Transform(InterpretationContext context, Node node, TransformationDelegate<TResult> next)
    {
        // Skip if already analyzed
        if (context.HasSemanticInfo(node))
        {
            return next(context, node);
        }

        // Resolve and cache type information via provider (nodes do not resolve themselves)
        var resolvedType = context.GetResolvedType(node) ?? ResolveNodeType(context, node);
        if (resolvedType != null)
        {
            context.SetResolvedType(node, resolvedType);
        }

        // Handle specific node types
        switch (node)
        {
            case MemberAccess memberAccess:
                AnalyzeMemberAccess(context, memberAccess);
                break;
        }

        // Pass enriched node to next middleware
        return next(context, node);
    }

    private void AnalyzeMemberAccess(InterpretationContext context, MemberAccess memberAccess)
    {
        var instanceType = context.GetResolvedType(memberAccess.Value)
            ?? ResolveNodeType(context, memberAccess.Value);

        if (instanceType != null)
        {
            // TODO: Evaluate whether to handle more than just first matching member
            var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
            if (member != null)
            {
                context.SetResolvedMember(memberAccess, member);
                context.SetResolvedType(memberAccess, member.MemberTypeDefinition);
            }
        }
    }

    private static ITypeDefinition? ResolveNodeType(InterpretationContext context, Node node)
    {
        return node switch
        {
            // Constants have their type directly available
            Constant<int> => context.GetTypeDefinition<int>(),
            Constant<long> => context.GetTypeDefinition<long>(),
            Constant<uint> => context.GetTypeDefinition<uint>(),
            Constant<ulong> => context.GetTypeDefinition<ulong>(),
            Constant<short> => context.GetTypeDefinition<short>(),
            Constant<ushort> => context.GetTypeDefinition<ushort>(),
            Constant<byte> => context.GetTypeDefinition<byte>(),
            Constant<sbyte> => context.GetTypeDefinition<sbyte>(),
            Constant<float> => context.GetTypeDefinition<float>(),
            Constant<double> => context.GetTypeDefinition<double>(),
            Constant<decimal> => context.GetTypeDefinition<decimal>(),
            Constant<bool> => context.GetTypeDefinition<bool>(),
            Constant<string> => context.GetTypeDefinition<string>(),
            Constant<char> => context.GetTypeDefinition<char>(),
            
            // Parameters have their type from the Type property
            Parameter p => p.Type,
            
            // Variables need to be looked up in the scope
            Variable v => context.Variables.TryGetValue(v.Name, out var varType) ? varType : null,
            
            // Arithmetic operations - use numeric type promotion
            Add add => ResolveArithmeticType(context, add.LeftHandValue, add.RightHandValue),
            Subtract sub => ResolveArithmeticType(context, sub.LeftHandValue, sub.RightHandValue),
            Multiply mul => ResolveArithmeticType(context, mul.LeftHandValue, mul.RightHandValue),
            Divide div => ResolveArithmeticType(context, div.LeftHandValue, div.RightHandValue),
            Modulo mod => ResolveArithmeticType(context, mod.LeftHandValue, mod.RightHandValue),
            UnaryMinus minus => ResolveNodeType(context, minus.Operand),
            
            // Boolean and comparison operations always return bool
            And => context.GetTypeDefinition<bool>(),
            Or => context.GetTypeDefinition<bool>(),
            Not => context.GetTypeDefinition<bool>(),
            Equal => context.GetTypeDefinition<bool>(),
            NotEqual => context.GetTypeDefinition<bool>(),
            LessThan => context.GetTypeDefinition<bool>(),
            LessThanOrEqual => context.GetTypeDefinition<bool>(),
            GreaterThan => context.GetTypeDefinition<bool>(),
            GreaterThanOrEqual => context.GetTypeDefinition<bool>(),
            
            // Member access - resolve through member lookup
            MemberAccess memberAccess => ResolveMemberAccessType(context, memberAccess),
            
            // Method invocation - resolve return type
            MethodInvocation methodInv => ResolveMethodInvocationType(context, methodInv),
            
            // Index access - resolve element type
            IndexAccess indexAccess => ResolveIndexAccessType(context, indexAccess),
            
            // Type cast returns the target type
            TypeCast cast => cast.TargetType,
            
            // Conditional returns the type of the ifTrue branch
            Conditional cond => ResolveNodeType(context, cond.IfTrue),
            
            // Coalesce returns the type of the rightHandValue (non-nullable)
            Coalesce coal => ResolveNodeType(context, coal.RightHandValue),
            
            // Block returns the type of the last expression
            Block block => block.Nodes.Any() 
                ? ResolveNodeType(context, block.Nodes.Last())
                : null,
            
            // Assignment returns the type of the value being assigned
            Assignment assign => ResolveNodeType(context, assign.Value),
            
            // CLR-specific helper types
            ClrMethodInvocationInterpretation clrMethodInv => clrMethodInv.Method.MemberTypeDefinition,
            ClrTypeFieldInterpretationAccessor clrFieldAccess => clrFieldAccess.Field.MemberTypeDefinition,
            ClrTypePropertyInterpretationAccessor clrPropAccess => clrPropAccess.Property.MemberTypeDefinition,
            
            _ => null
        };
    }
    
    private static ITypeDefinition? ResolveArithmeticType(
        InterpretationContext context,
        Node left,
        Node right)
    {
        var leftType = ResolveNodeType(context, left);
        var rightType = ResolveNodeType(context, right);
        
        if (leftType == null || rightType == null)
            return null;
            
        return NumericTypePromotion.GetPromotedType(context, leftType, rightType);
    }
    
    private static ITypeDefinition? ResolveMemberAccessType(
        InterpretationContext context,
        MemberAccess memberAccess)
    {
        var instanceType = ResolveNodeType(context, memberAccess.Value);
        if (instanceType == null)
            return null;
            
        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        if (member != null)
        {
            context.SetResolvedMember(memberAccess, member);
            return member.MemberTypeDefinition;
        }
        
        return null;
    }
    
    private static ITypeDefinition? ResolveMethodInvocationType(
        InterpretationContext context,
        MethodInvocation methodInv)
    {
        var instanceType = ResolveNodeType(context, methodInv.Target);
        if (instanceType == null)
            return null;
            
        // Find method by name
        var methods = instanceType.Methods.WithName(methodInv.MethodName);
        
        // TODO: Implement overload resolution based on argument types
        var method = methods.FirstOrDefault();
        if (method != null)
        {
            context.SetResolvedMember(methodInv, method);
            return method.MemberTypeDefinition;
        }
        
        return null;
    }
    
    private static ITypeDefinition? ResolveIndexAccessType(
        InterpretationContext context,
        IndexAccess indexAccess)
    {
        var instanceType = ResolveNodeType(context, indexAccess.Value);
        if (instanceType == null)
            return null;
            
        // Check for indexer properties (properties with parameters)
        var indexer = instanceType.Properties
            .Where(p => p.Parameters != null && p.Parameters.Any())
            .FirstOrDefault();
            
        if (indexer != null)
        {
            context.SetResolvedMember(indexAccess, indexer);
            return indexer.MemberTypeDefinition;
        }
        
        // Check for array element type
        if (instanceType.ReflectedType.IsArray)
        {
            var elementType = instanceType.ReflectedType.GetElementType();
            if (elementType != null)
            {
                return context.TypeProvider.GetTypeDefinition(elementType);
            }
        }
        
        return null;
    }
}
