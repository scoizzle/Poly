using System.Linq.Expressions;
using System.Text;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Renders the expression body the emitter produces, indented, so it can be read the way
/// `docs/interpretation/vm-abi-reference.md` describes it. Writes the dump to
/// `/tmp/expr-sieve-strided.txt` and asserts the structural facts this suite relies on, so the
/// artifact in `docs/perf-comparison/sieve-expression-body.txt` cannot rot silently.
/// </summary>
public class ExpressionBodyDumpTests {
    [Test]
    public async Task Sieve_Body_Renders_AndShowsTheStridedDeref() {
        var body = SieveStrided();
        var built = AbiEmitter.Build(body, Interpreter.Analyze(body), CompilationMode.NoDebug);
        var sb = new StringBuilder();
        Dump(built.Expression.Body, sb, 0);
        var text = sb.ToString();
        File.WriteAllText("/tmp/expr-sieve-strided.txt", text);

        await Assert.That(text).Contains("_stride_loop")
            .Because("the strided mark loop is emitted as a labelled loop");

        // `bits[j >> 6] |= mask` lowers to `a[i] = a[i] | mask`, and each `a[i]` re-derefs the
        // heap: the buffer reaches StridedSetBits as an argument, so the array cache declines it.
        var stride = text[text.IndexOf("_stride_loop", StringComparison.Ordinal)..];
        stride = stride[..stride.IndexOf("_stride_done", StringComparison.Ordinal)];
        // Count call nodes, not text: the renderer also prints a node's text on its parent's line.
        var derefs = stride.Split("Call : Object  _heap.UnsafeGet").Length - 1;
        await Assert.That(derefs).IsEqualTo(2)
            .Because($"the strided store re-derefs the heap once for the read and once for the write; found {derefs}");

        await Assert.That(text).Contains("_i4:Int32")
            .Because("an int constant is an Int32 ring slot (IS7c)");
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

    static void Dump(Expression? e, StringBuilder sb, int depth) {
        if (e is null) return;
        var pad = new string(' ', depth * 2);
        switch (e) {
            case LambdaExpression l:
                sb.AppendLine($"{pad}lambda");
                Dump(l.Body, sb, depth + 1);
                return;
            case BlockExpression b:
                sb.AppendLine($"{pad}block : {Fmt(b.Type)}  locals=[{string.Join(", ", b.Variables.Select(v => $"{v.Name}:{Fmt(v.Type)}"))}]");
                foreach (var x in b.Expressions) Dump(x, sb, depth + 1);
                return;
            case ConditionalExpression c:
                sb.AppendLine($"{pad}if ({Fmt(c.Test.Type)})");
                Dump(c.Test, sb, depth + 1);
                sb.AppendLine($"{pad}then");
                Dump(c.IfTrue, sb, depth + 1);
                sb.AppendLine($"{pad}else");
                Dump(c.IfFalse, sb, depth + 1);
                return;
            case GotoExpression g:
                sb.AppendLine($"{pad}{g.Kind} -> {g.Target?.Name}");
                return;
            case LabelExpression la:
                sb.AppendLine($"{pad}label {la.Target.Name}");
                Dump(la.DefaultValue, sb, depth + 1);
                return;
            case LoopExpression lo:
                sb.AppendLine($"{pad}loop {lo.BreakLabel?.Name}/{lo.ContinueLabel?.Name}");
                Dump(lo.Body, sb, depth + 1);
                return;
            case TryExpression t:
                sb.AppendLine($"{pad}try");
                Dump(t.Body, sb, depth + 1);
                foreach (var h in t.Handlers) { sb.AppendLine($"{pad}catch"); Dump(h.Body, sb, depth + 1); }
                if (t.Finally is not null) { sb.AppendLine($"{pad}finally"); Dump(t.Finally, sb, depth + 1); }
                return;
            case BinaryExpression bin when bin.NodeType == ExpressionType.Assign:
                sb.AppendLine($"{pad}assign : {Fmt(bin.Type)}");
                Dump(bin.Left, sb, depth + 1);
                Dump(bin.Right, sb, depth + 1);
                return;
            default:
                sb.AppendLine($"{pad}{e.NodeType} : {Fmt(e.Type)}  {Short(e)}");
                switch (e) {
                    case BinaryExpression b2: Dump(b2.Left, sb, depth + 1); Dump(b2.Right, sb, depth + 1); break;
                    case UnaryExpression u: Dump(u.Operand, sb, depth + 1); break;
                    case MethodCallExpression mc:
                        Dump(mc.Object, sb, depth + 1);
                        foreach (var a in mc.Arguments) Dump(a, sb, depth + 1);
                        break;
                    case InvocationExpression inv:
                        Dump(inv.Expression, sb, depth + 1);
                        foreach (var a in inv.Arguments) Dump(a, sb, depth + 1);
                        break;
                    case IndexExpression ix:
                        Dump(ix.Object, sb, depth + 1);
                        foreach (var a in ix.Arguments) Dump(a, sb, depth + 1);
                        break;
                    case NewArrayExpression na: foreach (var x in na.Expressions) Dump(x, sb, depth + 1); break;
                    case MemberExpression me: Dump(me.Expression, sb, depth + 1); break;
                    case TypeBinaryExpression tb: Dump(tb.Expression, sb, depth + 1); break;
                }
                return;
        }
    }

    static string Fmt(Type t) => t.Name;
    static string Short(Expression e) {
        var s = e.ToString().Replace("\r", " ").Replace("\n", " ");
        return s.Length > 90 ? s[..90] + "…" : s;
    }
}