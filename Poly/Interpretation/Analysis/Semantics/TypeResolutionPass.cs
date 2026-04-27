namespace Poly.Interpretation.Analysis.Semantics;

using Poly.Syntax.AbstractSyntaxTree.Arithmetic;
using Poly.Syntax.AbstractSyntaxTree.Boolean;
using Poly.Syntax.AbstractSyntaxTree.Comparison;
using Poly.Syntax.AbstractSyntaxTree.Equality;

internal sealed class TypeResolver : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        var resolvedType = ResolveNodeType(context, node);

        if (resolvedType != null) {
            context.SetResolvedType(node, resolvedType!);
        }

        this.AnalyzeChildren(context, node);
    }

    private static ITypeDefinition? ResolveNodeType(AnalysisContext context, Node node) {
        return node switch {
            Constant c => context.TypeDefinitions.GetTypeDefinition(c.Value?.GetType() ?? typeof(object)),
            ThisReference @this => ResolveThisReferenceType(context, @this),
            Parameter p => ResolveParameterType(context, p),
            Variable v => context.GetResolvedType(v)
                ?? (v.Value is null
                    ? context.TypeDefinitions.GetTypeDefinition(typeof(object))
                    : ResolveNodeType(context, v.Value)),
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
            Invoke methodInv => ResolveMethodInvocationType(context, methodInv),
            New @new => ResolveConstructorInvocationType(context, @new),
            IndexAccess indexAccess => ResolveIndexAccessType(context, indexAccess),
            TypeDefinitionReference typeDefRef => typeDefRef.TypeDefinition,
            TypeReference typeRef => context.TypeDefinitions.GetTypeDefinition(typeRef.TypeName),
            TypeCast cast => ResolveNodeType(context, cast.TargetTypeReference),
            TypeIs => context.TypeDefinitions.GetTypeDefinition(typeof(bool)),
            TypeAs asType => ResolveAsType(context, asType.TargetTypeReference),
            Conditional cond => ResolveNodeType(context, cond.IfTrue),
            Coalesce coal => ResolveNodeType(context, coal.RightHandValue),
            Block block => ResolveBlockType(context, block),
            Assignment assign => ResolveAssignmentType(context, assign),
            ForEachLoop foreachLoop => ResolveForEachLoopType(context, foreachLoop),
            Lambda lambda => ResolveBlockType(context, lambda.Body is Block b ? b : new Block(lambda.Body)),
            _ => null
        };
    }

    private static ITypeDefinition? ResolveAsType(AnalysisContext context, Node targetTypeReference) {
        var targetType = ResolveNodeType(context, targetTypeReference);
        if (targetType is null) {
            return null;
        }

        var runtimeType = targetType.GetRuntimeType();
        if (runtimeType is null || !runtimeType.IsValueType || Nullable.GetUnderlyingType(runtimeType) is not null) {
            return targetType;
        }

        var nullableType = typeof(Nullable<>).MakeGenericType(runtimeType);
        return context.TypeDefinitions.GetTypeDefinition(nullableType) ?? targetType;
    }

    private static ITypeDefinition? ResolveForEachLoopType(
        AnalysisContext context,
        ForEachLoop foreachLoop) {
        var collectionType = ResolveNodeType(context, foreachLoop.Collection);
        var elementType = collectionType?.GetElementType();

        if (elementType != null) {
            context.SetResolvedType(foreachLoop.LoopVariable, elementType);
        }

        return context.TypeDefinitions.GetTypeDefinition(typeof(void));
    }

    private static ITypeDefinition? ResolveThisReferenceType(AnalysisContext context, ThisReference thisReference) =>
        context.GetResolvedType(thisReference);

    private static ITypeDefinition? ResolveArithmeticType(
        AnalysisContext context,
        Node left,
        Node right) {
        var leftType = ResolveNodeType(context, left);
        var rightType = ResolveNodeType(context, right);

        if (leftType == null || rightType == null)
            return null;

        return leftType;
    }

    private static ITypeDefinition? ResolveMemberAccessType(
        AnalysisContext context,
        MemberAccess memberAccess) {
        var instanceType = ResolveNodeType(context, memberAccess.Value);
        if (instanceType == null)
            return null;

        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        return member?.MemberTypeDefinition;
    }

    private static ITypeDefinition? ResolveMethodInvocationType(
        AnalysisContext context,
        Invoke invoke) {
        // Resolve the target type through the method reference so the semantic
        // resolver can look it up by name when FindMatchingMethodOverloads is called.
        if (invoke.Delegate is MemberAccess memberAccess) {
            var targetType = ResolveNodeType(context, memberAccess.Value);
            if (targetType != null) {
                context.SetResolvedType(memberAccess.Value, targetType);
            }
        }
        else
        if (invoke.Delegate is Lambda lambda) {
            return ResolveNodeType(context, lambda.Body);
        }

        foreach (var argument in invoke.Arguments) {
            var argumentType = ResolveNodeType(context, argument);
            if (argumentType != null) {
                context.SetResolvedType(argument, argumentType);
            }
        }

        var method = MethodInvocationSemanticResolver.ResolveMethod(context, invoke);
        return method?.MemberTypeDefinition;
    }

    private static ITypeDefinition? ResolveConstructorInvocationType(
        AnalysisContext context,
        New @new) {
        var targetType = ResolveNodeType(context, @new.Type);
        if (targetType != null) {
            context.SetResolvedType(@new.Type, targetType);
        }

        foreach (var argument in @new.Arguments) {
            var argumentType = ResolveNodeType(context, argument);
            if (argumentType != null) {
                context.SetResolvedType(argument, argumentType);
            }
        }

        var constructor = ConstructorInvocationSemanticResolver.ResolveConstructor(context, @new);
        return constructor?.MemberTypeDefinition ?? targetType;
    }

    private static ITypeDefinition? ResolveIndexAccessType(
        AnalysisContext context,
        IndexAccess indexAccess) {
        var instanceType = ResolveNodeType(context, indexAccess.Value);
        if (instanceType == null)
            return null;

        var argumentTypes = indexAccess.Arguments
            .Select(argument => ResolveNodeType(context, argument))
            .ToArray();

        if (argumentTypes.All(static type => type is not null)) {
            return instanceType.GetElementType(argumentTypes!);
        }

        return instanceType.GetElementType();
    }

    private static ITypeDefinition? ResolveAssignmentType(
        AnalysisContext context,
        Assignment assignment) {
        var valueType = ResolveNodeType(context, assignment.Value);

        if (assignment.Destination is Variable variable && valueType != null) {
            context.SetResolvedType(variable, valueType);
        }

        return valueType;
    }

    private static ITypeDefinition? ResolveBlockType(
        AnalysisContext context,
        Block block) {
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

    private static ITypeDefinition? ResolveParameterType(AnalysisContext context, Parameter parameter) {
        if (parameter.TypeReference is not null) {
            return ResolveNodeType(context, parameter.TypeReference);
        }

        return null;
    }
}

public static class TypeResolutionMetadataExtensions {
    private sealed record TypeResolutionMetadata : IAnalysisMetadata {
        public ITypeDefinition? ResolvedTypeDefinition { get; set; }
    };

    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseTypeResolver() {
            builder.AddAnalyzer(new TypeResolver());
            return builder;
        }
    }

    extension(AnalysisContext context) {
        public void SetResolvedType(Node node, ITypeDefinition type) {
            var metadata = context.GetOrAddMetadata(node, static () => new TypeResolutionMetadata());
            metadata.ResolvedTypeDefinition = type;
        }
    }

    extension(INodeMetadataProvider typedMetadataProvider) {
        public ITypeDefinition? GetResolvedType(Node node) {
            return typedMetadataProvider.GetMetadata<TypeResolutionMetadata>(node)?.ResolvedTypeDefinition;
        }
    }
}