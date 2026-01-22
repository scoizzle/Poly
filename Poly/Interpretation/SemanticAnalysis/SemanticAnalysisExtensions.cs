namespace Poly.Interpretation.SemanticAnalysis;

/// <summary>
/// Extension methods for accessing and storing semantic analysis information in InterpretationContext.
/// </summary>
public static class SemanticAnalysisExtensions {
    extension<TResult>(InterpreterBuilder<TResult> builder) {
        /// <summary>
        /// Adds semantic analysis middleware to the interpreter builder.
        /// </summary>
        /// <typeparam name="TResult">The type of the expression result.</typeparam>
        /// <param name="builder">The interpreter builder.</param>
        /// <returns>The updated interpreter builder.</returns>
        public InterpreterBuilder<TResult> UseSemanticAnalysis() => builder.Use(new SemanticAnalysisMiddleware<TResult>());
    }

    extension<TResult>(InterpretationContext<TResult> context) {
        private ContextSemanticProvider GetPrivateProvider() => (ContextSemanticProvider)context.GetSemanticProvider();

        /// <summary>
        /// Gets or creates the semantic info provider for this context.
        /// </summary>
        public ISemanticInfoProvider GetSemanticProvider() => context.GetOrAddMetadata<ISemanticInfoProvider>(static () => new ContextSemanticProvider());

        /// <summary>
        /// Gets the resolved type for the given node, if available.
        /// </summary>
        /// <param name="context">The interpretation context.</param>
        /// <param name="node">The node for which to get the resolved type.</param>
        /// <returns>The resolved type if available; otherwise, null.</returns>
        public ITypeDefinition? GetResolvedType(Node node) => context.GetSemanticProvider().GetResolvedType(node);

        /// <summary>
        /// Sets the resolved type for the given node.
        /// </summary>
        /// <param name="context">The interpretation context.</param>
        /// <param name="node">The node for which to set the resolved type.</param>
        /// <param name="type">The resolved type to set.</param>
        public void SetResolvedType(Node node, ITypeDefinition type) => GetPrivateProvider(context).SetResolvedType(node, type);

        /// <summary>
        /// Gets the resolved member for the given node, if available.
        /// </summary>
        /// <param name="context">The interpretation context.</param>
        /// <param name="node">The node for which to get the resolved member.</param>
        /// <returns>The resolved member if available; otherwise, null.</returns>
        public ITypeMember? GetResolvedMember(Node node) => GetPrivateProvider(context).GetResolvedMember(node);

        /// <summary>
        /// Sets the resolved member for the given node.
        /// </summary>
        /// <param name="context">The interpretation context.</param>
        /// <param name="node">The node for which to set the resolved member.</param>
        /// <param name="member">The resolved member to set.</param>
        public void SetResolvedMember(Node node, ITypeMember member) => GetPrivateProvider(context).SetResolvedMember(node, member);

        /// <summary>
        /// Sets both the resolved member and type for the given node.
        /// </summary>
        /// <param name="node">The node for which to set the resolved member and type.</param>
        /// <param name="member">The resolved member to set.</param>
        /// <param name="type">The resolved type to set.</param>
        public void SetResolvedMemberAndType(Node node, ITypeMember member, ITypeDefinition type) => GetPrivateProvider(context).SetResolvedMemberAndType(node, member, type);

        /// <summary>
        /// Determines whether the given node has any semantic analysis information.
        /// </summary>
        /// <param name="context">The interpretation context.</param>
        /// <param name="node">The node to check for semantic information.</param>
        /// <returns>True if semantic information exists for the node; otherwise, false.</returns>
        public bool HasSemanticInfo(Node node) => GetPrivateProvider(context).HasSemanticInfo(node);

        /// <summary>
        /// Gets all semantic analysis information for the given node.
        /// </summary>
        /// <param name="context">The interpretation context.</param>
        /// <param name="node">The node for which to get semantic information.</param>
        /// <returns>The semantic information for the node.</returns>
        public SemanticInfo GetSemanticInfo(Node node) => GetPrivateProvider(context).GetSemanticInfo(node);
    }

    /// <summary>
    /// Private implementation of ISemanticInfoProvider that owns the semantic cache.
    /// </summary>
    private sealed class ContextSemanticProvider : ISemanticInfoProvider {
        private readonly Dictionary<Node, SemanticInfo> _cache = new(ReferenceEqualityComparer.Instance);

        public ITypeDefinition? GetResolvedType(Node node) => _cache.TryGetValue(node, out var info) ? info.ResolvedType : null;

        public ITypeMember? GetResolvedMember(Node node) => _cache.TryGetValue(node, out var info) ? info.ResolvedMember : null;

        public bool HasSemanticInfo(Node node) => _cache.ContainsKey(node);

        internal void SetResolvedType(Node node, ITypeDefinition type)
        {
            var info = _cache.TryGetValue(node, out var existing) ? existing : new SemanticInfo(null, null);
            _cache[node] = info with { ResolvedType = type };
        }

        internal void SetResolvedMember(Node node, ITypeMember member)
        {
            var info = _cache.TryGetValue(node, out var existing) ? existing : new SemanticInfo(null, null);
            _cache[node] = info with { ResolvedMember = member };
        }

        internal void SetResolvedMemberAndType(Node node, ITypeMember member, ITypeDefinition type)
        {
            if (_cache.TryGetValue(node, out var info)) {
                _cache[node] = info with { ResolvedMember = member, ResolvedType = type };
                return;
            }
            
            _cache[node] = new SemanticInfo(type, member);
        }

        internal SemanticInfo GetSemanticInfo(Node node) => _cache.TryGetValue(node, out var info) ? info : new SemanticInfo(null, null);
    }
}

/// <summary>
/// Represents semantic analysis information for a node.
/// </summary>
public record SemanticInfo(ITypeDefinition? ResolvedType, ITypeMember? ResolvedMember);