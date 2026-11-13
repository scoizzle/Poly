using System.Globalization;

using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>
/// T-22a scaffolding (deleted in the slice's final commit): prints the lowered verdict, the root type and the run result
/// of shapes the operator grid does not hold, to the file named by POLY_T22A_PROBE. Run on the base and on the head; the
/// diff is quoted in the PR body.
/// </summary>
public class T22aProbeScaffolding {
    public sealed class Item {
        public long Count { get; set; }
    }

    public sealed class Other {
    }

    public struct Pair {
        public int Value;
    }

    public interface IShape {
    }

    public class Shape : IShape {
    }

    public sealed class Circle : Shape {
    }

    private static Parameter P(string name, Type type) => new(name, new ClrTypeReference(type));

    private static Node K(object? value) => new Constant(value);

    private static string Run(Func<Node> build, object?[] args) {
        try {
            var node = build();
            var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
            var errors = string.Join(" / ", analysis.Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message));
            if (errors.Length > 0)
                return "ERROR: " + errors;
            var stamp = analysis.GetResolvedType(node)?.Name ?? "unstamped";
            try {
                var program = Interpreter.Compile(node, analysis);
                using var execution = Interpreter.Execute(program, args);
                var type = program.RootClrType;
                object? value = type == typeof(bool) ? execution.GetValue<bool>() : execution.Result.Value;
                return $"OK stamp={stamp} vm={type?.Name} value={Convert.ToString(value, CultureInfo.InvariantCulture)}";
            }
            catch (Exception ex) {
                return $"THROWS stamp={stamp} {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
            }
        }
        catch (Exception ex) {
            return $"BUILD-THROWS {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }
    }

    private static IEnumerable<(string Shape, Func<Node> Build, object?[] Args)> Shapes() {
        var item = new Item();
        var circle = new Circle();
        // CLR enum bitwise and arithmetic (lock amendment 5).
        yield return ("enum & enum", () => new BitwiseAnd(P("a", typeof(AttributeTargets)), P("b", typeof(AttributeTargets))), [AttributeTargets.Class | AttributeTargets.Method, AttributeTargets.Method]);
        yield return ("enum | enum", () => new BitwiseOr(P("a", typeof(AttributeTargets)), P("b", typeof(AttributeTargets))), [AttributeTargets.Class, AttributeTargets.Method]);
        yield return ("enum ^ enum", () => new BitwiseXor(P("a", typeof(AttributeTargets)), P("b", typeof(AttributeTargets))), [AttributeTargets.Class, AttributeTargets.Method]);
        yield return ("~enum", () => new BitwiseNot(P("a", typeof(AttributeTargets))), [AttributeTargets.Class]);
        yield return ("enum? & enum", () => new BitwiseAnd(P("a", typeof(AttributeTargets?)), P("b", typeof(AttributeTargets))), [AttributeTargets.Class, AttributeTargets.Class]);
        yield return ("enum & int", () => new BitwiseAnd(P("a", typeof(AttributeTargets)), P("b", typeof(int))), [AttributeTargets.Class, 4]);
        yield return ("enum & otherenum", () => new BitwiseAnd(P("a", typeof(AttributeTargets)), P("b", typeof(DayOfWeek))), [AttributeTargets.Class, DayOfWeek.Monday]);
        yield return ("enum << 1", () => new ShiftLeft(P("a", typeof(AttributeTargets)), K(1)), [AttributeTargets.Class]);
        yield return ("-enum", () => new UnaryMinus(P("a", typeof(DayOfWeek))), [DayOfWeek.Monday]);
        yield return ("enum + 1", () => new Add(P("a", typeof(DayOfWeek)), K(1)), [DayOfWeek.Monday]);
        yield return ("enum - enum", () => new Subtract(P("a", typeof(DayOfWeek)), P("b", typeof(DayOfWeek))), [DayOfWeek.Friday, DayOfWeek.Monday]);
        // Reference equality (lock amendment 3).
        yield return ("obj == obj", () => new Equal(P("a", typeof(object)), P("b", typeof(object))), [item, item]);
        yield return ("obj != obj", () => new NotEqual(P("a", typeof(object)), P("b", typeof(object))), [item, new Item()]);
        yield return ("obj == null", () => new Equal(P("a", typeof(object)), K(null)), [null]);
        yield return ("ent == ent", () => new Equal(P("a", typeof(Item)), P("b", typeof(Item))), [item, item]);
        yield return ("ent != ent", () => new NotEqual(P("a", typeof(Item)), P("b", typeof(Item))), [item, new Item()]);
        yield return ("ent == null", () => new Equal(P("a", typeof(Item)), K(null)), [item]);
        yield return ("null == ent", () => new Equal(K(null), P("a", typeof(Item))), [item]);
        yield return ("ent == obj", () => new Equal(P("a", typeof(Item)), P("b", typeof(object))), [item, item]);
        yield return ("ent == other", () => new Equal(P("a", typeof(Item)), P("b", typeof(Other))), [item, new Other()]);
        yield return ("shape == circle", () => new Equal(P("a", typeof(Shape)), P("b", typeof(Circle))), [circle, circle]);
        yield return ("ishape == circle", () => new Equal(P("a", typeof(IShape)), P("b", typeof(Circle))), [circle, circle]);
        yield return ("ishape == other", () => new Equal(P("a", typeof(IShape)), P("b", typeof(Other))), [circle, new Other()]);
        yield return ("str == obj", () => new Equal(P("a", typeof(string)), P("b", typeof(object))), ["x", "x"]);
        yield return ("str == str", () => new Equal(P("a", typeof(string)), P("b", typeof(string))), ["x", "x"]);
        yield return ("list == null", () => new Equal(P("a", typeof(List<int>)), K(null)), [new List<int>()]);
        yield return ("array == array", () => new Equal(P("a", typeof(long[])), P("b", typeof(long[]))), [new long[] { 1 }, new long[] { 1 }]);
        yield return ("array == intarray", () => new Equal(P("a", typeof(long[])), P("b", typeof(int[]))), [new long[] { 1 }, new int[] { 1 }]);
        yield return ("pair == pair", () => new Equal(P("a", typeof(Pair)), P("b", typeof(Pair))), [new Pair(), new Pair()]);
        yield return ("guid == guid", () => new Equal(P("a", typeof(Guid)), P("b", typeof(Guid))), [Guid.Empty, Guid.Empty]);
        yield return ("ent == 1", () => new Equal(P("a", typeof(Item)), K(1L)), [item]);
        yield return ("obj == 1", () => new Equal(P("a", typeof(object)), K(1)), [1]);
        yield return ("obj < obj", () => new LessThan(P("a", typeof(object)), P("b", typeof(object))), [1, 2]);
        yield return ("int? == null", () => new Equal(P("a", typeof(int?)), K(null)), [null]);
        // Concatenation stays (lock amendment 4).
        yield return ("obj + \"a\"", () => new Add(P("a", typeof(object)), K("a")), [1]);
        yield return ("ent + \"a\"", () => new Add(P("a", typeof(Item)), K("a")), [item]);
        yield return ("\"a\" + ent", () => new Add(K("a"), P("a", typeof(Item))), [item]);
        // Undefined arithmetic (named in the lock).
        yield return ("obj + 1", () => new Add(P("a", typeof(object)), K(1)), [1]);
        yield return ("1 + obj", () => new Add(K(1), P("a", typeof(object))), [1]);
        yield return ("ent + 1", () => new Add(P("a", typeof(Item)), K(1)), [item]);
        yield return ("pair + 1", () => new Add(P("a", typeof(Pair)), K(1)), [new Pair()]);
        yield return ("obj & 1", () => new BitwiseAnd(P("a", typeof(object)), K(1)), [1]);
        yield return ("obj << 1", () => new ShiftLeft(P("a", typeof(object)), K(1)), [1]);
        yield return ("-obj", () => new UnaryMinus(P("a", typeof(object))), [1]);
        yield return ("~obj", () => new BitwiseNot(P("a", typeof(object))), [1]);
        yield return ("-ent", () => new UnaryMinus(P("a", typeof(Item))), [item]);
        yield return ("null + 1", () => new Add(K(null), K(1)), []);
        yield return ("null & 1", () => new BitwiseAnd(K(null), K(1)), []);
        // Nullable and constant cells.
        yield return ("int? << long", () => new ShiftLeft(P("a", typeof(int?)), P("b", typeof(long))), [3, 2L]);
        yield return ("int? << long (null)", () => new ShiftLeft(P("a", typeof(int?)), P("b", typeof(long))), [null, 2L]);
        yield return ("int << long?", () => new ShiftLeft(P("a", typeof(int)), P("b", typeof(long?))), [3, 2L]);
        yield return ("long >> uint", () => new ShiftRight(P("a", typeof(long)), P("b", typeof(uint))), [-64L, 3u]);
        yield return ("int << long 33", () => new ShiftLeft(P("a", typeof(int)), P("b", typeof(long))), [1, 33L]);
        yield return ("int << long 2^32+1", () => new ShiftLeft(P("a", typeof(int)), P("b", typeof(long))), [1, 4294967297L]);
        yield return ("long << ulong max", () => new ShiftLeft(P("a", typeof(long)), P("b", typeof(ulong))), [1L, ulong.MaxValue]);
        yield return ("byte << long", () => new ShiftLeft(P("a", typeof(byte)), P("b", typeof(long))), [(byte)255, 4L]);
        yield return ("char >> nint", () => new ShiftRight(P("a", typeof(char)), P("b", typeof(nint))), ['a', (nint)1]);
        yield return ("int << 3L const", () => new ShiftLeft(P("a", typeof(int)), K(3L)), [1]);
        yield return ("1L << 3L consts", () => new ShiftLeft(K(1L), K(3L)), []);
        yield return ("int << int", () => new ShiftLeft(P("a", typeof(int)), P("b", typeof(int))), [1, 33]);
        yield return ("ulong << 1", () => new ShiftLeft(P("a", typeof(ulong)), K(1)), [ulong.MaxValue]);
        yield return ("int << short", () => new ShiftLeft(P("a", typeof(int)), P("b", typeof(short))), [1, (short)3]);
        yield return ("int << double", () => new ShiftLeft(P("a", typeof(int)), P("b", typeof(double))), [1, 3.0]);
        yield return ("int << decimal", () => new ShiftLeft(P("a", typeof(int)), P("b", typeof(decimal))), [1, 3m]);
        yield return ("u64? | i32", () => new BitwiseOr(P("a", typeof(ulong?)), P("b", typeof(int))), [5UL, 1]);
        yield return ("int? & long?", () => new BitwiseAnd(P("a", typeof(int?)), P("b", typeof(long?))), [7, 3L]);
        yield return ("int & long", () => new BitwiseAnd(P("a", typeof(int)), P("b", typeof(long))), [-1, 4294967296L]);
        yield return ("bool & bool", () => new BitwiseAnd(P("a", typeof(bool)), P("b", typeof(bool))), [true, false]);
        yield return ("bool? & bool?", () => new BitwiseAnd(P("a", typeof(bool?)), P("b", typeof(bool?))), [true, null]);
        yield return ("bool? & bool", () => new BitwiseAnd(P("a", typeof(bool?)), P("b", typeof(bool))), [true, true]);
        yield return ("bool ^ int", () => new BitwiseXor(P("a", typeof(bool)), P("b", typeof(int))), [true, 1]);
        yield return ("~int?", () => new BitwiseNot(P("a", typeof(int?))), [5]);
        yield return ("-int?", () => new UnaryMinus(P("a", typeof(int?))), [5]);
        yield return ("-uint", () => new UnaryMinus(P("a", typeof(uint))), [5u]);
        yield return ("-ulong", () => new UnaryMinus(P("a", typeof(ulong))), [5UL]);
        yield return ("-ulong?", () => new UnaryMinus(P("a", typeof(ulong?))), [5UL]);
        yield return ("-nuint", () => new UnaryMinus(P("a", typeof(nuint))), [(nuint)5]);
        yield return ("-byte", () => new UnaryMinus(P("a", typeof(byte))), [(byte)5]);
        yield return ("~byte", () => new BitwiseNot(P("a", typeof(byte))), [(byte)5]);
        yield return ("-decimal", () => new UnaryMinus(P("a", typeof(decimal))), [5m]);
        yield return ("-duration", () => new UnaryMinus(P("a", typeof(TimeSpan))), [TimeSpan.FromHours(1)]);
        yield return ("-text", () => new UnaryMinus(P("a", typeof(string))), ["x"]);
        yield return ("ulong & 1", () => new BitwiseAnd(P("a", typeof(ulong)), K(1)), [ulong.MaxValue]);
        yield return ("1 | ulong", () => new BitwiseOr(K(1), P("a", typeof(ulong))), [8UL]);
        yield return ("ulong & -1", () => new BitwiseAnd(P("a", typeof(ulong)), K(-1)), [8UL]);
        yield return ("ulong & int", () => new BitwiseAnd(P("a", typeof(ulong)), P("b", typeof(int))), [ulong.MaxValue, -1]);
        yield return ("nuint & int", () => new BitwiseAnd(P("a", typeof(nuint)), P("b", typeof(int))), [(nuint)8, -1]);
        yield return ("nuint & 1", () => new BitwiseAnd(P("a", typeof(nuint)), K(1)), [(nuint)9]);
        yield return ("uint & 1", () => new BitwiseAnd(P("a", typeof(uint)), K(1)), [uint.MaxValue]);
        yield return ("uint | -1", () => new BitwiseOr(P("a", typeof(uint)), K(-1)), [5u]);
        yield return ("uint & -1", () => new BitwiseAnd(P("a", typeof(uint)), K(-1)), [5u]);
        yield return ("int & 1L", () => new BitwiseAnd(P("a", typeof(int)), K(1L)), [3]);
        yield return ("long & int", () => new BitwiseAnd(P("a", typeof(long)), P("b", typeof(int))), [-1L, -1]);
        yield return ("text & int", () => new BitwiseAnd(P("a", typeof(string)), P("b", typeof(int))), ["x", 1]);
        yield return ("decimal | long", () => new BitwiseOr(P("a", typeof(decimal)), P("b", typeof(long))), [1m, 2L]);
        yield return ("double & long", () => new BitwiseAnd(P("a", typeof(double)), P("b", typeof(long))), [1d, 2L]);
        yield return ("~text", () => new BitwiseNot(P("a", typeof(string))), ["x"]);
        yield return ("(a & b) + 1", () => new Add(new BitwiseAnd(P("a", typeof(int)), P("b", typeof(long))), K(1)), [3, 1L]);
        yield return ("TakesLong(int & long)", () => new Invoke(new Member(new ClrTypeReference(typeof(Math)), "Abs"), new BitwiseAnd(P("a", typeof(int)), P("b", typeof(long)))), [-1, -2L]);
        yield return ("arr[int << long]", () => new IndexAccess(P("arr", typeof(long[])), new ShiftLeft(P("a", typeof(int)), P("b", typeof(long)))), [new long[] { 5, 6, 7, 8 }, 1, 1L]);
    }

    [Test]
    public async Task Probe_WhenRequested_PrintsEveryShape() {
        var path = Environment.GetEnvironmentVariable("POLY_T22A_PROBE");
        if (path is null)
            return;
        var lines = Shapes().Select(static s => $"{s.Shape}\t{Run(s.Build, s.Args)}").ToArray();
        File.WriteAllLines(path, lines);
        await Assert.That(lines.Length).IsGreaterThan(0);
    }
}