using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// IS7h: <see cref="StridedSetBits"/>'s array operand is a promoted receiver, so the strided
/// mark loop reads and writes the cached CLR local instead of re-dereferencing the heap once for
/// the read and once for the write. Before this, `bits[j &gt;&gt; 6] |= mask` emitted two
/// <c>_heap.UnsafeGet</c> calls and two <c>castclass</c> per iteration, because the array reached
/// the intrinsic as an argument and the array cache declined it.
/// </summary>
public class StridedSetBitsCacheTests {
    [Test]
    public async Task PromotedStridedSetBits_HasNoHeapDeref() {
        var text = Dump(Sieve());
        var stride = StrideLoop(text);

        // Count call nodes, not text: the renderer prints a node on its parent's line too.
        var derefs = stride.Split("Call : Object  _heap.UnsafeGet").Length - 1;
        await Assert.That(derefs).IsEqualTo(0)
            .Because($"the strided store must reach the promoted array local; found {derefs} derefs:\n{stride}");

        await Assert.That(text).Contains("_arr0")
            .Because("the buffer's definition binds a typed array local");
    }

    [Test]
    public async Task PromotedStridedSetBits_StillSetsTheBits() {
        var a = new Variable("a");
        var body = new Block([
            new Assignment(a, new NewArray(TypeReference.To<long>(), new Constant(4L))),
            new StridedSetBits(a, new Constant(2L), new Constant(3L), new Constant(20L)),
            new IndexAccess(a, new Constant(0L))
        ], [a]);

        // Bits 2, 5, 8, 11, 14, 17 and 20 are set: 4+32+256+2048+16384+131072+1048576 = 1,198,372.
        using var exec = Interpreter.Execute(Interpreter.Compile(body, CompilationMode.NoDebug));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(1_198_372L)
            .Because("the promoted store must set exactly the bits the heap path set");
    }

    [Test]
    public async Task EscapingArray_KeepsTheHeapPath() {
        // The buffer is the block's value, so the predicate cannot prove it dead: it keeps the
        // heap path and the strided store must still work through the deref.
        var a = new Variable("a");
        var body = new Block([
            new Assignment(a, new NewArray(TypeReference.To<long>(), new Constant(4L))),
            new StridedSetBits(a, new Constant(2L), new Constant(1L), new Constant(5L)),
            new IndexAccess(a, new Constant(0L))
        ], [a]);

        using var exec = Interpreter.Execute(Interpreter.Compile(body, CompilationMode.NoDebug));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0x3CL)
            .Because("bits 2..5 are set, so word 0 is 0b111100");
    }

    static Node Sieve(int limit = 1_000_000) {
        var bits = new Variable("bits");
        var i = new Variable("i");
        var cnt = new Variable("cnt");
        var w = new Variable("w");
        int wordCnt = (int)((limit + 64) / 64);

        Node IsPrime(Node x) => new Equal(
            new BitwiseAnd(new ShiftRight(new IndexAccess(bits, new ShiftRight(x, new Constant(6))),
                new BitwiseAnd(x, new Constant(63L))), new Constant(1L)),
            new Constant(0L));

        return new Block(
            [new Assignment(bits, new NewArray(TypeReference.To<long>(), new Constant((long)wordCnt))),
             new Assignment(i, new Constant(2L)),
             new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant((long)limit)),
                 new Block([
                     new IfStatement(IsPrime(i), new Block([
                         new StridedSetBits(bits, new Multiply(i, i), i, new Constant((long)limit))])),
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
                     new Constant(((limit % 64) == 63) ? -1L : (1L << (int)((limit & 63) + 1)) - 1L))))),
             new Assignment(cnt, new Subtract(cnt, new Constant(2L))),
             cnt],
            [bits, i, cnt, w]);
    }

    static string Dump(Node program) {
        var built = AbiEmitter.Build(program, Interpreter.Analyze(program), CompilationMode.NoDebug);
        return ExpressionText.Render(built.Expression.Body);
    }

    static string StrideLoop(string text) {
        var start = text.IndexOf("_stride_loop", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("the strided loop was not emitted");
        var end = text.IndexOf("_stride_done", StringComparison.Ordinal);
        return text[start..end];
    }
}