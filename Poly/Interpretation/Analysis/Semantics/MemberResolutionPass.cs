namespace Poly.Interpretation.Analysis.Semantics;


internal sealed class MemberResolutionPass : IAnalysisPass {
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
    internal record MemberResolutionMetadata(Dictionary<Node, ITypeMember> MemberMap);
    
    extension(ITypedMetadataProvider result) {
        private Dictionary<Node, ITypeMember> GetMemberResolutionMap()
        {
            var map = result.GetMetadata<MemberResolutionMetadata>()?.MemberMap;
            if (map is null) {
                throw new InvalidOperationException("Member resolution metadata is not available in this analysis result.");
            }
            return map;
        }

        public ITypeMember? GetResolvedMember(Node node)
        {
            var map = result.GetMemberResolutionMap();
            return map.TryGetValue(node, out var member) ? member : null;
        }

        public void SetResolvedMember(Node node, ITypeMember member)
        {
            var map = result.GetMemberResolutionMap();
            map[node] = member;
        }
    }
}