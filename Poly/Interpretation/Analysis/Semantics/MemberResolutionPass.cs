namespace Poly.Interpretation.Analysis.Semantics;


internal sealed class MemberResolver : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node)
    {
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

    private static ITypeMember? ResolveMemberAccessMember(AnalysisContext context, MemberAccess memberAccess)
    {
        var instanceType = context.GetResolvedType(memberAccess.Value);
        if (instanceType == null)
            return null;

        var member = instanceType.Members.WithName(memberAccess.MemberName).FirstOrDefault();
        return member;
    }

    private static ITypeMember? ResolveMethodInvocationMember(AnalysisContext context, MethodInvocation methodInv)
    {
        var targetType = context.GetResolvedType(methodInv.Target);
        if (targetType == null)
            return null;

        var method = targetType.Methods.WithName(methodInv.MethodName).FirstOrDefault();
        return method;
    }

    private static ITypeMember? ResolveIndexAccessMember(AnalysisContext context, IndexAccess indexAccess)
    {
        var instanceType = context.GetResolvedType(indexAccess.Value);
        if (instanceType == null)
            return null;

        var indexer = instanceType.Properties
            .FirstOrDefault(p => p.Parameters != null && p.Parameters.Any());

        return indexer;
    }
}


public static class MemberResolutionMetadataExtensions {
    internal record MemberResolutionMetadata {
        public Dictionary<Node, ITypeMember> TypeMap { get; } = new();
    };

    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseMemberResolver()
        {
            builder.AddAnalyzer(new MemberResolver());
            return builder;
        }
    }

    extension(AnalysisContext context) {
        public void SetResolvedMember(Node node, ITypeMember member)
        {
            var map = context.GetOrAddMetadata(static () => new MemberResolutionMetadata()).TypeMap;
            map[node] = member;

            context.SetResolvedType(node, member.MemberTypeDefinition);
        }
    }

    extension(ITypedMetadataProvider typedMetadataProvider) {
        public ITypeMember? GetResolvedMember(Node node)
        {
            if (typedMetadataProvider.GetMetadata<MemberResolutionMetadata>() is MemberResolutionMetadata metadata) {
                if (metadata.TypeMap.TryGetValue(node, out var member)) {
                    return member;
                }
            }

            return default;
        }
    }
}