using System.Globalization;
using System.Text;

using Poly.Interpretation;

namespace Poly.Tests.Interpretation;

/// <summary>
/// #374 S1: a seeded generator of well-typed AST programs for the Roslyn differential
/// (<see cref="AstParity.Compare"/>). The oracle is the VM against the Roslyn-compiled printed C#,
/// never Poly against itself: the generator only builds shapes, it never predicts a value.
/// <para>
/// <b>Determinism.</b> A pure function of <c>(seed, count, maxDepth)</c>. The RNG is a local SplitMix64
/// (not <see cref="Random"/>, whose sequence is a runtime detail), there is no clock, no GUID, no
/// ambient state. The same seed yields the same programs, text and labels.
/// </para>
/// <para>
/// <b>Bound.</b> <see cref="MaxDepth"/> caps the expression depth and <see cref="MaxCount"/> caps the
/// number of programs, so a run cannot hang or explode. A request above either is refused, not clamped.
/// </para>
/// <para>
/// <b>Shape.</b> Every operand bottoms out in a typed local assigned from a boundary constant, so
/// Roslyn cannot constant-fold the printed program (a folded <c>x / 0</c> or <c>int.MaxValue + 1</c> is
/// a compile error that says nothing about the writer). Constants appear only as the right operand
/// of an operation whose left operand is not constant, and a <c>/</c> or <c>%</c> divisor constant is
/// never zero. Runtime throws (divide by zero, overflow) remain reachable through the locals and are
/// outcomes both sides must share.
/// </para>
/// <para>
/// <b>Not generated</b> (see <see cref="NotGenerated"/>): shapes the harness cannot compare without a
/// semantic decision, or whose kind is already covered by the hand-written corpus.
/// </para>
/// </summary>
public static class AstShapeGenerator {
    public const int MaxDepth = 5;
    public const int MaxCount = 1_000;
    public const int DefaultDepth = 4;
    public const int DefaultCount = 120;
    public const ulong DefaultSeed = 374;

    /// <summary>Shapes this generator does not emit, and why. A recorded exclusion, not a pass.</summary>
    public static IReadOnlyList<(string Shape, string Why)> NotGenerated { get; } = [
        ("constant-only operands (constant folding)",
            "Roslyn folds and rejects constant div-by-zero / overflow at compile time; the VM throws at run time. That is a language-rule difference, not a writer defect, so operands come from locals"),
        ("shift, bitwise, PopCount, StridedSetBits, SuspendNode",
            "covered by hand-written corpus rows; shift-count masking and VM-only nodes need a per-kind semantic decision before they can be generated"),
        ("loops, try/catch, lambdas, member access, collections",
            "statement and heap shapes need a per-shape comparison rule (iteration bounds, ring state); AstParitySequence owns them"),
        ("string ==/!= with a concatenation operand",
            "FINDING: NotEqual(Add(s,\"c\"), Add(s,\"c\")) stack-overflows AbiEmitter at VM compile and kills the process; it cannot be a row, so comparison operands are concatenation-free until the VM is fixed"),
        ("string concatenation nested in a concatenation or beside another in a conditional",
            "FINDING: seed 374#103 (Cond(c, Cat(Cond(..Cat..), Cat(Cond,Cat)), ..)) stack-overflows AbiEmitter at VM compile; not minimised. String programs are a leaf, one concatenation of leaves, or a conditional of leaves/concatenations"),
        ("mixed-width comparison (int vs long vs double)",
            "needs a stated promotion rule; comparisons are generated same-type only"),
    ];

    /// <summary>One generated program: a label that names the seed and index, the node, and its canonical text.</summary>
    public sealed record GeneratedCase(string Label, Node Program, string Text);

    enum Ty { Int, Long, Double, Bool, Str }

    sealed record Gen(Node Node, string Text, bool IsConstant);

    static readonly int[] IntBoundaries = [0, 1, -1, 2, 3, 7, -7, 65535, int.MaxValue, int.MinValue];
    static readonly long[] LongBoundaries = [0L, 1L, -1L, 5L, 4294967296L, long.MaxValue, long.MinValue];
    static readonly double[] DoubleBoundaries = [0.0, 1.5, -2.25, 0.1, 1e308, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon];
    static readonly string[] StringBoundaries = ["", "a", "Ada", " ", "AB", "a b"];
    static readonly int[] SmallInts = [1, 2, 3, 5, -1, -4];

    /// <summary>The default set: <see cref="DefaultCount"/> programs from <paramref name="seed"/>.</summary>
    public static IReadOnlyList<GeneratedCase> Generate(ulong seed, int count = DefaultCount, int maxDepth = DefaultDepth) {
        if (count is < 0 or > MaxCount)
            throw new ArgumentOutOfRangeException(nameof(count), count, $"count is bounded to 0..{MaxCount}");
        if (maxDepth is < 0 or > MaxDepth)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, $"depth is bounded to 0..{MaxDepth}");

        var cases = new List<GeneratedCase>(count);
        for (var index = 0; index < count; index++) {
            // One independent stream per case, so a single case reproduces from (seed, index) alone.
            var rng = new SplitMix(Mix(seed, (ulong)index));
            cases.Add(BuildCase(seed, index, rng, maxDepth));
        }
        return cases;
    }

    /// <summary>The whole set as one text: what the determinism check diffs.</summary>
    public static string Render(IReadOnlyList<GeneratedCase> cases) =>
        string.Join("\n", cases.Select(c => $"{c.Label} :: {c.Text}"));

    /// <summary>
    /// Node equality is not structural, so repetition is read off the canonical text: every Name(...) segment
    /// other than a constant C(...) is a compound subtree, and the same segment twice is a repeat.
    /// </summary>
    public static bool RepeatsACompoundSubtree(string text) {
        var body = text[(text.LastIndexOf(';') + 1)..];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var starts = new Stack<int>();
        for (var i = 0; i < body.Length; i++) {
            if (body[i] == '(') {
                var s = i;
                while (s > 0 && (char.IsLetterOrDigit(body[s - 1]) || body[s - 1] is '<' or '>'))
                    s--;
                starts.Push(s);
            }
            else if (body[i] == ')') {
                var s = starts.Pop();
                var segment = body[s..(i + 1)];
                if (!segment.StartsWith("C(", StringComparison.Ordinal) && !seen.Add(segment))
                    return true;
            }
        }
        return false;
    }

    static GeneratedCase BuildCase(ulong seed, int index, SplitMix rng, int maxDepth) {
        var vars = new Dictionary<string, Variable> {
            ["i1"] = new("i1"),
            ["i2"] = new("i2"),
            ["l1"] = new("l1"),
            ["d1"] = new("d1"),
            ["b1"] = new("b1"),
            ["s1"] = new("s1"),
            ["s2"] = new("s2"),
        };
        var values = new (string Name, object Value)[] {
            ("i1", Pick(rng, IntBoundaries)), ("i2", Pick(rng, IntBoundaries)), ("l1", Pick(rng, LongBoundaries)),
            ("d1", Pick(rng, DoubleBoundaries)), ("b1", rng.Next(2) == 0), ("s1", Pick(rng, StringBoundaries)),
            ("s2", Pick(rng, StringBoundaries)),
        };
        var type = (Ty)rng.Next(5);
        var body = Expr(rng, vars, type, maxDepth);

        var statements = new List<Node>();
        var header = new StringBuilder();
        foreach (var (name, value) in values) {
            statements.Add(new Assignment(vars[name], new Constant(value)));
            header.Append(name).Append('=').Append(Literal(value)).Append(';');
        }
        statements.Add(body.Node);
        var program = new Block(statements, [.. vars.Values]);
        return new($"seed={seed}#{index}:{type}", program, header + body.Text);
    }

    static Gen Expr(SplitMix rng, Dictionary<string, Variable> vars, Ty type, int depth) {
        if (depth == 0 || rng.Next(5) == 0)
            return Leaf(rng, vars, type);
        return type switch {
            Ty.Int => Numeric(rng, vars, Ty.Int, depth),
            Ty.Long => Numeric(rng, vars, Ty.Long, depth),
            Ty.Double => Numeric(rng, vars, Ty.Double, depth),
            Ty.Bool => BoolExpr(rng, vars, depth),
            _ => StrExpr(rng, vars, depth),
        };
    }

    static Gen Leaf(SplitMix rng, Dictionary<string, Variable> vars, Ty type) {
        var name = type switch {
            Ty.Int => rng.Next(2) == 0 ? "i1" : "i2",
            Ty.Long => "l1",
            Ty.Double => "d1",
            Ty.Bool => "b1",
            _ => rng.Next(2) == 0 ? "s1" : "s2",
        };
        return new(vars[name], name, false);
    }

    static Gen Numeric(SplitMix rng, Dictionary<string, Variable> vars, Ty type, int depth) {
        switch (rng.Next(8)) {
            case 0: {
                    var operand = Expr(rng, vars, type, depth - 1);
                    return new(new UnaryMinus(operand.Node), $"Neg({operand.Text})", operand.IsConstant);
                }
            case 1: {
                    var test = BoolExpr(rng, vars, depth - 1);
                    var a = Expr(rng, vars, type, depth - 1);
                    var b = Expr(rng, vars, type, depth - 1);
                    return new(new Conditional(test.Node, a.Node, b.Node), $"Cond({test.Text},{a.Text},{b.Text})", false);
                }
            case 2 when type != Ty.Int: {
                    // widen: a narrower numeric operand cast up. Narrowing casts are not generated.
                    var from = type == Ty.Long ? Ty.Int : (Ty)rng.Next(2);
                    var operand = Expr(rng, vars, from, depth - 1);
                    return new(new TypeCast(operand.Node, TypeRef(type)), $"Cast<{type}>({operand.Text})", operand.IsConstant);
                }
            default: {
                    var (name, make, divisor) = rng.Next(5) switch {
                        0 => ("Add", (Func<Node, Node, Node>)((l, r) => new Add(l, r)), false),
                        1 => ("Sub", (l, r) => new Subtract(l, r), false),
                        2 => ("Mul", (l, r) => new Multiply(l, r), false),
                        3 => ("Div", (l, r) => new Divide(l, r), true),
                        _ => ("Mod", (l, r) => new Modulo(l, r), true),
                    };
                    var left = Expr(rng, vars, type, depth - 1);
                    Gen right;
                    if (divisor)
                        right = NonZeroDivisor(rng, vars, type, depth - 1, left.IsConstant);
                    else
                        right = rng.Next(3) == 0 && !left.IsConstant
                            ? ConstantOperand(rng, type)
                            : Expr(rng, vars, type, depth - 1);
                    return new(make(left.Node, right.Node), $"{name}({left.Text},{right.Text})", left.IsConstant && right.IsConstant);
                }
        }
    }

    // A divisor is either a local (zero is then a runtime outcome both sides must share) or a
    // non-zero constant. A constant divisor never meets a constant dividend.
    static Gen NonZeroDivisor(SplitMix rng, Dictionary<string, Variable> vars, Ty type, int depth, bool leftIsConstant) {
        if (!leftIsConstant && rng.Next(2) == 0)
            return ConstantOperand(rng, type);
        return Leaf(rng, vars, type);
    }

    static Gen ConstantOperand(SplitMix rng, Ty type) {
        object value = type switch {
            Ty.Int => (object)Pick(rng, SmallInts),
            Ty.Long => (object)(long)Pick(rng, SmallInts),
            Ty.Double => (object)((double)Pick(rng, SmallInts) + 0.5),
            _ => throw new InvalidOperationException($"no constant operand for {type}"),
        };
        return new(new Constant(value), $"C({Literal(value)})", true);
    }

    static Gen BoolExpr(SplitMix rng, Dictionary<string, Variable> vars, int depth) {
        if (depth == 0)
            return Leaf(rng, vars, Ty.Bool);
        switch (rng.Next(6)) {
            case 0: {
                    var operand = BoolExpr(rng, vars, depth - 1);
                    return new(new Not(operand.Node), $"Not({operand.Text})", false);
                }
            case 1: {
                    var l = BoolExpr(rng, vars, depth - 1);
                    var r = BoolExpr(rng, vars, depth - 1);
                    return new(new And(l.Node, r.Node), $"And({l.Text},{r.Text})", false);
                }
            case 2: {
                    var l = BoolExpr(rng, vars, depth - 1);
                    var r = BoolExpr(rng, vars, depth - 1);
                    return new(new Or(l.Node, r.Node), $"Or({l.Text},{r.Text})", false);
                }
            case 3: {
                    var l = StrOperand(rng, vars, depth - 1);
                    var r = StrOperand(rng, vars, depth - 1);
                    return rng.Next(2) == 0
                        ? new(new Equal(l.Node, r.Node), $"Eq({l.Text},{r.Text})", false)
                        : new(new NotEqual(l.Node, r.Node), $"Ne({l.Text},{r.Text})", false);
                }
            default: {
                    var operandType = (Ty)rng.Next(3);
                    var l = Expr(rng, vars, operandType, depth - 1);
                    var r = Expr(rng, vars, operandType, depth - 1);
                    var (name, make) = rng.Next(6) switch {
                        0 => ("Lt", (Func<Node, Node, Node>)((a, b) => new LessThan(a, b))),
                        1 => ("Le", (a, b) => new LessThanOrEqual(a, b)),
                        2 => ("Gt", (a, b) => new GreaterThan(a, b)),
                        3 => ("Ge", (a, b) => new GreaterThanOrEqual(a, b)),
                        4 => ("Eq", (a, b) => new Equal(a, b)),
                        _ => ("Ne", (a, b) => new NotEqual(a, b)),
                    };
                    return new(make(l.Node, r.Node), $"{name}<{operandType}>({l.Text},{r.Text})", false);
                }
        }
    }

    // Operand of a string ==/!=: no concatenation. NotEqual(Add(s,"c"), Add(s,"c")) stack-overflows the
    // VM compiler (AbiEmitter), which kills the test process, so the shape is excluded (NotGenerated).
    static Gen StrOperand(SplitMix rng, Dictionary<string, Variable> vars, int depth) {
        if (depth == 0 || rng.Next(2) == 0)
            return Leaf(rng, vars, Ty.Str);
        var test = BoolExpr(rng, vars, depth - 1);
        var a = Leaf(rng, vars, Ty.Str);
        var b = Leaf(rng, vars, Ty.Str);
        return new(new Conditional(test.Node, a.Node, b.Node), $"Cond({test.Text},{a.Text},{b.Text})", false);
    }

    // A string program is a leaf, one concatenation of two atoms, or a conditional of two atoms. A
    // concatenation nested in a concatenation or a conditional branch beside another one crashes the VM
    // compiler (NotGenerated), so atoms never nest.
    static Gen StrExpr(SplitMix rng, Dictionary<string, Variable> vars, int depth) {
        if (depth == 0 || rng.Next(3) == 0)
            return Leaf(rng, vars, Ty.Str);
        if (rng.Next(2) == 0) {
            var test = BoolExpr(rng, vars, depth - 1);
            var a = StrAtom(rng, vars);
            var b = StrAtom(rng, vars);
            return new(new Conditional(test.Node, a.Node, b.Node), $"Cond({test.Text},{a.Text},{b.Text})", false);
        }
        return StrConcat(rng, vars);
    }

    static Gen StrAtom(SplitMix rng, Dictionary<string, Variable> vars) =>
        rng.Next(2) == 0 ? Leaf(rng, vars, Ty.Str) : StrConcat(rng, vars);

    static Gen StrConcat(SplitMix rng, Dictionary<string, Variable> vars) {
        var left = Leaf(rng, vars, Ty.Str);
        var right = rng.Next(3) == 0
            ? new Gen(new Constant(Pick(rng, StringBoundaries)), "C(str)", true)
            : Leaf(rng, vars, Ty.Str);
        return new(new Add(left.Node, right.Node), $"Cat({left.Text},{right.Text})", false);
    }

    static Node TypeRef(Ty type) => type switch {
        Ty.Long => TypeReference.To<long>(),
        Ty.Double => TypeReference.To<double>(),
        _ => TypeReference.To<int>(),
    };

    static string Literal(object value) => value switch {
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    static T Pick<T>(SplitMix rng, T[] values) => values[rng.Next(values.Length)];

    static ulong Mix(ulong seed, ulong index) {
        var rng = new SplitMix(seed ^ (index * 0x9E3779B97F4A7C15UL));
        return rng.NextULong();
    }

    /// <summary>SplitMix64: fixed, portable, and independent of <see cref="Random"/>'s runtime-defined sequence.</summary>
    sealed class SplitMix(ulong state) {
        ulong _state = state;

        public ulong NextULong() {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int Next(int exclusiveMax) => (int)(NextULong() % (ulong)exclusiveMax);
    }
}