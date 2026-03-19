namespace Poly.Interpretation.Analysis.Semantics;

internal static class MethodInvocationSemanticResolver {
    public static ITypeMethod? ResolveMethod(AnalysisContext context, MethodInvocation methodInv) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(methodInv);

        var targetType = context.GetResolvedType(methodInv.Target);
        if (targetType == null)
            return null;

        var argumentTypes = new ITypeDefinition[methodInv.Arguments.Length];
        for (var i = 0; i < methodInv.Arguments.Length; i++) {
            var argumentType = context.GetResolvedType(methodInv.Arguments[i]);
            if (argumentType == null)
                return null;

            argumentTypes[i] = argumentType;
        }

        return targetType
            .FindMatchingMethodOverloads(methodInv.MethodName, argumentTypes)
            .FirstOrDefault();
    }
}


internal sealed class MemberResolver : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        var resolvedMember = node switch {
            // Member access - resolve the member being accessed
            MemberAccess memberAccess => ResolveMemberAccessMember(context, memberAccess),

            // Method invocation - resolve the method being called
            MethodInvocation methodInv => ResolveMethodInvocationMember(context, methodInv),

            // Index access - resolve the indexer property
            IndexAccess indexAccess => ResolveIndexAccessMember(context, indexAccess),

            _ => null
        };

        if (resolvedMember != null) {
            context.SetResolvedMember(node, resolvedMember);
        }

        this.AnalyzeChildren(context, node);
    }

    private static ITypeMember? ResolveMemberAccessMember(AnalysisContext context, MemberAccess memberAccess) {
        var instanceType = context.GetResolvedType(memberAccess.Value);
        if (instanceType == null)
            return null;

        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        return member;
    }

    private static ITypeMember? ResolveMethodInvocationMember(AnalysisContext context, MethodInvocation methodInv) {
        return MethodInvocationSemanticResolver.ResolveMethod(context, methodInv);
    }

    private static ITypeMember? ResolveIndexAccessMember(AnalysisContext context, IndexAccess indexAccess) {
        var instanceType = context.GetResolvedType(indexAccess.Value);
        if (instanceType == null)
            return null;

        var indexer = instanceType.Properties
            .FirstOrDefault(p => p.Parameters != null && p.Parameters.Any());

        return indexer;
    }
}


public static class MemberResolutionMetadataExtensions {
    private sealed class MemberResolutionMetadata : IAnalysisMetadata {
        public ITypeMember? ResolvedMember { get; set; }
    };

    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseMemberResolver() {
            builder.AddAnalyzer(new MemberResolver());
            return builder;
        }
    }

    extension(AnalysisContext context) {
        public void SetResolvedMember(Node node, ITypeMember member) {
            ArgumentNullException.ThrowIfNull(member);

            var metadata = context.GetOrAddMetadata(node, static () => new MemberResolutionMetadata());
            metadata.ResolvedMember = member;

            context.SetResolvedType(node, member.MemberTypeDefinition);
        }
    }

    extension(INodeMetadataProvider typedMetadataProvider) {
        public ITypeMember? GetResolvedMember(Node node) {
            return typedMetadataProvider.GetMetadata<MemberResolutionMetadata>(node)?.ResolvedMember;
        }
    }
}