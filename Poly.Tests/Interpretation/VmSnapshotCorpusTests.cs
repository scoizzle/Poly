using System.Text;
using System.Text.RegularExpressions;

using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Dumps the emitted VM expression tree for a fixed corpus, so a later refactor can be diffed
/// against a baseline. Writes <c>&lt;POLY_VM_SNAPSHOT_DIR&gt;/&lt;entry&gt;.txt</c> when that
/// variable is set; otherwise the test passes without touching the filesystem.
/// </summary>
public class VmSnapshotCorpusTests {
    /// <summary>A <see cref="NodeId"/> is a fresh GUID per run, so a suspend's emitted
    /// constant is normalised out; the tree shape around it is what a diff compares.</summary>
    static readonly Regex NodeIdText = new(@"\b[0-9a-f]{32}\b");

    [Test]
    public async Task Snapshot_CorpusMatchesWhenRequested() {
        var dir = Environment.GetEnvironmentVariable("POLY_VM_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;

        Directory.CreateDirectory(dir);
        foreach (var (name, program) in Corpus()) {
            var text = new StringBuilder();
            foreach (var mode in new[] { CompilationMode.NoDebug, CompilationMode.Normal }) {
                var built = AbiEmitter.Build(program, Interpreter.Analyze(program), mode);
                text.AppendLine($"// {mode}");
                text.AppendLine(NodeIdText.Replace(ExpressionText.Render(built.Expression), "<nodeid>"));
            }
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".txt"), text.ToString());
        }
    }

    static IEnumerable<(string Name, Node Program)> Corpus() {
        yield return ("readme_var_add", ReadmeVarAdd());
        yield return ("suspend_var", SuspendVar());
        yield return ("double_var", DoubleVar());
        yield return ("ieee_mixed", IeeeMixed());
        yield return ("loop_sum", LoopSum());
        yield return ("nested_blocks", NestedBlocks());
        yield return ("shadowing", Shadowing());
        yield return ("try_catch", TryCatch());
        yield return ("array_local", ArrayLocal());
        yield return ("nullable_local", NullableLocal());
        yield return ("function_invoke", FunctionInvoke());
        yield return ("suspend_inside_loop", SuspendInsideLoop());
        yield return ("coalesce", CoalesceEntry());
    }

    /// <summary>The README worked example: <c>var x = 2; x + 1</c>.</summary>
    static Node ReadmeVarAdd() {
        var x = new Variable("x");
        return new Block([
            new Assignment(x, new Constant(2L)),
            new Add(x, new Constant(1L)),
        ], [x]);
    }

    static Node SuspendVar() {
        var x = new Variable("x");
        return new Block([
            new Assignment(x, new Constant(41L)),
            new SuspendNode(new Constant(0L), "mid"),
            new Add(x, new Constant(1L)),
        ], [x]);
    }

    static Node DoubleVar() {
        var zr = new Variable("zr");
        var zi = new Variable("zi");
        return new Block([
            new Assignment(zr, new Constant(0.5)),
            new Assignment(zi, new Constant(0.25)),
            new Assignment(zr, new Subtract(new Multiply(zr, zr), new Multiply(zi, zi))),
            zr,
        ], [zr, zi]);
    }

    static Node IeeeMixed() {
        var i = new Variable("i");
        var l = new Variable("l");
        var d = new Variable("d");
        return new Block([
            new Assignment(i, new Constant(3)),
            new Assignment(l, new Constant(4L)),
            new Assignment(d, new Add(
                new TypeCast(i, TypeReference.To<double>()),
                new TypeCast(l, TypeReference.To<double>()))),
            d,
        ], [i, l, d]);
    }

    static Node LoopSum() {
        var i = new Variable("i");
        var sum = new Variable("sum");
        return new Block([
            new Assignment(i, new Constant(0L)),
            new Assignment(sum, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(5L)),
                new Block([
                    new Assignment(sum, new Add(sum, i)),
                    new Assignment(i, new Add(i, new Constant(1L))),
                ])),
            sum,
        ], [i, sum]);
    }

    static Node NestedBlocks() {
        var a = new Variable("a");
        var b = new Variable("b");
        return new Block([
            new Block([
                new Assignment(a, new Constant(1L)),
                a,
            ], [a]),
            new Block([
                new Assignment(b, new Constant(2L)),
                b,
            ], [b]),
        ]);
    }

    static Node Shadowing() {
        var outer = new Variable("s");
        var inner = new Variable("s");
        return new Block([
            new Assignment(outer, new Constant(1L)),
            new Block([
                new Assignment(inner, new Constant(10L)),
                new Add(outer, inner),
            ], [inner]),
            outer,
        ], [outer]);
    }

    static Node TryCatch() {
        var caught = new Variable("caught");
        return new Block([
            new Assignment(caught, new Constant(0L)),
            new TryCatchFinally(
                new ThrowStatement(new New(new ClrTypeReference(typeof(InvalidOperationException)))),
                CatchClauses: [
                    new CatchClause(
                        new ClrTypeReference(typeof(InvalidOperationException)),
                        "ex",
                        new Assignment(caught, new Constant(1L))),
                ]),
            caught,
        ], [caught]);
    }

    static Node ArrayLocal() {
        var cols = new Variable("cols");
        return new Block([
            new Assignment(cols, new NewArray(TypeReference.To<long>(), new Constant(4L))),
            new Assignment(new IndexAccess(cols, new Constant(0L)), new Constant(7L)),
            new IndexAccess(cols, new Constant(0L)),
        ], [cols]);
    }

    static Node NullableLocal() {
        var n = new Variable("n");
        return new Block([
            new Assignment(n, new TypeCast(new Constant(null), TypeReference.To<int?>())),
            new Equal(n, new Constant(null)),
        ], [n]);
    }

    static Node FunctionInvoke() {
        var x = new Parameter("x", TypeReference.To<long>());
        return new Invoke(new Lambda([x], new Add(x, new Constant(1L))), new Constant(6L));
    }

    static Node SuspendInsideLoop() {
        var i = new Variable("i");
        return new Block([
            new Assignment(i, new Constant(0L)),
            new WhileLoop(
                new LessThan(i, new Constant(3L)),
                new Block([
                    new SuspendNode(new Constant(0L), "tick"),
                    new Assignment(i, new Add(i, new Constant(1L))),
                ])),
            i,
        ], [i]);
    }

    static Node CoalesceEntry() {
        return new Coalesce(new Constant(100 as int?), new Constant(42));
    }
}