using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax.Primitives;

using Prim = Poly.Syntax.Primitives;
using SN = Poly.Syntax.Nodes;

namespace Poly.Tests.Syntax;

/// <summary>
/// Roundtrip tests: AST → Primitives → AST → VM execution.
/// Verifies the reconstructed AST produces identical results to the original.
///
/// <b>Expression roundtrips</b> — full VM execution roundtrip for pure expression
/// trees that don't contain structural boundaries (CondGoto, Goto, Label).
/// These are handled entirely by the stack-based <see cref="ExpressionReconstructor"/>.
///
/// <b>Structural validation</b> — verify the reconstructor produces a non-null
/// AST from statement-level primitives.  Full execution roundtrip for statements
/// requires the statement pipeline (StatementAssemblyPass) to recognise
/// sequential StoreLocal/LoadLocal patterns — not yet implemented.
///
/// <b>NOT yet tested:</b>
///   - Control-flow expression patterns (Conditional, Coalesce)
///     <c>⇒</c> need the statement pipeline to recognise CondGoto/Goto/Label
///     patterns in expression context
///   - Statement-level roundtrip via VM execution
///     <c>⇒</c> need the statement pipeline to recognise StoreLocal/LoadLocal
///     sequential patterns
/// </summary>
public class PrimitiveReconstructorRoundtripTests {
    /// <summary>
    /// Full expression roundtrip: expand → reconstruct → execute both via VM.
    /// Only suitable for pure expression trees (no structural boundaries).
    /// </summary>
    private static (long Original, long Reconstructed) RunExpressionRoundtrip(Node node) {
        var ctx = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);

        // Execute original via normal VM pipeline
        var originalProg = Interpreter.Compile(node);
        var originalExec = Interpreter.Execute(originalProg, s => {
            s.MaxLoopIterations = 100_000_000;
        });
        var originalResult = originalExec.RawValue;

        // Expand to primitives, reconstruct, execute reconstructed
        var primitives = node.ToPrimitives(ctx).ToList();
        var reconstructor = new PrimitiveReconstructor();
        var reconstructed = reconstructor.Reconstruct(primitives, ctx);

        if (reconstructed is null)
            return (originalResult, long.MinValue);

        var reconProg = Interpreter.Compile(reconstructed);
        var reconExec = Interpreter.Execute(reconProg, s => {
            s.MaxLoopIterations = 100_000_000;
        });
        var reconResult = reconExec.RawValue;

        return (originalResult, reconResult);
    }

    private static async Task AssertExpressionRoundtrip(Node node) {
        var (original, reconstructed) = RunExpressionRoundtrip(node);
        await Assert.That(reconstructed)
            .IsEqualTo(original)
            .Because($"Expression roundtrip failed for {node.GetType().Name}: original={original}, reconstructed={reconstructed}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Expression roundtrips (full VM execution)
    //
    //  These all expand to flat push/binary/unary primitive sequences
    //  with no control-flow markers, so the expression reconstructor
    //  handles them cleanly.
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Constant_ReturnsValue() =>
        await AssertExpressionRoundtrip(new SN.Constant(42));

    [Test]
    public async Task Constant_Negative_ReturnsValue() =>
        await AssertExpressionRoundtrip(new SN.Constant(-7));

    [Test]
    public async Task Add_Simple_ReturnsSum() =>
        await AssertExpressionRoundtrip(new SN.Add(new SN.Constant(5), new SN.Constant(3)));

    [Test]
    public async Task Add_Nested_ReturnsCorrectResult() =>
        await AssertExpressionRoundtrip(new SN.Add(new SN.Add(new SN.Constant(1), new SN.Constant(2)), new SN.Constant(3)));

    [Test]
    public async Task Subtract_ReturnsDifference() =>
        await AssertExpressionRoundtrip(new SN.Subtract(new SN.Constant(10), new SN.Constant(3)));

    [Test]
    public async Task Multiply_ReturnsProduct() =>
        await AssertExpressionRoundtrip(new SN.Multiply(new SN.Constant(7), new SN.Constant(6)));

    [Test]
    public async Task Divide_ReturnsQuotient() =>
        await AssertExpressionRoundtrip(new SN.Divide(new SN.Constant(10), new SN.Constant(3)));

    [Test]
    public async Task Modulo_ReturnsRemainder() =>
        await AssertExpressionRoundtrip(new SN.Modulo(new SN.Constant(10), new SN.Constant(3)));

    [Test]
    public async Task Negate_ReturnsNegated() =>
        await AssertExpressionRoundtrip(new SN.UnaryMinus(new SN.Constant(42)));

    [Test]
    public async Task BitwiseNot_ReturnsComplement() =>
        await AssertExpressionRoundtrip(new SN.BitwiseNot(new SN.Constant(42)));

    [Test]
    public async Task PopCount_ReturnsBitCount() =>
        await AssertExpressionRoundtrip(new SN.PopCount(new SN.Constant(11L)));

    [Test]
    public async Task Equal_ReturnsOneWhenEqual() =>
        await AssertExpressionRoundtrip(new SN.Equal(new SN.Constant(5), new SN.Constant(5)));

    [Test]
    public async Task Equal_ReturnsZeroWhenNotEqual() =>
        await AssertExpressionRoundtrip(new SN.Equal(new SN.Constant(5), new SN.Constant(3)));

    [Test]
    public async Task GreaterThan_ReturnsOneWhenGreater() =>
        await AssertExpressionRoundtrip(new SN.GreaterThan(new SN.Constant(10), new SN.Constant(3)));

    [Test]
    public async Task LessThan_ReturnsOneWhenLess() =>
        await AssertExpressionRoundtrip(new SN.LessThan(new SN.Constant(3), new SN.Constant(10)));

    [Test]
    public async Task LessThanOrEqual_ReturnsOne() =>
        await AssertExpressionRoundtrip(new SN.LessThanOrEqual(new SN.Constant(5), new SN.Constant(5)));

    [Test]
    public async Task GreaterThanOrEqual_ReturnsOne() =>
        await AssertExpressionRoundtrip(new SN.GreaterThanOrEqual(new SN.Constant(5), new SN.Constant(5)));

    [Test]
    public async Task NotEqual_ReturnsOne() =>
        await AssertExpressionRoundtrip(new SN.NotEqual(new SN.Constant(5), new SN.Constant(3)));

    [Test]
    public async Task And_ReturnsLogicalAnd() =>
        await AssertExpressionRoundtrip(new SN.And(new SN.Constant(1), new SN.Constant(0)));

    [Test]
    public async Task Or_ReturnsLogicalOr() =>
        await AssertExpressionRoundtrip(new SN.Or(new SN.Constant(0), new SN.Constant(1)));

    [Test]
    public async Task BitwiseAnd_ReturnsAnd() =>
        await AssertExpressionRoundtrip(new SN.BitwiseAnd(new SN.Constant(6), new SN.Constant(3)));

    [Test]
    public async Task BitwiseOr_ReturnsOr() =>
        await AssertExpressionRoundtrip(new SN.BitwiseOr(new SN.Constant(6), new SN.Constant(3)));

    [Test]
    public async Task BitwiseXor_ReturnsXor() =>
        await AssertExpressionRoundtrip(new SN.BitwiseXor(new SN.Constant(6), new SN.Constant(3)));

    [Test]
    public async Task ShiftLeft_ReturnsShifted() =>
        await AssertExpressionRoundtrip(new SN.ShiftLeft(new SN.Constant(1), new SN.Constant(3)));

    [Test]
    public async Task ShiftRight_ReturnsShifted() =>
        await AssertExpressionRoundtrip(new SN.ShiftRight(new SN.Constant(8), new SN.Constant(2)));

    [Test]
    public async Task Default_ReturnsZero() =>
        await AssertExpressionRoundtrip(new SN.Default());

    [Test]
    public async Task NullForgiving_Passthrough() =>
        await AssertExpressionRoundtrip(new SN.NullForgiving(new SN.Constant(42)));

    [Test]
    public async Task ThisReference_ReturnsZero() =>
        await AssertExpressionRoundtrip(new SN.ThisReference());

    [Test]
    public async Task Not_ReturnsLogicalNot() =>
        await AssertExpressionRoundtrip(new SN.Not(new SN.Constant(0)));

    // ═══════════════════════════════════════════════════════════════
    //  Combinatorial expression patterns (all pure, no CF primitives)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task MixedArithmetic_ComplexExpression_ReturnsResult() {
        var expr = new SN.Add(
            new SN.Divide(
                new SN.Multiply(
                    new SN.Add(new SN.Constant(1), new SN.Constant(2)),
                    new SN.Subtract(new SN.Constant(10), new SN.Constant(3))),
                new SN.Constant(3)),
            new SN.Constant(1));
        await AssertExpressionRoundtrip(expr);
    }

    [Test]
    public async Task ComparisonChain_ReturnsCorrect() {
        var expr = new SN.BitwiseAnd(
            new SN.LessThan(new SN.Constant(5), new SN.Constant(10)),
            new SN.GreaterThan(new SN.Constant(10), new SN.Constant(3)));
        await AssertExpressionRoundtrip(expr);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Pending: expression patterns with control-flow primitives
    //
    //  TODO: Conditional, Coalesce, NestedConditional
    //  These expand to sequences that include CondGoto/Goto/Label
    //  markers.  The expression reconstructor stops at the first
    //  such marker, so they need statement-pipeline support for
    //  the conditional/ternary/coalesce patterns.
    //
    //  Once TryMatchConditional/TryMatchCoalesce in StatementAssemblyPass
    //  handle expression-level patterns (without requiring StoreLocal),
    //  uncomment these tests:
    //
    //  await AssertExpressionRoundtrip(new SN.Conditional(...));
    //  await AssertExpressionRoundtrip(new SN.Coalesce(...));
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    //  Pending: statement-level roundtrips with variables
    //
    //  TODO: Block, WhileLoop, IfStatement, DoWhileLoop
    //  These expand to StoreLocal/LoadLocal/Goto/Label patterns.
    //  The statement pipeline needs to recognise these sequential
    //  patterns and the Variable identity caching needs to work
    //  with the ExpansionEnvironment.
    //
    //  Once the pipeline handles these, uncomment the structural
    //  validation tests below and migrate to full VM roundtrip.
    // ═══════════════════════════════════════════════════════════════
}