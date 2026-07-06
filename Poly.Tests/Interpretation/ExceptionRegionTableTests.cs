using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Syntax.Primitives;

using PrimRegionMarker = Poly.Syntax.Primitives.RegionMarker;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Tests for <see cref="ExceptionTableBuilder"/> — the PC-to-handler mapping
/// that supports Strategy B (side-table dispatch) for structured EH.
/// Maps to P1C-005.
/// </summary>
public class ExceptionRegionTableTests {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseThisReferenceContext()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseControlFlowAnalysis()
        .UseValueRepresentationAnalysis()
        .UseConstantFolding()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseCallSiteCatalog()
        .UseExceptionRegionAnalysis()
        .UsePrimitiveExpansion()
        .Build();

    /// <summary>
    /// A simple try-catch with no nested regions should produce
    /// exactly one table entry with the correct TryStartPc/TryEndPc.
    /// </summary>
    [Test]
    public async Task BuildTable_TryCatch_ProducesOneEntry() {
        // try { 42 } catch { 0 }
        var tryCatch = new TryCatchFinally(
            new Constant(42),
            [new CatchClause(null, null, new Constant(0))]);

        var analysis = _analyzer.Analyze(tryCatch);
        var meta = analysis.GetMetadata<PrimitiveExpansionMetadata>(tryCatch);
        await Assert.That(meta).IsNotNull();

        var primitives = PrimitiveLinker.Link(meta!.Primitives);
        var exceptionMeta = analysis.GetMetadata<ExceptionRegionMetadata>(null);

        var table = ExceptionTableBuilder.BuildTable(primitives, exceptionMeta);

        await Assert.That(table).IsNotNull();
        await Assert.That(table!.Entries.Count).IsEqualTo(1);

        var entry = table.Entries[0];
        await Assert.That(entry.Kind).IsEqualTo(RegionKind.Catch);
        await Assert.That(entry.TryStartPc).IsLessThan(entry.TryEndPc);
        await Assert.That(entry.HandlerFuncIndex).IsEqualTo(-1); // not yet compiled
    }

    /// <summary>
    /// A try-finally should produce a Finally entry.
    /// </summary>
    [Test]
    public async Task BuildTable_TryFinally_ProducesFinallyEntry() {
        // try { 42 } finally { 0 }
        var tryFinally = new TryCatchFinally(
            new Constant(42),
            null,
            new Constant(0));

        var analysis = _analyzer.Analyze(tryFinally);
        var meta = analysis.GetMetadata<PrimitiveExpansionMetadata>(tryFinally);
        await Assert.That(meta).IsNotNull();

        var primitives = PrimitiveLinker.Link(meta!.Primitives);
        var exceptionMeta = analysis.GetMetadata<ExceptionRegionMetadata>(null);

        var table = ExceptionTableBuilder.BuildTable(primitives, exceptionMeta);

        await Assert.That(table).IsNotNull();
        await Assert.That(table!.Entries.Count).IsEqualTo(1);
        await Assert.That(table.Entries[0].Kind).IsEqualTo(RegionKind.Finally);
    }

    /// <summary>
    /// A try-catch-finally should produce two entries (catch + finally).
    /// </summary>
    [Test]
    public async Task BuildTable_TryCatchFinally_ProducesTwoEntries() {
        // try { 42 } catch { 0 } finally { -1 }
        var tryCatchFinally = new TryCatchFinally(
            new Constant(42),
            [new CatchClause(null, null, new Constant(0))],
            new Constant(-1));

        var analysis = _analyzer.Analyze(tryCatchFinally);
        var meta = analysis.GetMetadata<PrimitiveExpansionMetadata>(tryCatchFinally);
        await Assert.That(meta).IsNotNull();

        var primitives = PrimitiveLinker.Link(meta!.Primitives);
        var exceptionMeta = analysis.GetMetadata<ExceptionRegionMetadata>(null);

        var table = ExceptionTableBuilder.BuildTable(primitives, exceptionMeta);

        await Assert.That(table).IsNotNull();
        await Assert.That(table!.Entries.Count).IsEqualTo(2);

        // Entries should be in emission order: catch first, then finally
        await Assert.That(table.Entries[0].Kind).IsEqualTo(RegionKind.Catch);
        await Assert.That(table.Entries[1].Kind).IsEqualTo(RegionKind.Finally);

        // Both should guard the same try range
        await Assert.That(table.Entries[0].TryStartPc).IsEqualTo(table.Entries[1].TryStartPc);
    }

    /// <summary>
    /// A node with no EH should produce null table.
    /// </summary>
    [Test]
    public async Task BuildTable_NoExceptionHandling_ReturnsNull() {
        var expr = new Constant(42);

        var analysis = _analyzer.Analyze(expr);
        var meta = analysis.GetMetadata<ExceptionRegionMetadata>(null);

        var table = ExceptionTableBuilder.BuildTable([], meta);
        await Assert.That(table).IsNull();
    }

    /// <summary>
    /// Table entries reference TryStartPc that points to somewhere
    /// in the primitive array (valid PC after linking).
    /// </summary>
    [Test]
    public async Task BuildTable_TryStartPc_IsValidPrimitiveIndex() {
        var tryCatch = new TryCatchFinally(
            new Constant(42),
            [new CatchClause(null, null, new Constant(0))]);

        var analysis = _analyzer.Analyze(tryCatch);
        var meta = analysis.GetMetadata<PrimitiveExpansionMetadata>(tryCatch);
        await Assert.That(meta).IsNotNull();

        var primitives = PrimitiveLinker.Link(meta!.Primitives);
        var exceptionMeta = analysis.GetMetadata<ExceptionRegionMetadata>(null);

        var table = ExceptionTableBuilder.BuildTable(primitives, exceptionMeta);
        await Assert.That(table).IsNotNull();

        foreach (var entry in table!.Entries) {
            // TryStartPc must be a valid index in the primitive array
            await Assert.That(entry.TryStartPc).IsGreaterThanOrEqualTo(0);
            await Assert.That(entry.TryStartPc).IsLessThan(primitives.Count);
            // TryEndPc must be a valid index (exclusive end)
            await Assert.That(entry.TryEndPc).IsLessThanOrEqualTo(primitives.Count);
            // Range must be non-empty
            await Assert.That(entry.TryEndPc).IsGreaterThan(entry.TryStartPc);
        }
    }
}