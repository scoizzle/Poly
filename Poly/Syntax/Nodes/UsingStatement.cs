using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a using statement that manages resource disposal.
/// </summary>
/// <remarks>
/// The resource is acquired and the body is executed. Regardless of how the body completes,
/// the resource is released (via cleanup operations specific to the implementation language).
/// This pattern ensures deterministic resource management.
/// </remarks>
public sealed record UsingStatement(Node Resource, Node Body) : Statement {
    public override IEnumerable<Node?> Children => [Resource, Body];

    /// <inheritdoc />
    public override string ToString() => $"using ({Resource}) {{ {Body} }}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Read ExceptionRegionMetadata for UsingDispose entries.
        var regions = context.Analysis.GetExceptionRegions();
        var usingRegion = regions?.FirstOrDefault(r => r.AnchorNodeId == Id && r.Kind == ExceptionRegionKind.UsingDispose);

        if (usingRegion is not null) {
            // Find the index of this region in the table
            int regionIdx = -1;
            for (int i = 0; i < regions!.Count; i++) {
                if (regions[i].AnchorNodeId == usingRegion.AnchorNodeId
                    && regions[i].Kind == usingRegion.Kind
                    && regions[i].CatchVariableName == usingRegion.CatchVariableName) {
                    regionIdx = i;
                    break;
                }
            }

            // Emit resource, body, then dispose placeholder (dispose after body signals cleanup for INT-018)
            foreach (var p in Resource.ToPrimitives(context)) yield return p;
            foreach (var p in Body.ToPrimitives(context)) yield return p;
            yield return new Primitives.RegionMarker(regionIdx >= 0 ? regionIdx : 0, "LeaveUsingDispose");
        }
        else {
            // No metadata — fall back to: resource, discard, body (backward compatible)
            foreach (var p in Resource.ToPrimitives(context)) yield return p;
            yield return new Primitives.Discard();
            foreach (var p in Body.ToPrimitives(context)) yield return p;
        }
    }
}