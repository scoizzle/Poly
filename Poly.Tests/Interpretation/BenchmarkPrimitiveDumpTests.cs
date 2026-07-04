using System.Text;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Syntax.Primitives;

using Prim = Poly.Syntax.Primitives;
using SN = Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Replicates the four Poly VM benchmarks, validates they produce correct
/// results, and dumps a human-readable assembly-style listing of the linked
/// primitive instructions to /tmp/*.polyvm for review.
///
/// Run with: dotnet run --project Poly.Tests -- --filter BenchmarkDump*
/// </summary>
public class BenchmarkPrimitiveDumpTests {
    private const int SieveLimit = 1000000;

    // ── Sieve of Eratosthenes ──────────────────────────────────────

    [Test]
    public async Task BenchmarkDump_Sieve() {
        int wordCnt = (SieveLimit + 64) / 64;
        var bits = new Variable("bits");
        var i = new Variable("i");
        var cnt = new Variable("cnt");
        var w = new Variable("w");

        Node IsPrime(Node x) => new Equal(
            new BitwiseAnd(new ShiftRight(new IndexAccess(bits,
                new ShiftRight(x, new Constant(6))),
                new BitwiseAnd(x, new Constant(63L))), new Constant(1L)),
            new Constant(0L));

        var body = new Block(
            [new Assignment(bits, new SN.NewArray(TypeReference.To<long>(), new Constant(wordCnt))),
             new Assignment(i, new Constant(2)),
             new WhileLoop(new LessThanOrEqual(new Multiply(i, i), new Constant(SieveLimit)),
                 new Block([
                     new IfStatement(IsPrime(i),
                         new Block([new StridedSetBits(bits, new Multiply(i, i), i, new Constant(SieveLimit))])),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             new Assignment(cnt, new Constant(0L)),
             new Assignment(w, new Constant(0L)),
             new WhileLoop(new LessThan(w, new Constant(wordCnt - 1)),
                 new Block([
                     new Assignment(cnt, new Add(cnt, new PopCount(new BitwiseNot(new IndexAccess(bits, w))))),
                     new Assignment(w, new Add(w, new Constant(1L)))
                 ])),
             new Assignment(cnt, new Add(cnt, new PopCount(new BitwiseAnd(
                 new BitwiseNot(new IndexAccess(bits, new Constant(wordCnt - 1))),
                 new Constant((SieveLimit % 64) == 63 ? -1L : (1L << ((SieveLimit & 63) + 1)) - 1L))))),
             new Assignment(cnt, new Subtract(cnt, new Constant(2L))),
             cnt],
            [bits, i, cnt, w]);

        await DumpAndVerify("sieve", body, expectedResult: 78498);
    }

    // ── Collatz (max chain length) ─────────────────────────────────

    [Test]
    public async Task BenchmarkDump_Collatz() {
        const int limit = 100;
        var n = new Variable("n"); var ci = new Variable("i");
        var len = new Variable("len"); var maxLen = new Variable("maxLen");
        var bestN = new Variable("bestN");

        var body = new Invoke(new Lambda([], new Block(
            [new Assignment(maxLen, new Constant(0L)),
             new Assignment(bestN, new Constant(0L)),
             new Assignment(n, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(n, new Constant(limit)),
                 new Block([
                     new Assignment(len, new Constant(0L)),
                     new Assignment(ci, n),
                     new WhileLoop(new NotEqual(ci, new Constant(1L)),
                         new Block([
                             new Assignment(ci, new Conditional(
                                 new Equal(new Modulo(ci, new Constant(2L)), new Constant(0L)),
                                 new ShiftRight(ci, new Constant(1)),
                                 new Add(new Multiply(ci, new Constant(3L)), new Constant(1L)))),
                             new Assignment(len, new Add(len, new Constant(1L)))
                         ])),
                     new IfStatement(
                         new GreaterThan(len, maxLen),
                         new Block([new Assignment(maxLen, len), new Assignment(bestN, n)])),
                     new Assignment(n, new Add(n, new Constant(1L)))
                 ])),
             new BitwiseOr(new ShiftLeft(bestN, new Constant(32L)), maxLen)],
            [n, ci, len, maxLen, bestN])));

        await DumpAndVerify("collatz", body, expectedResult: 97L << 32 | 118);
    }

    // ── Mandelbrot (pixel count) ───────────────────────────────────

    [Test]
    public async Task BenchmarkDump_Mandelbrot() {
        int size = 16;
        const int S = 8;

        var x = new Variable("x"); var y = new Variable("y");
        var zx = new Variable("zx"); var zy = new Variable("zy");
        var zx2 = new Variable("zx2"); var zy2 = new Variable("zy2");
        var iter = new Variable("iter"); var total = new Variable("total");

        Node Cx(Node xv) => new Subtract(new Multiply(xv, new Constant(8L)), new Constant(size * 4L));
        Node Cy(Node yv) => Cx(yv);

        Node mandelPixel = new Block([
            new Assignment(zx, new Constant(0L)), new Assignment(zy, new Constant(0L)),
            new Assignment(iter, new Constant(0L)),
            new WhileLoop(
                new And(new LessThan(iter, new Constant(256)),
                    new LessThanOrEqual(new Add(
                        new ShiftRight(new Multiply(zx, zx), new Constant(S)),
                        new ShiftRight(new Multiply(zy, zy), new Constant(S))), new Constant(4 << S))),
                new Block([
                    new Assignment(zx2, new Add(new Subtract(
                        new ShiftRight(new Multiply(zx, zx), new Constant(S)),
                        new ShiftRight(new Multiply(zy, zy), new Constant(S))), Cx(x))),
                    new Assignment(zy, new Add(
                        new ShiftRight(new Multiply(new Multiply(zx, new Constant(2L)), zy), new Constant(S)), Cy(y))),
                    new Assignment(zx, zx2),
                    new Assignment(iter, new Add(iter, new Constant(1L)))
                ])),
            iter
        ]);

        var body = new Invoke(new Lambda([], new Block(
            [new Assignment(total, new Constant(0L)), new Assignment(y, new Constant(0L)),
             new WhileLoop(new LessThan(y, new Constant(size)),
                 new Block([
                     new Assignment(x, new Constant(0L)),
                     new WhileLoop(new LessThan(x, new Constant(size)),
                         new Block([
                             new Assignment(total, new Add(total, mandelPixel)),
                             new Assignment(x, new Add(x, new Constant(1L)))
                         ])),
                     new Assignment(y, new Add(y, new Constant(1L)))
                 ])),
             total],
            [x, y, zx, zy, zx2, zy2, iter, total])));

        await DumpAndVerify("mandelbrot", body, expectedResult: 256L * size * size);
    }

    // ── N-Queens (bitboard) ────────────────────────────────────────

    [Test]
    public async Task BenchmarkDump_NQueens() {
        int boardSize = 8;
        long allBits = (1L << boardSize) - 1;
        int stackSize = boardSize * boardSize * boardSize * 3;

        var stack = new Variable("stack");
        var sp = new Variable("sp");
        var total = new Variable("total");
        var ld = new Variable("ld"); var cols = new Variable("cols");
        var rd = new Variable("rd");
        var avail = new Variable("avail"); var bit = new Variable("bit");

        Node StackAt(Node idx) => new IndexAccess(stack, idx);
        Node Long(long v) => new Constant(v);

        var body = new Invoke(new Lambda([], new Block(
            [new Assignment(stack, new SN.NewArray(TypeReference.To<long>(), new Constant(stackSize))),
             new Assignment(sp, Long(0)), new Assignment(total, Long(0)),
             new Assignment(StackAt(sp), Long(0)), new Assignment(StackAt(new Add(sp, Long(1))), Long(0)),
             new Assignment(StackAt(new Add(sp, Long(2))), Long(0)),
             new Assignment(sp, new Add(sp, Long(3))),
             new WhileLoop(new GreaterThan(sp, Long(0)), new Block([
                 new Assignment(sp, new Subtract(sp, Long(3))),
                 new Assignment(ld, StackAt(sp)), new Assignment(cols, StackAt(new Add(sp, Long(1)))),
                 new Assignment(rd, StackAt(new Add(sp, Long(2)))),
                 new IfStatement(new Equal(cols, Long(allBits)), new Assignment(total, new Add(total, Long(1)))),
                 new Assignment(avail, new BitwiseAnd(new BitwiseNot(new BitwiseOr(new BitwiseOr(ld, cols), rd)), Long(allBits))),
                 new WhileLoop(new NotEqual(avail, Long(0)), new Block([
                     new Assignment(bit, new BitwiseAnd(new UnaryMinus(avail), avail)),
                     new Assignment(avail, new BitwiseXor(avail, bit)),
                     new Assignment(StackAt(sp), new ShiftLeft(new BitwiseOr(ld, bit), Long(1))),
                     new Assignment(StackAt(new Add(sp, Long(1))), new BitwiseOr(cols, bit)),
                     new Assignment(StackAt(new Add(sp, Long(2))), new ShiftRight(new BitwiseOr(rd, bit), Long(1))),
                     new Assignment(sp, new Add(sp, Long(3))),
                 ])),
             ])),
             total],
            [stack, sp, total, ld, cols, rd, avail, bit])));

        await DumpAndVerify("nqueens", body, expectedResult: 92);
    }

    // ── Shared pipeline ────────────────────────────────────────────

    /// <summary>
    /// Runs the full VM pipeline on <paramref name="body"/>, validates the
    /// result, and writes a formatted instruction listing to
    /// /tmp/{name}.polyvm.
    /// </summary>
    private static async Task DumpAndVerify(string name, Node body, long expectedResult) {
        // 1. Analyze and compile
        var analysis = Interpreter.Analyzer.Analyze(body);
        var meta = analysis.GetMetadata<PrimitiveExpansionMetadata>(body);
        await Assert.That(meta).IsNotNull();

        // 2. Get expanded primitives and append Return (required by VM)
        var primsList = meta!.Primitives.ToList();
        primsList.Add(new Prim.Return());

        // 3. Link to resolve label → PC offsets
        var linked = PrimitiveLinker.Link(primsList);

        // 4. Compile and execute
        var program = ProgramCompiler.CompilePrimitives(linked, CompilationMode.NoDebug);
        using var exec = Vm.Execute(program);
        long result = exec.RawValue;

        // 5. Build human-readable listing (before validation — dump even on failure)
        var sb = new StringBuilder();
        sb.AppendLine($"; {name}.polyvm — {linked.Count} primitives");
        sb.AppendLine($"; Stack effect: (Pop, Push)");
        sb.AppendLine($"; Exec result: {result} (expected {expectedResult})");
        sb.AppendLine($";");
        sb.AppendLine($"   PC   | Instruction                 | Effect");
        sb.AppendLine($"  ------+-----------------------------+--------");

        // Build PC → Label name map for ResolvedGoto/CondGoto display.
        // Use pre-link primitives so Label names are still visible.
        var pcToLabel = new Dictionary<int, string>();
        for (int i = 0; i < primsList.Count; i++) {
            if (primsList[i] is Prim.Label l)
                pcToLabel[i] = l.Name ?? $"L{i}";
        }

        for (int idx = 0; idx < linked.Count; idx++) {
            var prim = linked[idx];
            var (pop, push) = prim.StackEffect;
            var detail = FormatPrimitive(prim, pcToLabel);

            // Determine if this PC is a branch target; show marker in its own column
            string marker = "";
            foreach (var p in linked) {
                if (p is ResolvedGoto rg && rg.TargetPc == idx) { marker = "\u2190"; break; }
                if (p is ResolvedCondGoto rcg && rcg.TargetPc == idx) { marker = "?"; break; }
            }

            string markerCol = string.IsNullOrEmpty(marker) ? "   " : $" {marker} ";
            sb.AppendLine($"{idx,5}{markerCol}| {detail,-28}| ({pop},{push}) |");
        }

        // 7. Write to temp file
        var outputPath = $"/tmp/{name}.polyvm";
        await File.WriteAllTextAsync(outputPath, sb.ToString());

        // 6. Validate correctness
        await Assert.That(result).IsEqualTo(expectedResult);
    }

    private static string FormatPrimitive(PrimitiveNode prim, Dictionary<int, string> pcToLabel) {
        return prim switch {
            PushConstant pc => $"PushConstant {FormatValue(pc.Value)}",
            LoadLocal ll => $"LoadLocal slot={ll.SlotIndex}",
            StoreLocal sl => $"StoreLocal slot={sl.SlotIndex}",
            Prim.Parameter p => $"Parameter slot={p.SlotIndex}",
            BinaryOp bo => $"BinaryOp.{bo.Op}",
            UnaryOp uo => $"UnaryOp.{uo.Op}",
            Prim.Label l => $"Label:'{l.Name}'",
            Goto g => $"Goto {g.Target?.Name ?? "?"}",
            CondGoto cg => $"CondGoto {cg.Target?.Name ?? "?"}",
            ResolvedGoto rg => $"Goto pc_{rg.TargetPc}",
            ResolvedCondGoto rcg => $"CondGoto pc_{rcg.TargetPc}",
            Discard => "Discard",
            Dup => "Dup",
            Prim.Return => "Return",
            CountBits => "PopCount",
            ArrayLoad => "ArrayLoad",
            ArrayStore => "ArrayStore",
            Prim.NewArray => "NewArray",
            StridedSet => "StridedSetBits",
            Prim.Call c => $"Call args={c.ArgCount} func={c.FuncIndex}",
            Prim.CallExternal ce => $"CallExternal {ce.Target.Name}",
            Throw => "Throw",
            _ => prim.GetType().Name
        };
    }

    private static string FormatValue(object? value) => value switch {
        null => "null",
        string s => $"\"{s}\"",
        long l => l.ToString(),
        int i => i.ToString(),
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "?"
    };
}