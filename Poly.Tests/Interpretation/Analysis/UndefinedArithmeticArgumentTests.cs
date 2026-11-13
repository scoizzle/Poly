using System.Globalization;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>
/// T-17 closing round: arithmetic over operands the provider defines no operator for (object, entity, struct, null constant,
/// unresolved, the nullable shift and bitwise cells) has no stamp, but every consumer position judges it exactly as master
/// did, when the private rule stamped the left operand's type (a string when an operand was exactly string). Each cell is
/// pinned with master's verdict, message and value (<see cref="Expected"/> was generated on origin/master). The cell records the verdict, the message and the value, not the
/// root type name: a bare undefined sum has no stamp (master stamped the left operand's type), which only the VM root type shows.
/// </summary>
public class UndefinedArithmeticArgumentTests {
    public struct Pair {
        public int Value;
    }

    public sealed class Holder {
        public long Count { get; set; }
    }

    public static class Calls {
        public static int TakesInt(int value) => value;
        public static object TakesObject(object value) => value;
        public static Item TakesItem(Item value) => value;
        public static Pair TakesPair(Pair value) => value;
        public static string TakesString(string value) => value;
        public static int? TakesNInt(int? value) => value;
        public static ulong? TakesNULong(ulong? value) => value;
    }

    public sealed class Item(decimal price) {
        public decimal Price { get; set; } = price;
    }

    public sealed class CtorInt(int value) {
        public int Value { get; } = value;
    }

    public sealed class CtorObject(object value) {
        public object Value { get; } = value;
    }

    public sealed class CtorItem(Item value) {
        public Item Value { get; } = value;
    }

    private static Parameter P(string name, Type type) => new(name, new ClrTypeReference(type));

    private static Node K(object? value) => new Constant(value);

    private static Node Call(string method, Node argument) => new Invoke(new Member(new ClrTypeReference(typeof(Calls)), method), argument);

    private static string Run(Func<Node> build, object?[] args) {
        try {
            var node = build();
            var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
            var errors = string.Join(" / ", analysis.Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message));
            if (errors.Length > 0)
                return "ERROR: " + errors;
            try {
                var program = Interpreter.Compile(node, analysis);
                using var execution = Interpreter.Execute(program, args);
                var type = program.RootClrType;
                var value = type == typeof(int) ? execution.GetValue<int>()
                    : type == typeof(long) ? execution.GetValue<long>()
                    : type == typeof(string) ? execution.GetValue<string>()
                    : type == typeof(bool) ? execution.GetValue<bool>()
                    : execution.GetValue<object>();
                return $"OK {Convert.ToString(value, CultureInfo.InvariantCulture)}";
            }
            catch (Exception ex) {
                return $"THROWS {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
            }
        }
        catch (Exception ex) {
            return $"BUILD-THROWS {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }
    }

    private static IEnumerable<(string Shape, Func<Node> Build, object?[] Args)> Shapes() {
        yield return ("obj + 1", () => new Add(P("o", typeof(object)), K(1)), [1]);
        yield return ("1 + obj", () => new Add(K(1), P("o", typeof(object))), [1]);
        yield return ("obj * 2", () => new Multiply(P("o", typeof(object)), K(2)), [1]);
        yield return ("obj - 1", () => new Subtract(P("o", typeof(object)), K(1)), [1]);
        yield return ("(obj + 1) * 2", () => new Multiply(new Add(P("o", typeof(object)), K(1)), K(2)), [1]);
        yield return ("1 + (obj + 1)", () => new Add(K(1), new Add(P("o", typeof(object)), K(1))), [1]);
        yield return ("ent + 1", () => new Add(P("e", typeof(Item)), K(1)), [new Item(1m)]);
        yield return ("st + 1", () => new Add(P("s", typeof(Pair)), K(1)), [new Pair()]);
        yield return ("null + 1", () => new Add(K(null), K(1)), []);
        yield return ("\"a\" + u", () => new Add(K("a"), new Default(null)), []);
        yield return ("u + \"a\"", () => new Add(new Default(null), K("a")), []);
        yield return ("i32n << i64", () => new ShiftLeft(P("a", typeof(int?)), P("b", typeof(long))), [3, 2L]);
        yield return ("u64n | i32", () => new BitwiseOr(P("a", typeof(ulong?)), P("b", typeof(int))), [5UL, 1]);
    }

    public static IEnumerable<(string Name, Func<string> Run)> Cases() {
        foreach (var (shape, build, args) in Shapes()) {
            string Name(string consumer) => $"{shape} @ {consumer}";
            yield return (Name("root"), () => Run(build, args));
            foreach (var method in new[] { "TakesInt", "TakesObject", "TakesItem", "TakesPair", "TakesString", "TakesNInt", "TakesNULong" })
                yield return (Name(method), () => Run(() => Call(method, build()), args));
            yield return (Name("new CtorInt"), () => Run(() => new New(TypeReference.To<CtorInt>(), build()), args));
            yield return (Name("new CtorObject"), () => Run(() => new New(TypeReference.To<CtorObject>(), build()), args));
            yield return (Name("new CtorItem"), () => Run(() => new New(TypeReference.To<CtorItem>(), build()), args));
            yield return (Name("index"), () => Run(() => new IndexAccess(P("arr", typeof(long[])), build()), [new long[] { 5, 6, 7 }, .. args]));
            yield return (Name("assign var"), () => Run(() => {
                var x = new Variable("x");
                return new Block([new Assignment(x, K(0)), new Assignment(x, build()), x], [x]);
            }, args));
            yield return (Name("assign member"), () => Run(() => {
                var holder = P("h", typeof(Holder));
                return new Block([new Assignment(new Member(holder, "Count"), build()), new Member(holder, "Count")]);
            }, [new Holder(), .. args]));
            yield return (Name("conditional arm"), () => Run(() => Call("TakesInt", new Conditional(P("c", typeof(bool)), build(), K(5))), [true, .. args]));
            yield return (Name("coalesce"), () => Run(() => Call("TakesInt", new Coalesce(P("x", typeof(int?)), build())), [null, .. args]));
            yield return (Name("return"), () => Run(() => new Block([new Return(build())]), args));
        }
    }

    [Test]
    public async Task UndefinedArithmetic_InEveryConsumer_HasMastersVerdictMessageAndValue() {
        var generate = Environment.GetEnvironmentVariable("UNDEFPIN_OUT");
        var actual = Cases().Select(static c => $"{c.Name}\t{c.Run()}").ToArray();
        if (generate is not null) {
            File.WriteAllLines(generate, actual);
            return;
        }
        await Assert.That(actual).IsEquivalentTo(Expected);
    }

    private static readonly string[] Expected = [
        "obj + 1 @ root\tOK 2",
        "obj + 1 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj + 1 @ TakesObject\tOK ",
        "obj + 1 @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "obj + 1 @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "obj + 1 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "obj + 1 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "obj + 1 @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "obj + 1 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "obj + 1 @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "obj + 1 @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "obj + 1 @ index\tOK 7",
        "obj + 1 @ assign var\tERROR: cannot assign 'Object' to variable 'x' (incompatible with prior 'Int32')",
        "obj + 1 @ assign member\tOK 2",
        "obj + 1 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj + 1 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj + 1 @ return\tOK 2",
        "1 + obj @ root\tOK 2",
        "1 + obj @ TakesInt\tOK 2",
        "1 + obj @ TakesObject\tOK 2",
        "1 + obj @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "1 + obj @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "1 + obj @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "1 + obj @ TakesNInt\tOK 2",
        "1 + obj @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "1 + obj @ new CtorInt\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorInt",
        "1 + obj @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "1 + obj @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "1 + obj @ index\tOK 7",
        "1 + obj @ assign var\tOK 2",
        "1 + obj @ assign member\tOK 2",
        "1 + obj @ conditional arm\tOK 2",
        "1 + obj @ coalesce\tOK 2",
        "1 + obj @ return\tOK 2",
        "obj * 2 @ root\tOK 2",
        "obj * 2 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj * 2 @ TakesObject\tOK ",
        "obj * 2 @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "obj * 2 @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "obj * 2 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "obj * 2 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "obj * 2 @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "obj * 2 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "obj * 2 @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "obj * 2 @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "obj * 2 @ index\tOK 7",
        "obj * 2 @ assign var\tERROR: cannot assign 'Object' to variable 'x' (incompatible with prior 'Int32')",
        "obj * 2 @ assign member\tOK 2",
        "obj * 2 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj * 2 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj * 2 @ return\tOK 2",
        "obj - 1 @ root\tOK ",
        "obj - 1 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj - 1 @ TakesObject\tOK ",
        "obj - 1 @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "obj - 1 @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "obj - 1 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "obj - 1 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "obj - 1 @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "obj - 1 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "obj - 1 @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "obj - 1 @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "obj - 1 @ index\tOK 5",
        "obj - 1 @ assign var\tERROR: cannot assign 'Object' to variable 'x' (incompatible with prior 'Int32')",
        "obj - 1 @ assign member\tOK 0",
        "obj - 1 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj - 1 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "obj - 1 @ return\tOK ",
        "(obj + 1) * 2 @ root\tOK 4",
        "(obj + 1) * 2 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "(obj + 1) * 2 @ TakesObject\tOK ",
        "(obj + 1) * 2 @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "(obj + 1) * 2 @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "(obj + 1) * 2 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "(obj + 1) * 2 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "(obj + 1) * 2 @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "(obj + 1) * 2 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "(obj + 1) * 2 @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "(obj + 1) * 2 @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "(obj + 1) * 2 @ index\tTHROWS IndexOutOfRangeException: Index was outside the bounds of the array.",
        "(obj + 1) * 2 @ assign var\tERROR: cannot assign 'Object' to variable 'x' (incompatible with prior 'Int32')",
        "(obj + 1) * 2 @ assign member\tOK 4",
        "(obj + 1) * 2 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "(obj + 1) * 2 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "(obj + 1) * 2 @ return\tOK 4",
        "1 + (obj + 1) @ root\tOK 3",
        "1 + (obj + 1) @ TakesInt\tOK 3",
        "1 + (obj + 1) @ TakesObject\tOK 3",
        "1 + (obj + 1) @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "1 + (obj + 1) @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "1 + (obj + 1) @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "1 + (obj + 1) @ TakesNInt\tOK 3",
        "1 + (obj + 1) @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "1 + (obj + 1) @ new CtorInt\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorInt",
        "1 + (obj + 1) @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "1 + (obj + 1) @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "1 + (obj + 1) @ index\tTHROWS IndexOutOfRangeException: Index was outside the bounds of the array.",
        "1 + (obj + 1) @ assign var\tOK 3",
        "1 + (obj + 1) @ assign member\tOK 3",
        "1 + (obj + 1) @ conditional arm\tOK 3",
        "1 + (obj + 1) @ coalesce\tOK 3",
        "1 + (obj + 1) @ return\tOK 3",
        "ent + 1 @ root\tOK 2",
        "ent + 1 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "ent + 1 @ TakesObject\tOK ",
        "ent + 1 @ TakesItem\tOK ",
        "ent + 1 @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "ent + 1 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "ent + 1 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "ent + 1 @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "ent + 1 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "ent + 1 @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "ent + 1 @ new CtorItem\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorItem",
        "ent + 1 @ index\tTHROWS IndexOutOfRangeException: Index was outside the bounds of the array.",
        "ent + 1 @ assign var\tERROR: cannot assign 'Item' to variable 'x' (incompatible with prior 'Int32')",
        "ent + 1 @ assign member\tOK 3",
        "ent + 1 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "ent + 1 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "ent + 1 @ return\tOK 2",
        "st + 1 @ root\tOK 2",
        "st + 1 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "st + 1 @ TakesObject\tOK ",
        "st + 1 @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "st + 1 @ TakesPair\tTHROWS NullReferenceException: Object reference not set to an instance of an object.",
        "st + 1 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "st + 1 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "st + 1 @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "st + 1 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "st + 1 @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "st + 1 @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "st + 1 @ index\tTHROWS IndexOutOfRangeException: Index was outside the bounds of the array.",
        "st + 1 @ assign var\tERROR: cannot assign 'Pair' to variable 'x' (incompatible with prior 'Int32')",
        "st + 1 @ assign member\tOK 3",
        "st + 1 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "st + 1 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "st + 1 @ return\tOK 2",
        "null + 1 @ root\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'null' (Constant).",
        "null + 1 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "null + 1 @ TakesObject\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'null' (Constant).",
        "null + 1 @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "null + 1 @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "null + 1 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "null + 1 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "null + 1 @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "null + 1 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "null + 1 @ new CtorObject\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'null' (Constant).",
        "null + 1 @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "null + 1 @ index\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'null' (Constant).",
        "null + 1 @ assign var\tERROR: cannot assign 'Object' to variable 'x' (incompatible with prior 'Int32')",
        "null + 1 @ assign member\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'null' (Constant).",
        "null + 1 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "null + 1 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "null + 1 @ return\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'null' (Constant).",
        "\"a\" + u @ root\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "\"a\" + u @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "\"a\" + u @ TakesObject\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "\"a\" + u @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "\"a\" + u @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "\"a\" + u @ TakesString\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "\"a\" + u @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "\"a\" + u @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "\"a\" + u @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "\"a\" + u @ new CtorObject\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "\"a\" + u @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "\"a\" + u @ index\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "\"a\" + u @ assign var\tERROR: cannot assign 'String' to variable 'x' (incompatible with prior 'Int32')",
        "\"a\" + u @ assign member\tERROR: cannot assign 'String' to 'Count' (type 'Int64')",
        "\"a\" + u @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "\"a\" + u @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "\"a\" + u @ return\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "u + \"a\" @ root\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "u + \"a\" @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "u + \"a\" @ TakesObject\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "u + \"a\" @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "u + \"a\" @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "u + \"a\" @ TakesString\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "u + \"a\" @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "u + \"a\" @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "u + \"a\" @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "u + \"a\" @ new CtorObject\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "u + \"a\" @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "u + \"a\" @ index\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "u + \"a\" @ assign var\tERROR: cannot assign 'String' to variable 'x' (incompatible with prior 'Int32')",
        "u + \"a\" @ assign member\tERROR: cannot assign 'String' to 'Count' (type 'Int64')",
        "u + \"a\" @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "u + \"a\" @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "u + \"a\" @ return\tTHROWS InvalidOperationException: VM compile rejected: cannot resolve the integer width of 'default' (Default).",
        "i32n << i64 @ root\tOK 12",
        "i32n << i64 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "i32n << i64 @ TakesObject\tOK 12",
        "i32n << i64 @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "i32n << i64 @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "i32n << i64 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "i32n << i64 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "i32n << i64 @ TakesNULong\tERROR: no matching member 'TakesNULong' on 'Calls' for invoke with 1 argument(s)",
        "i32n << i64 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "i32n << i64 @ new CtorObject\tOK Poly.Tests.Interpretation.Analysis.UndefinedArithmeticArgumentTests+CtorObject",
        "i32n << i64 @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "i32n << i64 @ index\tTHROWS IndexOutOfRangeException: Index was outside the bounds of the array.",
        "i32n << i64 @ assign var\tERROR: cannot assign 'Nullable`1' to variable 'x' (incompatible with prior 'Int32')",
        "i32n << i64 @ assign member\tOK 3",
        "i32n << i64 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "i32n << i64 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "i32n << i64 @ return\tOK 12",
        "u64n | i32 @ root\tTHROWS InvalidOperationException: VM compile rejected: no operator for 'UInt64' and 'Int32'.",
        "u64n | i32 @ TakesInt\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "u64n | i32 @ TakesObject\tTHROWS InvalidOperationException: VM compile rejected: no operator for 'UInt64' and 'Int32'.",
        "u64n | i32 @ TakesItem\tERROR: no matching member 'TakesItem' on 'Calls' for invoke with 1 argument(s)",
        "u64n | i32 @ TakesPair\tERROR: no matching member 'TakesPair' on 'Calls' for invoke with 1 argument(s)",
        "u64n | i32 @ TakesString\tERROR: no matching member 'TakesString' on 'Calls' for invoke with 1 argument(s)",
        "u64n | i32 @ TakesNInt\tERROR: no matching member 'TakesNInt' on 'Calls' for invoke with 1 argument(s)",
        "u64n | i32 @ TakesNULong\tTHROWS InvalidOperationException: VM compile rejected: no operator for 'UInt64' and 'Int32'.",
        "u64n | i32 @ new CtorInt\tERROR: no matching constructor for 'CtorInt' with 1 argument(s)",
        "u64n | i32 @ new CtorObject\tTHROWS InvalidOperationException: VM compile rejected: no operator for 'UInt64' and 'Int32'.",
        "u64n | i32 @ new CtorItem\tERROR: no matching constructor for 'CtorItem' with 1 argument(s)",
        "u64n | i32 @ index\tTHROWS InvalidOperationException: VM compile rejected: no operator for 'UInt64' and 'Int32'.",
        "u64n | i32 @ assign var\tERROR: cannot assign 'Nullable`1' to variable 'x' (incompatible with prior 'Int32')",
        "u64n | i32 @ assign member\tTHROWS InvalidOperationException: VM compile rejected: no operator for 'UInt64' and 'Int32'.",
        "u64n | i32 @ conditional arm\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "u64n | i32 @ coalesce\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "u64n | i32 @ return\tTHROWS InvalidOperationException: VM compile rejected: no operator for 'UInt64' and 'Int32'."
    ];
}
