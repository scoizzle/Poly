using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Introspection;

namespace Poly.Interpretation.SemanticAnalysis;

/// <summary>
/// Extension methods for accessing and storing semantic analysis information in InterpretationContext.
/// </summary>
public static class SemanticAnalysisExtensions {
    private const string SemanticInfoKey = "__SemanticInfo__";

    private static Dictionary<Node, SemanticInfo> GetCache(InterpretationContext context)
    {
        if (!context.Properties.TryGetValue(SemanticInfoKey, out var cache))
        {
            cache = new Dictionary<Node, SemanticInfo>(ReferenceEqualityComparer.Instance);
            context.Properties[SemanticInfoKey] = cache;
        }
        return (Dictionary<Node, SemanticInfo>)cache!;
    }

    public static ITypeDefinition? GetResolvedType(this InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        return cache.TryGetValue(node, out var info) ? info.ResolvedType : null;
    }

    public static void SetResolvedType(this InterpretationContext context, Node node, ITypeDefinition type)
    {
        var cache = GetCache(context);
        var info = cache.TryGetValue(node, out var existing) ? existing : new SemanticInfo(null, null);
        cache[node] = info with { ResolvedType = type };
    }

    public static ITypeMember? GetResolvedMember(this InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        return cache.TryGetValue(node, out var info) ? info.ResolvedMember : null;
    }

    public static void SetResolvedMember(this InterpretationContext context, Node node, ITypeMember member)
    {
        var cache = GetCache(context);
        var info = cache.TryGetValue(node, out var existing) ? existing : new SemanticInfo(null, null);
        cache[node] = info with { ResolvedMember = member };
    }

    public static bool HasSemanticInfo(this InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        return cache.ContainsKey(node);
    }

    public static SemanticInfo GetSemanticInfo(this InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        return cache.TryGetValue(node, out var info) ? info : new SemanticInfo(null, null);
    }
}

/// <summary>
/// Represents semantic analysis information for a node.
/// </summary>
public record SemanticInfo(ITypeDefinition? ResolvedType, ITypeMember? ResolvedMember);