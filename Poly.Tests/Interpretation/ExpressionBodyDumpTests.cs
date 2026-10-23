
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Renders the expression body the emitter produces, indented, so it can be read the way
/// `docs/interpretation/vm-abi-reference.md` describes it. Writes the dump to
/// `/tmp/expr-sieve-strided.txt` and asserts the structural facts this suite relies on, so the
/// artifact in `docs/perf-comparison/sieve-expression-body.txt` cannot rot silently.
/// </summary>
public class ExpressionBodyDumpTests {
    [Test]
    public async Task Sieve_Body_Renders_AndStridedLoopUsesTheCache() {
        var body = SieveStrided();
        var built = AbiEmitter.Build(body, Interpreter.Analyze(body), CompilationMode.NoDebug);
        var text = ExpressionText.Render(built.Expression.Body);
        File.WriteAllText("/tmp/expr-sieve-strided.txt", text);

        await Assert.That(text).Contains("_stride_loop")
            .Because("the strided mark loop is emitted as a labelled loop");

        // `bits[j >> 6] |= mask` lowers to `a[i] = a[i] | mask` over the promoted array local, so
        // the loop neither reads nor writes through the heap. It used to re-deref twice per
        // iteration, because the buffer reached StridedSetBits as an argument and the cache
        // declined it (IS7h gave the intrinsic a promoted receiver).
        var stride = text[text.IndexOf("_stride_loop", StringComparison.Ordinal)..];
        stride = stride[..stride.IndexOf("_stride_done", StringComparison.Ordinal)];
        // Count call nodes, not text: the renderer also prints a node's text on its parent's line.
        var derefs = stride.Split("Call : Object  _heap.UnsafeGet").Length - 1;
        await Assert.That(derefs).IsEqualTo(0)
            .Because($"the strided store must reach the promoted array local; found {derefs} derefs");

        // Slot numbering shifts as the emitter changes, so name the fact, not the index.
        var locals = text[..text.IndexOf('\n')];
        await Assert.That(locals).Contains(":Int32")
            .Because("an int constant is carried in an Int32 ring slot (IS7c)");
    }

    /// <summary>Mirrors docs/perf-comparison/sieve_polyvm.cs.</summary>
    static Node SieveStrided() {
        var bits = new Variable("bits");
        var i = new Variable("i");
        var cnt = new Variable("cnt");
        var w = new Variable("w");
        const long Limit = 1_000_000;
        int wordCnt = (int)((Limit + 64) / 64);

        Node IsPrime(Node x) => new Equal(
            new BitwiseAnd(new ShiftRight(new IndexAccess(bits, new ShiftRight(x, new Constant(6))),
                new BitwiseAnd(x, new Constant(63L))), new Constant(1L)),
            new Constant(0L));

        return new Block(
            [new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant((long)wordCnt))),
             new Assignment(i, new Constant(2L)),
             new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(Limit)),
                 new Block([
                     new IfStatement(IsPrime(i), new Block([
                         new StridedSetBits(bits, new Multiply(i, i), i, new Constant(Limit))])),
                     new Assignment(i, new Add(i, new Constant(1L)))])),
             new Assignment(cnt, new Constant(0L)),
             new Assignment(w, new Constant(0L)),
             new WhileLoop(new LessThan(w, new Constant(wordCnt - 1L)),
                 new Block([
                     new Assignment(cnt, new Add(cnt, new PopCount(
                         new BitwiseNot(new IndexAccess(bits, w))))),
                     new Assignment(w, new Add(w, new Constant(1L)))])),
             new Assignment(cnt, new Add(cnt, new PopCount(
                 new BitwiseAnd(
                     new BitwiseNot(new IndexAccess(bits, new Constant(wordCnt - 1L))),
                     new Constant(((Limit % 64) == 63) ? -1L : (1L << (int)((Limit & 63) + 1)) - 1L))))),
             new Assignment(cnt, new Subtract(cnt, new Constant(2L))),
             cnt],
            [bits, i, cnt, w]);
    }
}