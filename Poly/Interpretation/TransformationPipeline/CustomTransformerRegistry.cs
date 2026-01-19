using Poly.Interpretation.AbstractSyntaxTree;

namespace Poly.Interpretation;

/// <summary>
/// Predicate for matching nodes in the custom transformer registry.
/// </summary>
public delegate bool NodeMatcher(Node node, InterpretationContext context);

/// <summary>
/// Registry for custom domain-specific transformers that can handle specific node patterns.
/// </summary>
public interface ICustomTransformerRegistry<TResult>
{
    /// <summary>
    /// Registers a custom transformer for nodes matching the given predicate.
    /// </summary>
    void Register(NodeMatcher matcher, TransformationDelegate<TResult> transformer, int priority = 0);

    /// <summary>
    /// Attempts to resolve a transformer for the given node.
    /// Returns true if a matching transformer is found; otherwise false.
    /// </summary>
    bool TryResolve(Node node, InterpretationContext context, out TransformationDelegate<TResult> transformer);
}

/// <summary>
/// Default implementation of ICustomTransformerRegistry.
/// Supports priority-based ordering; highest priority runs first.
/// </summary>
public sealed class CustomTransformerRegistry<TResult> : ICustomTransformerRegistry<TResult>
{
    private readonly List<(int Priority, NodeMatcher Matcher, TransformationDelegate<TResult> Transformer)> _entries = new();

    /// <summary>
    /// Registers a custom transformer with optional priority.
    /// Higher priority transformers are checked first.
    /// </summary>
    public void Register(NodeMatcher matcher, TransformationDelegate<TResult> transformer, int priority = 0)
    {
        if (matcher == null) throw new ArgumentNullException(nameof(matcher));
        if (transformer == null) throw new ArgumentNullException(nameof(transformer));

        _entries.Add((priority, matcher, transformer));
        _entries.Sort(static (a, b) => b.Priority.CompareTo(a.Priority)); // highest priority first
    }

    /// <summary>
    /// Tries to find and return a matching transformer for the node.
    /// </summary>
    public bool TryResolve(Node node, InterpretationContext context, out TransformationDelegate<TResult> transformer)
    {
        foreach (var (priority, matcher, impl) in _entries)
        {
            if (matcher(node, context))
            {
                transformer = impl;
                return true;
            }
        }

        transformer = default!;
        return false;
    }
}
