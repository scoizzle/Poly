namespace Poly.Interpretation.Analysis.Semantics;

internal sealed class MemberResolutionPass : IAnalysisPass {
    public void Analyze(AnalysisContext context, Node node)
    {
        switch (node) {
            default:
                Debug.WriteLine($"[MemberResolutionPass] Unhandled node type: {node.GetType().Name}");
                this.AnalyzeChildren(context, node);
                break;
        }
    }
}

public static class MemberResolutionMetadataExtensions {
    internal record MemberResolutionMetadata(Dictionary<Node, ITypeMember> MemberMap);

    extension(ITypedMetadataProvider typedMetadataProvider) {
        private Dictionary<Node, ITypeMember> GetMemberResolutionMap() {
            var map = typedMetadataProvider.GetMetadata<MemberResolutionMetadata>()?.MemberMap;
            if (map is null) {
                throw new InvalidOperationException("Member resolution metadata is not available in this analysis result.");
            }
            return map;
        }

        public ITypeMember? GetResolvedMember(Node node)
        {
            var map = typedMetadataProvider.GetMemberResolutionMap();
            return map.TryGetValue(node, out var member) ? member : null;
        }

        public void SetResolvedMember(Node node, ITypeMember member)
        {
            var map = typedMetadataProvider.GetMemberResolutionMap();
            map[node] = member;
        }
    }
}