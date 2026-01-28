namespace Poly.Interpretation.Analysis.Semantics;

using Poly.Interpretation.AbstractSyntaxTree.Arithmetic;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;
using Poly.Interpretation.AbstractSyntaxTree.Comparison;
using Poly.Interpretation.AbstractSyntaxTree.Equality;

internal sealed class TypeResolver : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node)
    {
        var resolvedType = node switch {
            // Constants have their type directly available
            Constant c => context.TypeDefinitions.GetTypeDefinition(c.Value?.GetType() ?? typeof(object)),

            // Parameters: resolve from type hint if available
            Parameter p => ResolveParameterType(context, p),

            // Variables: resolve from their assigned value
            Variable v => v.Value is null ? context.TypeDefinitions.GetTypeDefinition(typeof(object)) : ResolveNodeType(context, v.Value),

            // Arithmetic operations - all return the promoted numeric type
            Add add => ResolveArithmeticType(context, add.LeftHandValue, add.RightHandValue),
            Subtract sub => ResolveArithmeticType(context, sub.LeftHandValue, sub.RightHandValue),
            Multiply mul => ResolveArithmeticType(context, mul.LeftHandValue, mul.RightHandValue),
            Divide div => ResolveArithmeticType(context, div.LeftHandValue, div.RightHandValue),
            Modulo mod => ResolveArithmeticType(context, mod.LeftHandValue, mod.RightHandValue),
            UnaryMinus minus => ResolveNodeType(context, minus.Operand),

            // Boolean and comparison operations always return bool
            And => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Or => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Not => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Equal => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            NotEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            LessThan => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            LessThanOrEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            GreaterThan => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            GreaterThanOrEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),

            // Member access - resolve through member lookup
            MemberAccess memberAccess => ResolveMemberAccessType(context, memberAccess),

            // Method invocation - resolve return type
            MethodInvocation methodInv => ResolveMethodInvocationType(context, methodInv),

            // Index access - resolve element type
            IndexAccess indexAccess => ResolveIndexAccessType(context, indexAccess),

            // Type cast: resolve target type from type reference
            TypeReference typeRef => context.TypeDefinitions.GetTypeDefinition(typeRef.TypeName),
            TypeCast cast => ResolveNodeType(context, cast.TargetTypeReference),

            // Conditional returns the type of the ifTrue branch
            Conditional cond => ResolveNodeType(context, cond.IfTrue),

            // Coalesce returns the type of the rightHandValue (non-nullable)
            Coalesce coal => ResolveNodeType(context, coal.RightHandValue),

            // Block returns the type of the last expression
            Block block => ResolveBlockType(context, block),

            // Assignment returns the type of the value being assigned
            Assignment assign => ResolveAssignmentType(context, assign),

            _ => null
        };

        if (resolvedType != null) {
            context.SetResolvedType(node, resolvedType!);
        }

        this.AnalyzeChildren(context, node);
    }

    private static ITypeDefinition? ResolveNodeType(AnalysisContext context, Node node)
    {
        return node switch {
            Constant c => context.TypeDefinitions.GetTypeDefinition(c.Value?.GetType() ?? typeof(object)),
            Parameter p => ResolveParameterType(context, p),
            Variable v => v.Value is null ? context.TypeDefinitions.GetTypeDefinition(typeof(object)) : ResolveNodeType(context, v.Value),
            Add add => ResolveArithmeticType(context, add.LeftHandValue, add.RightHandValue),
            Subtract sub => ResolveArithmeticType(context, sub.LeftHandValue, sub.RightHandValue),
            Multiply mul => ResolveArithmeticType(context, mul.LeftHandValue, mul.RightHandValue),
            Divide div => ResolveArithmeticType(context, div.LeftHandValue, div.RightHandValue),
            Modulo mod => ResolveArithmeticType(context, mod.LeftHandValue, mod.RightHandValue),
            UnaryMinus minus => ResolveNodeType(context, minus.Operand),
            And => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Or => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Not => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            Equal => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            NotEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            LessThan => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            LessThanOrEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            GreaterThan => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            GreaterThanOrEqual => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            MemberAccess memberAccess => ResolveMemberAccessType(context, memberAccess),
            MethodInvocation methodInv => ResolveMethodInvocationType(context, methodInv),
            IndexAccess indexAccess => ResolveIndexAccessType(context, indexAccess),
            TypeReference typeRef => context.TypeDefinitions.GetTypeDefinition(typeRef.TypeName),
            TypeCast cast => ResolveNodeType(context, cast.TargetTypeReference),
            Conditional cond => ResolveNodeType(context, cond.IfTrue),
            Coalesce coal => ResolveNodeType(context, coal.RightHandValue),
            Block block => ResolveBlockType(context, block),
            Assignment assign => ResolveAssignmentType(context, assign),
            _ => null
        };
    }

    private static ITypeDefinition? ResolveArithmeticType(
        AnalysisContext context,
        Node left,
        Node right)
    {
        var leftType = ResolveNodeType(context, left);
        var rightType = ResolveNodeType(context, right);

        if (leftType == null || rightType == null)
            return null;

        return leftType;
    }

    private static ITypeDefinition? ResolveMemberAccessType(
        AnalysisContext context,
        MemberAccess memberAccess)
    {
        var instanceType = ResolveNodeType(context, memberAccess.Value);
        if (instanceType == null)
            return null;

        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        return member?.MemberTypeDefinition;
    }

    private static ITypeDefinition? ResolveMethodInvocationType(
        AnalysisContext context,
        MethodInvocation methodInv)
    {
        var instanceType = ResolveNodeType(context, methodInv.Target);
        if (instanceType == null)
            return null;

        var method = instanceType.Methods.WithName(methodInv.MethodName).FirstOrDefault();
        return method?.MemberTypeDefinition;
    }

    private static ITypeDefinition? ResolveIndexAccessType(
        AnalysisContext context,
        IndexAccess indexAccess)
    {
        var instanceType = ResolveNodeType(context, indexAccess.Value);
        if (instanceType == null)
            return null;

        var indexer = instanceType.Properties
            .FirstOrDefault(p => p.Parameters != null && p.Parameters.Any());

        if (indexer != null)
            return indexer.MemberTypeDefinition;

        if (instanceType.ReflectedType.IsArray) {
            var elementType = instanceType.ReflectedType.GetElementType();
            if (elementType != null) {
                return context.TypeDefinitions.GetTypeDefinition(elementType);
            }
        }

        return null;
    }

    private static ITypeDefinition? ResolveAssignmentType(
        AnalysisContext context,
        Assignment assignment)
    {
        var valueType = ResolveNodeType(context, assignment.Value);

        if (assignment.Destination is Variable variable && valueType != null) {
            context.SetResolvedType(variable, valueType);
        }

        return valueType;
    }

    private static ITypeDefinition? ResolveBlockType(
        AnalysisContext context,
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

    private static ITypeDefinition? ResolveParameterType(AnalysisContext context, Parameter parameter)
    {
        if (parameter.TypeReference is not null) {
            return ResolveNodeType(context, parameter.TypeReference);
        }

        return null;
    }
}

public static class TypeResolutionMetadataExtensions {
    internal record TypeResolutionMetadata {
        public Dictionary<Node, ITypeDefinition> TypeMap { get; } = new();
    };

    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder AddTypeResolutionPass()
        {
            builder.AddPass(new TypeResolutionPass());
            return builder;
        }
    }

    extension(AnalysisContext context) {
        public void SetResolvedType(Node node, ITypeDefinition type)
        {
            var map = context.GetOrAddMetadata(static () => new TypeResolutionMetadata()).TypeMap;
            map[node] = type;
        }
    }

    extension(ITypedMetadataProvider typedMetadataProvider) {
        public ITypeDefinition? GetResolvedType(Node node)
        {
            if (typedMetadataProvider.GetMetadata<TypeResolutionMetadata>() is TypeResolutionMetadata metadata) {
                if (metadata.TypeMap.TryGetValue(node, out var type)) {
                    return type;
                }
            }
            
            return default;
        }
    }
}
