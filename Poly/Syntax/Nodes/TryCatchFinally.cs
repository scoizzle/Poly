using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a try-catch-finally statement that handles exceptions.
/// </summary>
/// <remarks>
/// The try block is executed; if an exception occurs, it is matched against catch clauses.
/// The finally block (if present) is guaranteed to execute regardless of normal or exceptional completion.
/// At least one catch or finally clause must be present.
/// </remarks>
public sealed record TryCatchFinally(Node TryBlock, IReadOnlyList<CatchClause>? CatchClauses = null, Node? FinallyBlock = null) : Node {
    public override IEnumerable<Node?> Children =>
        [TryBlock, .. (CatchClauses ?? new List<CatchClause>()).SelectMany(c => c.Children), FinallyBlock];

    /// <inheritdoc />
    public override string ToString() {
        var catches = CatchClauses != null ? string.Join(" ", CatchClauses.Select(c => c.ToString())) : "";
        var finallyStr = FinallyBlock is not null ? $" finally {{ {FinallyBlock} }}" : "";
        return $"try {{ {TryBlock} }} {catches}{finallyStr}";
    }

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Read ExceptionRegionMetadata to bracket try/catch/finally bodies.
        // Placeholder markers until INT-018 implements real EH µops.
        var regions = context.Analysis.GetExceptionRegions();
        List<(ExceptionRegionEntry entry, int idx)>? ourRegions = null;
        if (regions is not null) {
            ourRegions = new List<(ExceptionRegionEntry, int)>();
            for (int i = 0; i < regions.Count; i++) {
                if (regions[i].AnchorNodeId == Id)
                    ourRegions.Add((regions[i], i));
            }
        }

        if (ourRegions is not null && ourRegions.Count > 0) {
            // Emit Try region marker
            yield return new Primitives.RegionMarker(ourRegions[0].idx, "EnterTry");

            // Expand the try block body
            foreach (var p in TryBlock.ToPrimitives(context)) yield return p;

            // Expand catch clauses
            if (CatchClauses is not null) {
                foreach (var clause in CatchClauses) {
                    var catchRegion = ourRegions.Find(r => r.entry.Kind == ExceptionRegionKind.Catch
                        && r.entry.CatchVariableName == clause.VariableName);
                    if (catchRegion.entry is not null) {
                        yield return new Primitives.RegionMarker(catchRegion.idx, "EnterCatch");
                    }
                    foreach (var p in clause.Body.ToPrimitives(context)) yield return p;
                }
            }

            // Expand finally block
            if (FinallyBlock is not null) {
                var finallyRegion = ourRegions.Find(r => r.entry.Kind == ExceptionRegionKind.Finally);
                if (finallyRegion.entry is not null) {
                    yield return new Primitives.RegionMarker(finallyRegion.idx, "EnterFinally");
                }
                foreach (var p in FinallyBlock.ToPrimitives(context)) yield return p;
            }
        }
        else {
            // No metadata available — fall back to try-body only (backward compatible)
            foreach (var p in TryBlock.ToPrimitives(context)) yield return p;
        }
    }
}

/// <summary>
/// Represents a single catch clause in a try-catch-finally statement.
/// </summary>
/// <remarks>
/// A catch clause specifies the exception type to handle and the body to execute when an exception of that type is raised.
/// The optional variable name binds the caught exception for use within the body.
/// </remarks>
public sealed record CatchClause(Node? ExceptionType, string? VariableName, Node Body) {
    public IEnumerable<Node?> Children => [ExceptionType, Body];

    /// <inheritdoc />
    public override string ToString() {
        var exceptionPart = ExceptionType is not null ? ExceptionType.ToString() : "Exception";
        var varPart = VariableName is not null ? $" {VariableName}" : "";
        return $"catch ({exceptionPart}{varPart}) {{ {Body} }}";
    }
}