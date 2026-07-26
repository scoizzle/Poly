using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.ControlFlow;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Describes the kind of an exception region.
/// </summary>
public enum ExceptionRegionKind {
    /// <summary>A try block protected by catch/finally handlers.</summary>
    Try,

    /// <summary>A catch clause that handles a specific exception type.</summary>
    Catch,

    /// <summary>A finally block that always executes.</summary>
    Finally,

    /// <summary>A dispose region generated from a using statement.</summary>
    UsingDispose,
}

/// <summary>
/// A single entry in the exception region table, describing one
/// protected range and its associated handler.
/// </summary>
/// <param name="Kind">The kind of region entry.</param>
/// <param name="AnchorNodeId">The node that anchors this region entry (e.g. the TryCatchFinally or UsingStatement).</param>
/// <param name="CatchTypeId">Optional resolved type ID for catch clauses (null for finally, using, or general catch). Interim until ANA-002 provides stable IDs. Derived from <c>CatchTypeName.GetHashCode(StringComparison.Ordinal)</c>.</param>
/// <param name="CatchTypeName">Optional stable type name for catch clauses (null for finally, using, or general catch). Interim identifier until ANA-002 lands.</param>
/// <param name="CatchVariableName">Optional variable name binding the caught exception.</param>
/// <param name="ProtectedNodeIds">Node IDs of the protected subtree (the try body or using resource body).</param>
/// <param name="HandlerNodeIds">Node IDs of the handler subtree (catch body or finally body).</param>
public sealed record ExceptionRegionEntry(
    ExceptionRegionKind Kind,
    NodeId AnchorNodeId,
    int? CatchTypeId,
    string? CatchTypeName,
    string? CatchVariableName,
    IReadOnlyList<NodeId> ProtectedNodeIds,
    IReadOnlyList<NodeId> HandlerNodeIds
);

/// <summary>
/// Metadata containing the complete exception region table for an analyzed tree.
/// Stored on the root node (null key) by <see cref="ExceptionRegionAnalyzer"/>.
/// </summary>
public sealed record ExceptionRegionMetadata(
    IReadOnlyList<ExceptionRegionEntry> Regions
) : IAnalysisMetadata;

/// <summary>
/// Optional metadata stamped on <see cref="ThrowStatement"/> nodes that
/// reside inside protected (try) regions, aiding lowering passes in
/// deciding whether to emit a primitive throw or a direct CLR throw.
/// </summary>
/// <param name="IsInProtectedRegion">True when this node is inside a try block,
/// catch clause, finally block, or using statement dispose region.</param>
public sealed record InProtectedRegionMetadata(
    bool IsInProtectedRegion
) : IAnalysisMetadata;

/// <summary>
/// Per-traversal mutable accumulator for <see cref="ExceptionRegionAnalyzer"/>.
/// Stored on <see cref="AnalysisContext"/> metadata (null key) so each
/// <c>Analyze()</c> call gets a fresh instance.
/// </summary>
internal sealed class ExceptionRegionState : IAnalysisMetadata {
    public List<ExceptionRegionEntry> Regions { get; } = new();
    public HashSet<NodeId> ProtectedNodeIds { get; } = new();
    public int Depth { get; set; }
}

/// <summary>
/// Analysis pass that identifies exception handling regions
/// (<see cref="TryCatchFinally"/>, <see cref="UsingStatement"/>)
/// and builds an <see cref="ExceptionRegionMetadata"/> table.
///
/// Placement: after <c>LambdaReturnTypeResolution</c>
/// (slot 12 in the pipeline).
/// </summary>
internal sealed class ExceptionRegionAnalyzer : INodeAnalyzer {
    public const string Id = "ExceptionRegion";
    public string PassName => Id;
    public string[] Dependencies => [TypeAndMemberResolver.Id, ControlFlowAnalysisPass.Id];
    public void Analyze(AnalysisContext context, Node node) {
        // Get or create per-traversal state. Reuses existing state from parent
        // node traversal so ProtectedNodeIds and Regions are shared across the
        // entire tree.
        var state = context.GetMetadata<ExceptionRegionState>(null);
        if (state is null) {
            state = new ExceptionRegionState();
            context.SetMetadata(null, state);
        }

        bool isRootEntry = state.Depth == 0;
        state.Depth++;

        if (isRootEntry) {
            state.Regions.Clear();
            state.ProtectedNodeIds.Clear();
            if (context.IsIncrementalAnalysisAvailable()) {
                var prior = context.GetMetadata<ExceptionRegionMetadata>(null);
                if (prior?.Regions is { Count: > 0 }) {
                    state.Regions.AddRange(prior.Regions);
                    foreach (var entry in prior.Regions)
                        foreach (var id in entry.ProtectedNodeIds)
                            state.ProtectedNodeIds.Add(id);
                }
            }
        }

        // Pre-order: process parent EH nodes before children
        switch (node) {
            case TryCatchFinally tcf:
                ProcessTryCatchFinally(context, tcf, state);
                break;
            case UsingStatement us:
                ProcessUsingStatement(context, us, state);
                break;
        }

        // Recurse into children
        this.AnalyzeChildren(context, node);

        // Mark ThrowStatement nodes inside protected regions
        if (node is ThrowStatement throwStmt && state.ProtectedNodeIds.Contains(throwStmt.Id)) {
            context.SetMetadata(throwStmt, new InProtectedRegionMetadata(true));
        }

        state.Depth--;

        // When depth reaches 0, we've returned from the outermost (root) call,
        // and all children have been fully processed. Store the region table.
        // Always store even if empty (consistent API — consumers always find metadata).
        if (state.Depth == 0) {
            context.SetMetadata(null, new ExceptionRegionMetadata(state.Regions.AsReadOnly()));
        }
    }

    private void ProcessTryCatchFinally(AnalysisContext context, TryCatchFinally node, ExceptionRegionState state) {
        var anchorId = node.Id;
        RemoveRegionsForAnchor(state, anchorId);

        // Collect all node IDs in the try block subtree
        var tryNodeIds = CollectSubtreeIds(context, node.TryBlock);

        // Mark them as protected
        foreach (var id in tryNodeIds)
            state.ProtectedNodeIds.Add(id);

        // Try region
        state.Regions.Add(new ExceptionRegionEntry(
            ExceptionRegionKind.Try,
            anchorId,
            CatchTypeId: null,
            CatchTypeName: null,
            CatchVariableName: null,
            tryNodeIds,
            HandlerNodeIds: Array.Empty<NodeId>()
        ));

        // Catch clauses
        if (node.CatchClauses is not null) {
            foreach (var clause in node.CatchClauses) {
                // Use stable catch type name as interim ID until ANA-002 lands
                string? catchTypeName = null;
                if (clause.ExceptionType is not null) {
                    var resolvedType = context.GetResolvedType(clause.ExceptionType);
                    if (resolvedType is not null)
                        catchTypeName = resolvedType.FullName;
                }

                var handlerNodeIds = CollectSubtreeIds(context, clause.Body);

                state.Regions.Add(new ExceptionRegionEntry(
                    ExceptionRegionKind.Catch,
                    anchorId,
                    CatchTypeId: catchTypeName?.GetHashCode(StringComparison.Ordinal),
                    CatchTypeName: catchTypeName,
                    clause.VariableName,
                    tryNodeIds,
                    handlerNodeIds
                ));
            }
        }

        // Finally block (if present)
        if (node.FinallyBlock is not null) {
            var finallyNodeIds = CollectSubtreeIds(context, node.FinallyBlock);

            state.Regions.Add(new ExceptionRegionEntry(
                ExceptionRegionKind.Finally,
                anchorId,
                CatchTypeId: null,
                CatchTypeName: null,
                CatchVariableName: null,
                tryNodeIds,
                finallyNodeIds
            ));
        }
    }

    private void ProcessUsingStatement(AnalysisContext context, UsingStatement node, ExceptionRegionState state) {
        RemoveRegionsForAnchor(state, node.Id);

        // Protected region = resource acquisition + body (both need cleanup on exceptional exit)
        var protectedIds = new List<NodeId>();
        CollectNodeIds(context, node.Resource, protectedIds);
        CollectNodeIds(context, node.Body, protectedIds);

        // Handler = resource node IDs (the dispose call site operates on the resource expression)
        var resourceNodeIds = new List<NodeId>();
        CollectNodeIds(context, node.Resource, resourceNodeIds);

        state.Regions.Add(new ExceptionRegionEntry(
            ExceptionRegionKind.UsingDispose,
            node.Id,
            CatchTypeId: null,
            CatchTypeName: null,
            CatchVariableName: null,
            protectedIds,
            resourceNodeIds
        ));
    }

    private static void RemoveRegionsForAnchor(ExceptionRegionState state, NodeId anchorId) {
        foreach (var entry in state.Regions.Where(r => r.AnchorNodeId == anchorId).ToList()) {
            foreach (var id in entry.ProtectedNodeIds)
                state.ProtectedNodeIds.Remove(id);
            state.Regions.Remove(entry);
        }
    }

    /// <summary>
    /// Collects all NodeIds from a subtree rooted at <paramref name="node"/>.
    /// </summary>
    private static List<NodeId> CollectSubtreeIds(AnalysisContext context, Node node) {
        var ids = new List<NodeId>();
        CollectNodeIds(context, node, ids);
        return ids;
    }

    private static void CollectNodeIds(AnalysisContext context, Node node, List<NodeId> ids) {
        ids.Add(node.Id);
        foreach (var child in node.Children) {
            if (child is not null)
                CollectNodeIds(context, child, ids);
        }
    }
}

public static class ExceptionRegionAnalysisExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>
        /// Adds the <see cref="ExceptionRegionAnalyzer"/> to the pipeline.
        /// This pass identifies exception handling constructs
        /// (<see cref="TryCatchFinally"/>, <see cref="UsingStatement"/>)
        /// and produces <see cref="ExceptionRegionMetadata"/> for use
        /// by the expansion pass and VM.
        /// </summary>
        public AnalyzerBuilder UseExceptionRegionAnalysis() {
            builder.AddAnalyzer(new ExceptionRegionAnalyzer());
            return builder;
        }
    }

    extension(INodeMetadataProvider provider) {
        /// <summary>
        /// Gets the exception region table for the analyzed tree, if available.
        /// </summary>
        public IReadOnlyList<ExceptionRegionEntry>? GetExceptionRegions() {
            return provider.GetMetadata<ExceptionRegionMetadata>(null)?.Regions;
        }

        /// <summary>
        /// Gets whether a <see cref="ThrowStatement"/> is inside a protected region.
        /// </summary>
        public bool IsInProtectedRegion(ThrowStatement throwStmt) {
            return provider.GetMetadata<InProtectedRegionMetadata>(throwStmt)?.IsInProtectedRegion ?? false;
        }
    }
}