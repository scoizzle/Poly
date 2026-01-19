using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Interpretation;

/// <summary>
/// Represents a cached transformation result for a specific node.
/// Stores the result keyed by the result type to support multiple transformation types on the same node.
/// </summary>
internal sealed record TransformationResultEntry
{
    public required string ResultTypeName { get; init; }
    public required object ResultValue { get; init; }
}

/// <summary>
/// Pipeline-level cache for transformation results that middlewares can access and populate.
/// Allows middlewares to cache computed TResult values per node to avoid redundant computation.
/// </summary>
/// <remarks>
/// <para>
/// This cache is type-aware: if multiple transformations (e.g., Expression vs ITypeDefinition) 
/// run on the same node, each is cached separately using the result type name as a key.
/// </para>
/// <para>
/// Usage:
/// <code>
/// var transformer = new TransformationResultCache();
/// 
/// // In middleware:
/// if (!cache.TryGetCachedResult(node, out var result))
/// {
///     result = /* compute expensive result */;
///     cache.CacheResult(node, result);
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class TransformationResultCache
{
    private const string TransformationCacheKey = "__TransformationResults__";

    /// <summary>
    /// Gets or creates the transformation result cache from the context.
    /// </summary>
    private static Dictionary<Node, List<TransformationResultEntry>> GetCache(InterpretationContext context)
    {
        if (!context.Properties.TryGetValue(TransformationCacheKey, out var cache))
        {
            cache = new Dictionary<Node, List<TransformationResultEntry>>(
                ReferenceEqualityComparer<Node>.Instance);
            context.Properties[TransformationCacheKey] = cache;
        }
        return (Dictionary<Node, List<TransformationResultEntry>>)cache!;
    }

    /// <summary>
    /// Tries to get a cached transformation result for a node.
    /// </summary>
    /// <typeparam name="TResult">The type of result to retrieve.</typeparam>
    /// <param name="context">The interpretation context.</param>
    /// <param name="node">The node to look up.</param>
    /// <param name="result">The cached result, if found.</param>
    /// <returns>True if a cached result was found; otherwise false.</returns>
    public static bool TryGetCachedResult<TResult>(InterpretationContext context, Node node, out TResult? result)
    {
        result = default;
        var cache = GetCache(context);

        if (!cache.TryGetValue(node, out var entries))
        {
            return false;
        }

        var resultTypeName = typeof(TResult).FullName!;
        var entry = entries.FirstOrDefault(e => e.ResultTypeName == resultTypeName);

        if (entry != null)
        {
            result = (TResult?)entry.ResultValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Caches a transformation result for a node.
    /// If a result for this type already exists for the node, it is replaced.
    /// </summary>
    /// <typeparam name="TResult">The type of result to cache.</typeparam>
    /// <param name="context">The interpretation context.</param>
    /// <param name="node">The node to cache the result for.</param>
    /// <param name="result">The result to cache.</param>
    public static void CacheResult<TResult>(InterpretationContext context, Node node, TResult result)
    {
        var cache = GetCache(context);
        var resultTypeName = typeof(TResult).FullName!;

        if (!cache.TryGetValue(node, out var entries))
        {
            entries = new List<TransformationResultEntry>();
            cache[node] = entries;
        }

        // Remove existing entry for this result type
        var existingIndex = entries.FindIndex(e => e.ResultTypeName == resultTypeName);
        if (existingIndex >= 0)
        {
            entries[existingIndex] = new TransformationResultEntry
            {
                ResultTypeName = resultTypeName,
                ResultValue = result!
            };
        }
        else
        {
            entries.Add(new TransformationResultEntry
            {
                ResultTypeName = resultTypeName,
                ResultValue = result!
            });
        }
    }

    /// <summary>
    /// Checks if a cached result exists for a node and result type.
    /// </summary>
    public static bool HasCachedResult<TResult>(InterpretationContext context, Node node)
    {
        return TryGetCachedResult<TResult>(context, node, out _);
    }

    /// <summary>
    /// Clears all cached results for a specific node (all result types).
    /// </summary>
    public static void ClearNodeCache(InterpretationContext context, Node node)
    {
        var cache = GetCache(context);
        cache.Remove(node);
    }

    /// <summary>
    /// Clears the entire transformation cache.
    /// </summary>
    public static void ClearAll(InterpretationContext context)
    {
        context.Properties.Remove(TransformationCacheKey);
    }
}
