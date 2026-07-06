using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax.Primitives;

using AnalysisRegionEntry = Poly.Interpretation.Analysis.Semantics.ExceptionRegionEntry;
using PrimRegionMarker = Poly.Syntax.Primitives.RegionMarker;

namespace Poly.Interpretation.Vm;

/// <summary>
/// Builds an <see cref="ExceptionRegionTable"/> from linked primitives and
/// analysis metadata, and compiles handler delegates.
///
/// Strategy B (side-table dispatch): scans the flat µop array for
/// <see cref="PrimRegionMarker"/> annotations and maps PC ranges to
/// handler function indices.
/// </summary>
public static class ExceptionTableBuilder {
    /// <summary>
    /// Build an <see cref="ExceptionRegionTable"/> from a linked primitive sequence
    /// and the analysis metadata. Maps <see cref="PrimRegionMarker"/> positions to
    /// PC ranges and cross-references <see cref="ExceptionRegionEntry"/> metadata.
    /// </summary>
    /// <param name="primitives">Linked primitives (after <see cref="PrimitiveLinker.Link"/>).</param>
    /// <param name="metadata">Exception region metadata from analysis (null-keyed).</param>
    /// <returns>The exception region table, or null if no regions are present.</returns>
    public static ExceptionRegionTable? BuildTable(
        IReadOnlyList<PrimitiveNode> primitives,
        ExceptionRegionMetadata? metadata) {

        if (metadata?.Regions is not { Count: > 0 } regions)
            return null;

        // Map analysis region index → ExceptionRegionEntry for fast lookup
        var regionByIndex = new Dictionary<int, AnalysisRegionEntry>();
        for (int i = 0; i < regions.Count; i++) {
            regionByIndex[i] = regions[i];
        }

        // Scan the linked primitives for RegionMarker entries.
        // Emission order (from TryCatchFinally.ToPrimitives):
        //   EnterTry → try body → EnterCatch → catch body → EnterFinally → finally body
        // PCs indicate position in the linked flat array.
        var entries = new List<ExceptionRegionEntry>();
        var markerStack = new List<(int pc, int regionIdx, string kind)>();

        for (int pc = 0; pc < primitives.Count; pc++) {
            if (primitives[pc] is PrimRegionMarker marker) {
                markerStack.Add((pc, marker.RegionIndex, marker.Kind));
            }
        }

        // Group markers by anchor (they appear sequentially per TryCatchFinally node)
        // For each EnterTry marker, the next markers belong to the same region group.
        for (int i = 0; i < markerStack.Count; i++) {
            var (startPc, regionIdx, kind) = markerStack[i];

            if (kind == "EnterTry") {
                // The try body starts after this marker and ends at the next marker.
                // We don't create a table entry for Try itself — only handlers get entries.
                // But we need tryStartPc for the handler entries.
                // Stored implicitly via tryStartPc resolution below.
                continue;
            }

            if (kind == "EnterCatch" || kind == "EnterFinally") {
                // Handler body starts after this marker.
                // Try region ends at this marker (exclusive). Try region starts
                // right after the corresponding EnterTry marker.
                int handlerStartPc = startPc + 1;
                int handlerEndPc = (i + 1 < markerStack.Count) ? markerStack[i + 1].pc : primitives.Count;
                int handlerFuncIndex = -1; // assigned during compilation

                if (!regionByIndex.TryGetValue(regionIdx, out var analysisEntry))
                    continue;

                var handlerKind = kind switch {
                    "EnterCatch" => RegionKind.Catch,
                    "EnterFinally" => RegionKind.Finally,
                    _ => RegionKind.Finally
                };

                // Find the corresponding Try region's EnterTry marker position
                int tryStartPc = 0;
                for (int j = i - 1; j >= 0; j--) {
                    if (markerStack[j].kind == "EnterTry") {
                        tryStartPc = markerStack[j].pc + 1; // start after marker
                        break;
                    }
                }

                entries.Add(new ExceptionRegionEntry(
                    TryStartPc: tryStartPc,
                    TryEndPc: startPc,           // try body ends at handler marker
                    HandlerFuncIndex: handlerFuncIndex,
                    Kind: handlerKind,
                    CatchTypeName: analysisEntry.CatchTypeName,
                    CatchVariableName: analysisEntry.CatchVariableName,
                    ParentRegionIndex: -1
                ));
            }
        }

        return entries.Count > 0 ? new ExceptionRegionTable(entries.AsReadOnly()) : null;
    }

    /// <summary>
    /// Extract handler primitive sub-ranges from the linked primitive array.
    /// Each handler's µops range starts after its marker and ends at the next marker
    /// (or end of the array).
    /// </summary>
    public static List<(int startPc, int endPc, RegionKind kind, int regionIdx, int handlerFuncIndex)>
        ExtractHandlerRanges(IReadOnlyList<PrimitiveNode> primitives) {

        var ranges = new List<(int, int, RegionKind, int, int)>();
        int i = 0;
        while (i < primitives.Count) {
            if (primitives[i] is PrimRegionMarker marker && marker.Kind is "EnterCatch" or "EnterFinally") {
                int startPc = i + 1;
                int endPc = startPc;
                int regionIdx = marker.RegionIndex;
                RegionKind kind = marker.Kind switch {
                    "EnterCatch" => RegionKind.Catch,
                    "EnterFinally" => RegionKind.Finally,
                    _ => RegionKind.Finally
                };

                // Find the next marker (or end of array)
                for (int j = startPc; j < primitives.Count; j++) {
                    if (primitives[j] is PrimRegionMarker) {
                        endPc = j;
                        break;
                    }
                    endPc = j + 1;
                }

                ranges.Add((startPc, endPc, kind, regionIdx, -1));
            }
            i++;
        }
        return ranges;
    }
}