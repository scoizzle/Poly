using System.Globalization;
using System.Reflection;

using Microsoft.CSharp.RuntimeBinder;

using Poly.Interpretation;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The closed-generic probe the bridge grid calls (T-19 lock, amendment 1): a module lambda passed to
/// <c>Expression&lt;Func&lt;TA, TB, TR&gt;&gt;</c> is quoted by the lambda bridge; the probe compiles the quoted tree, runs it
/// on the operands and records the tree and the result through an <see cref="AsyncLocal{T}"/>, read host-side.
/// </summary>
public static class BridgeProbe<TA, TB, TR> {
    public static void Apply(System.Linq.Expressions.Expression<Func<TA, TB, TR>> f, TA a, TB b) =>
        BridgeProbeLog.Record(f, () => f.Compile()(a, b));
}

/// <summary>The arity-1 probe, for unary kinds and one-operand bodies.</summary>
public static class BridgeProbe<TA, TR> {
    public static void Apply(System.Linq.Expressions.Expression<Func<TA, TR>> f, TA a) =>
        BridgeProbeLog.Record(f, () => f.Compile()(a));
}

/// <summary>CLR methods with unsigned parameters, called from inside a bridged body.</summary>
public static class BridgeProbeTargets {
    public static ulong TakeUInt64(ulong value) => value;

    public static uint TakeUInt32(uint value) => value;
}

internal sealed record BridgeProbeRecord(System.Linq.Expressions.LambdaExpression Tree, object? Value, Exception? Error);

internal static class BridgeProbeLog {
    /// <summary>Per execution context: the VM calls the probe on the calling thread, so a cell reads its own record.</summary>
    private static readonly AsyncLocal<BridgeProbeRecord?> Last = new();

    public static void Record(System.Linq.Expressions.LambdaExpression tree, Func<object?> run) {
        object? value = null;
        Exception? error = null;
        try {
            value = run();
        }
        catch (Exception ex) {
            error = ex;
        }
        Last.Value = new(tree, value, error);
    }

    public static BridgeProbeRecord? Take() {
        var record = Last.Value;
        Last.Value = null;
        return record;
    }
}

/// <summary>An operand of the bridge grid: one of the operator ledger's CLR operands, optionally as <c>Nullable&lt;T&gt;</c>.</summary>
internal sealed record BridgeOperand(OracleOperand Operand, bool Nullable) {
    public Type Underlying => Operand.Clr!;

    public Type Clr => Nullable ? typeof(Nullable<>).MakeGenericType(Underlying) : Underlying;

    public string Name => Nullable ? Operand.Name + "?" : Operand.Name;
}

/// <summary>One value set of a cell: the root arguments fed to the probe.</summary>
internal sealed record BridgeSet(string Name, object? Left, object? Right);

/// <summary>
/// One cell of a bridge grid: a lambda body over the probe's parameters (<c>x</c>, and <c>y</c> for arity 2), the probe's
/// return type, and the value sets. <see cref="Key"/> is unique within <see cref="Grid"/>.
/// </summary>
internal sealed record BridgeCase(
    string Grid, string Key, OperatorKind? Op, BridgeOperand Left, BridgeOperand? Right, Type Return,
    Func<Parameter, Parameter?, Node> Body, IReadOnlyList<BridgeSet> Sets) {
    public string Line => $"{Grid} {Key}";
}

/// <summary>
/// What a cell did. <see cref="Stage"/> is <c>ran</c>, <c>analysis-rejected</c> (an analysis error inside the lambda body),
/// <c>main-rejected</c> (the main emitter refused the body while compiling the program's function table, before the bridge
/// quotes it), <c>bridge-rejected</c> (the bridge's own <c>VM compile rejected: lambda bridge</c> message), <c>emit-throws</c>
/// (any other exception while the bridge quotes the body, for example from <c>System.Linq.Expressions</c>) or
/// <c>harness-error</c> (an analysis error outside the body, or no probe record: the grid itself is wrong). <see cref="Replaced"/>: the analysis replaced the body node.
/// </summary>
internal sealed record BridgeOutcome(string Stage, bool Replaced, string? Type, IReadOnlyList<string> Runs, string? Message) {
    public bool Refused => Stage is "analysis-rejected" or "main-rejected" or "bridge-rejected" or "emit-throws";

    /// <summary>The outcome without the replacement flag: what a caller observes.</summary>
    public string Observed => Stage == "ran" ? $"ran; {Type}; {string.Join("; ", Runs)}" : $"{Stage}: {Message}";

    public string Render => Stage == "ran" && Replaced ? $"ran replaced; {Type}; {string.Join("; ", Runs)}" : Observed;
}

/// <summary>What C# says the cell is: defined with a type and the value per set, or undefined (C# rejects it).</summary>
internal sealed record BridgeExpectation(bool Defined, string? Type, IReadOnlyList<string> Runs) {
    public static readonly BridgeExpectation Undefined = new(false, null, []);

    public string Render => Defined ? $"ran; {Type}; {string.Join("; ", Runs)}" : "refused";
}

/// <summary>
/// The lambda bridge grid (T-19). The operands are the operator ledger's CLR numeric, char and Boolean operands
/// (<see cref="OperatorOracle.Operands"/>, with its Stress and Plain values), and the kinds are every
/// <see cref="OperatorKind"/> with a node. Each cell passes a module lambda to a closed <see cref="BridgeProbe{TA, TB, TR}"/>
/// so the VM's lambda bridge quotes it, and records the stage it reaches, the bridged body's static type (read from the
/// quoted tree, so <c>int?</c> and <c>int</c> differ) and the value per set. The expectation is C#'s, from the runtime
/// binder on the operand values (and C#'s lifting rules for <c>Nullable&lt;T&gt;</c>), cross-checked against the type
/// provider's <c>GetOperator</c>.
/// </summary>
internal static class BridgeGrid {
    private static readonly string[] GovernedNames =
        ["Number", "Int32", "Decimal", "Float", "Float32", "Int8", "Int16", "UInt8", "UInt16", "UInt32", "UInt64", "Char", "Boolean"];

    /// <summary>The one read of the ledger's operand set: its CLR numeric, char and Boolean operands.</summary>
    public static IReadOnlyList<OracleOperand> Operands { get; } =
        [.. OperatorOracle.Operands.Where(static o => GovernedNames.Contains(o.Name, StringComparer.Ordinal))];

    private static OracleOperand OperandOf(Type clr) => Operands.Single(o => o.Clr == clr);

    private static BridgeOperand P(OracleOperand o) => new(o, false);

    private static BridgeOperand N(OracleOperand o) => new(o, true);

    public static bool IsComparison(OperatorKind kind) =>
        kind is OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
            or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual;

    public static bool IsArithmetic(OperatorKind kind) =>
        kind is OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide or OperatorKind.Modulo;

    public static string TypeName(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    private static IReadOnlyList<BridgeSet> BinarySets(BridgeOperand left, BridgeOperand right) {
        var sets = new List<BridgeSet> {
            new("S1", left.Operand.Stress, right.Operand.Plain),
            new("S2", left.Operand.Plain, right.Operand.Stress)
        };
        if (left.Nullable)
            sets.Add(new("N1", null, right.Operand.Plain));
        if (right.Nullable)
            sets.Add(new("N2", left.Operand.Plain, null));
        if (left.Nullable && right.Nullable)
            sets.Add(new("N3", null, null));
        return sets;
    }

    private static IReadOnlyList<BridgeSet> UnarySets(BridgeOperand operand) {
        var sets = new List<BridgeSet> { new("S1", operand.Operand.Stress, null), new("S2", operand.Operand.Plain, null) };
        if (operand.Nullable)
            sets.Add(new("N1", null, null));
        return sets;
    }

    private static BridgeCase OperatorCase(string grid, OperatorKind kind, BridgeOperand left, BridgeOperand? right) =>
        new(grid, $"{left.Name} {kind} {right?.Name ?? "-"}", kind, left, right, typeof(object),
            (x, y) => ConstantFoldGrid.Build(kind, x, y), right is null ? UnarySets(left) : BinarySets(left, right));

    private static IEnumerable<OperatorKind> Kinds() =>
        Enum.GetValues<OperatorKind>().Where(static k => k is not OperatorKind.UnaryPlus);

    /// <summary>Every kind over every ordered pair of non-nullable operands (unary kinds over each operand).</summary>
    public static IEnumerable<BridgeCase> OperatorCases() {
        foreach (var kind in Kinds()) {
            foreach (var left in Operands) {
                if (OperatorShape.ArityOf(kind) == 1) {
                    yield return OperatorCase("operator", kind, P(left), null);
                    continue;
                }
                foreach (var right in Operands)
                    yield return OperatorCase("operator", kind, P(left), P(right));
            }
        }
    }

    /// <summary>
    /// The nullable shapes, one by one: for each operand T and kind, <c>(T?, T)</c>, <c>(T, T?)</c>, <c>(T?, T?)</c> and
    /// <c>(T?, U)</c> for every other operand U; unary kinds over <c>T?</c>.
    /// </summary>
    public static IEnumerable<BridgeCase> NullableCases() {
        foreach (var kind in Kinds()) {
            foreach (var t in Operands) {
                if (OperatorShape.ArityOf(kind) == 1) {
                    yield return OperatorCase("nullable", kind, N(t), null);
                    continue;
                }
                yield return OperatorCase("nullable", kind, N(t), P(t));
                yield return OperatorCase("nullable", kind, P(t), N(t));
                yield return OperatorCase("nullable", kind, N(t), N(t));
                foreach (var u in Operands) {
                    if (u != t)
                        yield return OperatorCase("nullable", kind, N(t), P(u));
                }
            }
        }
    }

    /// <summary>
    /// A conditional over two arms, <c>x == x ? x : y</c> (the condition is not a constant, so nothing prunes it), for every
    /// ordered pair of operands as <c>(T, U)</c>, <c>(T?, U)</c>, <c>(T, U?)</c> and <c>(T?, U?)</c>.
    /// </summary>
    public static IEnumerable<BridgeCase> ConditionalCases() {
        foreach (var (ln, rn) in new[] { (false, false), (true, false), (false, true), (true, true) }) {
            foreach (var left in Operands) {
                foreach (var right in Operands) {
                    var (l, r) = (new BridgeOperand(left, ln), new BridgeOperand(right, rn));
                    yield return new("conditional", $"{l.Name} ? {r.Name}", null, l, r, typeof(object),
                        static (x, y) => new Conditional(new Equal(x, x), x, y!), BinarySets(l, r));
                }
            }
        }
    }

    /// <summary>
    /// The return coercion: the body <c>x</c> of type S returned into <c>Func&lt;S, T&gt;</c>, for every ordered pair as
    /// <c>S → T</c>, <c>S → T?</c>, <c>S? → T?</c> and <c>S? → T</c>.
    /// </summary>
    public static IEnumerable<BridgeCase> ReturnCases() {
        foreach (var (sn, tn) in new[] { (false, false), (false, true), (true, true), (true, false) }) {
            foreach (var source in Operands) {
                foreach (var target in Operands) {
                    var (s, t) = (new BridgeOperand(source, sn), new BridgeOperand(target, tn));
                    yield return new("return", $"{s.Name} -> {t.Name}", null, s, null, t.Clr,
                        static (x, _) => x, UnarySets(s));
                }
            }
        }
    }

    private static readonly OperatorKind[] ConstantRuleKinds = [
        OperatorKind.Add, OperatorKind.Subtract, OperatorKind.Multiply, OperatorKind.Divide, OperatorKind.Modulo,
        OperatorKind.Equal, OperatorKind.NotEqual, OperatorKind.LessThan, OperatorKind.LessThanOrEqual,
        OperatorKind.GreaterThan, OperatorKind.GreaterThanOrEqual
    ];

    private static readonly object[] SignedConstants = [(sbyte)1, (short)1, 1, 1L, (sbyte)-1, (short)-1, -1, -1L];

    private static string ConstantName(object value) => $"{value.GetType().Name}({ConstantFoldGrid.Format(value)})";

    /// <summary>
    /// The owner's narrow exception (#679 6842272727): a signed integral constant beside a <c>ulong</c> parameter, a
    /// <c>ulong?</c> parameter or a <c>Constant(ulong)</c>, on either side, for the five arithmetic and six comparison kinds.
    /// </summary>
    public static IEnumerable<BridgeCase> UInt64ConstantCases() {
        var uint64 = OperandOf(typeof(ulong));
        foreach (var kind in ConstantRuleKinds) {
            foreach (var constant in SignedConstants) {
                foreach (var other in new[] { "x", "x?", "5UL" }) {
                    foreach (var constantOnLeft in new[] { false, true }) {
                        var operand = new BridgeOperand(uint64, other == "x?");
                        Node Side(Parameter x) => other == "5UL" ? new Constant(5UL) : x;
                        var key = constantOnLeft
                            ? $"{ConstantName(constant)} {kind} {other}"
                            : $"{other} {kind} {ConstantName(constant)}";
                        yield return new("uint64-constant", key, kind, operand, null, typeof(object),
                            (x, _) => constantOnLeft
                                ? ConstantFoldGrid.Build(kind, new Constant(constant), Side(x))
                                : ConstantFoldGrid.Build(kind, Side(x), new Constant(constant)),
                            UnarySets(operand));
                    }
                }
            }
        }
    }

    /// <summary>
    /// An <c>int</c> constant beside a <c>uint</c> parameter, every binary kind, both sides. C# converts a non-negative
    /// constant to <c>uint</c> (spec 10.2.11); the owner's exception does not cover <c>uint</c> (#679 6842272727).
    /// </summary>
    public static IEnumerable<BridgeCase> UInt32ConstantCases() {
        var uint32 = P(OperandOf(typeof(uint)));
        foreach (var kind in Kinds().Where(static k => OperatorShape.ArityOf(k) == 2 && k is not (OperatorKind.And or OperatorKind.Or))) {
            foreach (var constant in new object[] { 1, -1 }) {
                foreach (var constantOnLeft in new[] { false, true }) {
                    var key = constantOnLeft ? $"{ConstantName(constant)} {kind} x" : $"x {kind} {ConstantName(constant)}";
                    yield return new("uint32-constant", key, kind, uint32, null, typeof(object),
                        (x, _) => constantOnLeft
                            ? ConstantFoldGrid.Build(kind, new Constant(constant), x)
                            : ConstantFoldGrid.Build(kind, x, new Constant(constant)),
                        UnarySets(uint32));
                }
            }
        }
    }

    private static readonly Type[] UnsignedTargets = [typeof(byte), typeof(ushort), typeof(uint), typeof(ulong), typeof(sbyte), typeof(short), typeof(char)];

    /// <summary>
    /// A non-negative signed constant where a CLR parameter of a narrower or unsigned type wants it: as the lambda's
    /// return (<c>Func&lt;int, T&gt;</c>, body the constant) and as the argument of a CLR method taking <c>uint</c> or
    /// <c>ulong</c>. C# converts each in-range constant implicitly (spec 10.2.11).
    /// </summary>
    public static IEnumerable<BridgeCase> ConstantParameterCases() {
        var int32 = P(OperandOf(typeof(int)));
        foreach (var constant in new object[] { (sbyte)1, (short)1, 1, 1L }) {
            foreach (var target in UnsignedTargets) {
                yield return new("constant-parameter", $"return {ConstantName(constant)} -> {target.Name}", null, int32, null, target,
                    (_, _) => new Constant(constant), [new("S1", 7, null)]);
            }
            foreach (var method in new[] { nameof(BridgeProbeTargets.TakeUInt32), nameof(BridgeProbeTargets.TakeUInt64) }) {
                yield return new("constant-parameter", $"{method}({ConstantName(constant)})", null, int32, null, typeof(object),
                    (_, _) => new Invoke(new Member(new ClrTypeReference(typeof(BridgeProbeTargets)), method), new Constant(constant)),
                    [new("S1", 7, null)]);
            }
        }
    }

    public static IReadOnlyList<BridgeCase> Cases { get; } = [
        .. OperatorCases(), .. NullableCases(), .. ConditionalCases(), .. ReturnCases(),
        .. UInt64ConstantCases(), .. UInt32ConstantCases(), .. ConstantParameterCases()
    ];

    /// <summary>Every cell's outcome, run once under the invariant culture (values are formatted as the ledger formats them).</summary>
    public static readonly Lazy<IReadOnlyDictionary<BridgeCase, BridgeOutcome>> Outcomes = new(() => {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var outcomes = new Dictionary<BridgeCase, BridgeOutcome>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var c in Cases)
                outcomes[c] = Evaluate(c);
            return outcomes;
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    });

    public static BridgeOutcome Outcome(BridgeCase c) => Outcomes.Value[c];

    private static bool Contains(Node root, Node target) {
        if (ReferenceEquals(root, target))
            return true;
        foreach (var child in root.Children) {
            if (child is not null && Contains(child, target))
                return true;
        }
        return false;
    }

    /// <summary>Runs one cell through analysis, the emitter (which quotes the lambda) and the probe, once per value set.</summary>
    public static BridgeOutcome Evaluate(BridgeCase c) {
        var a = new Parameter("a", new ClrTypeReference(c.Left.Clr));
        var b = c.Right is null ? null : new Parameter("b", new ClrTypeReference(c.Right.Clr));
        var x = new Parameter("x", new ClrTypeReference(c.Left.Clr));
        var y = c.Right is null ? null : new Parameter("y", new ClrTypeReference(c.Right.Clr));
        var body = c.Body(x, y);
        var lambda = new Lambda(y is null ? [x] : [x, y], body);
        var probe = y is null
            ? typeof(BridgeProbe<,>).MakeGenericType(c.Left.Clr, c.Return)
            : typeof(BridgeProbe<,,>).MakeGenericType(c.Left.Clr, c.Right!.Clr, c.Return);
        var apply = new Member(new ClrTypeReference(probe), "Apply");
        Node call = b is null ? new Invoke(apply, lambda, a) : new Invoke(apply, lambda, a, b);

        AnalysisResult analysis;
        try {
            analysis = Interpreter.Analyze(call, ClrTypeDefinitionRegistry.Shared);
        }
        catch (Exception ex) {
            return new("harness-error", false, null, [], $"analysis throws {ex.GetType().Name}: {ex.Message}");
        }
        var replaced = analysis.GetNodeReplacement(body) is not null;
        if (analysis.Diagnostics.FirstOrDefault(static d => d.Severity == DiagnosticSeverity.Error) is { } error) {
            return error.Node is { } at && Contains(body, at)
                ? new("analysis-rejected", replaced, null, [], error.Message)
                : new("harness-error", replaced, null, [], $"analysis error outside the body: {error.Message}");
        }

        Poly.Interpretation.Vm.VmProgram program;
        try {
            program = Interpreter.Compile(call, analysis);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("VM compile rejected: lambda bridge", StringComparison.Ordinal)) {
            return new("bridge-rejected", replaced, null, [], ex.Message);
        }
        catch (Exception ex) when (!InTheBridge(ex)) {
            return new("main-rejected", replaced, null, [], $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex) {
            return new("emit-throws", replaced, null, [], $"{ex.GetType().Name}: {ex.Message}");
        }

        string? type = null;
        var runs = new List<string>();
        foreach (var set in c.Sets) {
            BridgeProbeLog.Take();
            try {
                var args = b is null ? new[] { set.Left } : [set.Left, set.Right];
                using var exec = Interpreter.Execute(program, args);
            }
            catch (Exception ex) {
                runs.Add($"{set.Name}=throws {Unwrap(ex).GetType().Name}");
                continue;
            }
            if (BridgeProbeLog.Take() is not { } record)
                return new("harness-error", replaced, null, [], $"{set.Name}: the probe recorded nothing");
            type ??= TypeName(BodyType(record.Tree));
            runs.Add(record.Error is { } runError
                ? $"{set.Name}=throws {Unwrap(runError).GetType().Name}"
                : $"{set.Name}={ConstantFoldGrid.Format(record.Value)}");
        }
        return new("ran", replaced, type, runs, null);
    }

    /// <summary>
    /// Whether an emit exception came from the bridge quoting the body (<c>BuildExpressionArgument</c> is on its stack).
    /// The emitter also compiles every lambda of the program into its function table with the main emitter, before the
    /// call that quotes it (<c>AbiEmitter.Build</c>, <c>CollectLambdas</c>); an exception there means the bridge never decided.
    /// </summary>
    private static bool InTheBridge(Exception ex) =>
        ex.StackTrace?.Contains("AbiEmitter.BuildExpressionArgument", StringComparison.Ordinal) == true;

    private static Exception Unwrap(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : ex;

    /// <summary>
    /// The bridged body's static type: with an <c>object</c> return the bridge boxes the body, so it is the boxed operand's
    /// type; with a typed return it is the coerced body.
    /// </summary>
    private static Type BodyType(System.Linq.Expressions.LambdaExpression tree) =>
        tree.ReturnType == typeof(object)
            && tree.Body is System.Linq.Expressions.UnaryExpression { NodeType: System.Linq.Expressions.ExpressionType.Convert } box
            ? box.Operand.Type
            : tree.Body.Type;

    // ---- the expectation: C#'s answer ----

    public static readonly Lazy<IReadOnlyDictionary<BridgeCase, BridgeExpectation>> Expectations = new(() => {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var expectations = new Dictionary<BridgeCase, BridgeExpectation>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var c in Cases)
                expectations[c] = Expect(c);
            return expectations;
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    });

    public static BridgeExpectation Expectation(BridgeCase c) => Expectations.Value[c];

    private static BridgeExpectation Expect(BridgeCase c) => c.Grid switch {
        "operator" or "nullable" => ExpectOperator(c),
        "conditional" => ExpectConditional(c),
        "return" => ExpectReturn(c),
        "uint64-constant" => ExpectUInt64Constant(c),
        "uint32-constant" => ExpectUInt32Constant(c, constantConversion: true),
        "constant-parameter" => ExpectConstantParameter(c, constantConversion: true),
        _ => throw new ArgumentOutOfRangeException(nameof(c), c.Grid, "unknown grid")
    };

    /// <summary>The runtime binder's value for the operator over two non-null operand values; throws when C# has no operator.</summary>
    public static object? Binder(OperatorKind kind, object? left, object? right) {
        dynamic x = left!;
        dynamic? y = right;
        return kind switch {
            OperatorKind.Add => x + y,
            OperatorKind.Subtract => x - y,
            OperatorKind.Multiply => x * y,
            OperatorKind.Divide => x / y,
            OperatorKind.Modulo => x % y,
            OperatorKind.Equal => x == y,
            OperatorKind.NotEqual => x != y,
            OperatorKind.LessThan => x < y,
            OperatorKind.LessThanOrEqual => x <= y,
            OperatorKind.GreaterThan => x > y,
            OperatorKind.GreaterThanOrEqual => x >= y,
            OperatorKind.And => x && y,
            OperatorKind.Or => x || y,
            OperatorKind.Not => !x,
            OperatorKind.BitwiseAnd => x & y,
            OperatorKind.BitwiseOr => x | y,
            OperatorKind.BitwiseXor => x ^ y,
            OperatorKind.ShiftLeft => x << y,
            OperatorKind.ShiftRight => x >> y,
            OperatorKind.UnaryMinus => -x,
            OperatorKind.BitwiseNot => ~x,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>C#'s lifted operator when an operand is null: equality compares nullness, ordering is false, Boolean <c>&amp;</c> and <c>|</c> are three-valued, the rest are null.</summary>
    private static object? LiftedNull(OperatorKind kind, object? left, object? right) => kind switch {
        OperatorKind.Equal => left is null && right is null,
        OperatorKind.NotEqual => !(left is null && right is null),
        OperatorKind.LessThan or OperatorKind.LessThanOrEqual or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual => false,
        OperatorKind.BitwiseAnd when left is null or bool && right is null or bool => (bool?)left & (bool?)right,
        OperatorKind.BitwiseOr when left is null or bool && right is null or bool => (bool?)left | (bool?)right,
        _ => null
    };

    private static string Run(string set, Func<object?> value) {
        try {
            return $"{set}={ConstantFoldGrid.Format(value())}";
        }
        catch (Exception ex) when (ex is not RuntimeBinderException) {
            return $"{set}=throws {Unwrap(ex).GetType().Name}";
        }
    }

    /// <summary>
    /// C#'s operator: the binder decides the operator and its type from the operand types (it never sees a constant);
    /// a nullable operand lifts it (C# 12.4.8): comparisons stay <c>bool</c>, the rest become <c>R?</c>. <c>&amp;&amp;</c>
    /// and <c>||</c> have no lifted form.
    /// </summary>
    private static BridgeExpectation ExpectOperator(BridgeCase c) {
        var kind = c.Op!.Value;
        var lifted = c.Left.Nullable || c.Right is { Nullable: true };
        // The binder short-circuits `false && y` without looking at y, so it cannot judge the pair: C#'s && and || take
        // two bool operands (C# 12.14), with no lifted form.
        if (kind is OperatorKind.And or OperatorKind.Or
            && (lifted || c.Left.Underlying != typeof(bool) || c.Right!.Underlying != typeof(bool)))
            return BridgeExpectation.Undefined;
        object? probe;
        try {
            probe = Binder(kind, c.Left.Operand.Plain, c.Right?.Operand.Plain);
        }
        catch (RuntimeBinderException) {
            return BridgeExpectation.Undefined;
        }
        var result = probe!.GetType();
        var type = lifted && !IsComparison(kind) ? typeof(Nullable<>).MakeGenericType(result) : result;
        var runs = c.Sets.Select(set => set.Left is null || (c.Right is not null && set.Right is null)
            ? Run(set.Name, () => LiftedNull(kind, set.Left, set.Right))
            : Run(set.Name, () => Binder(kind, set.Left, set.Right))).ToArray();
        return new(true, TypeName(type), runs);
    }

    private static T ImplicitTo<T>(dynamic value) => value;

    private static readonly MethodInfo ImplicitToInfo =
        typeof(BridgeGrid).GetMethod(nameof(ImplicitTo), BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>The binder's implicit conversion of a value to <paramref name="target"/>; throws when C# has none for the value's type.</summary>
    private static object? Implicit(object? value, Type target) {
        try {
            return ImplicitToInfo.MakeGenericMethod(target).Invoke(null, [value]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            throw ex.InnerException;
        }
    }

    /// <summary>
    /// C#'s implicit conversion between two grid types (C# 10.2): identity; between the underlying types as the binder
    /// converts a value (it never sees a constant); lifted to <c>T?</c> from <c>S</c> or <c>S?</c> (C# 10.6.1); never
    /// from <c>S?</c> to a non-nullable type.
    /// </summary>
    public static bool ImplicitlyConverts(BridgeOperand from, BridgeOperand to) {
        if (from.Clr == to.Clr)
            return true;
        if (from.Nullable && !to.Nullable)
            return false;
        if (from.Underlying == to.Underlying)
            return true;
        try {
            Implicit(from.Operand.Plain, to.Underlying);
            return true;
        }
        catch (RuntimeBinderException) {
            return false;
        }
    }

    /// <summary>C#'s conditional typing (C# 12.18): the arm type the other arm implicitly converts to, when exactly one does.</summary>
    private static BridgeExpectation ExpectConditional(BridgeCase c) {
        var (x, y) = (c.Left, c.Right!);
        Type natural;
        if (x.Clr == y.Clr)
            natural = x.Clr;
        else if (ImplicitlyConverts(y, x) && !ImplicitlyConverts(x, y))
            natural = x.Clr;
        else if (ImplicitlyConverts(x, y) && !ImplicitlyConverts(y, x))
            natural = y.Clr;
        else
            return BridgeExpectation.Undefined;
        var runs = c.Sets.Select(set => Run(set.Name, () => set.Left is null ? null : Implicit(set.Left, natural))).ToArray();
        return new(true, TypeName(natural), runs);
    }

    private static BridgeExpectation ExpectReturn(BridgeCase c) {
        var target = new BridgeOperand(OperandOf(Nullable.GetUnderlyingType(c.Return) ?? c.Return), Nullable.GetUnderlyingType(c.Return) is not null);
        if (!ImplicitlyConverts(c.Left, target))
            return BridgeExpectation.Undefined;
        var runs = c.Sets.Select(set => Run(set.Name, () => set.Left is null ? null : Implicit(set.Left, c.Return))).ToArray();
        return new(true, TypeName(c.Return), runs);
    }

    /// <summary>The constant the cell's body states, read back from the body (left or right of the operator).</summary>
    private static object SignedConstantOf(BridgeCase c) {
        var body = c.Body(new Parameter("x"), null);
        return body.Children.OfType<Constant>().Select(static k => k.Value!).First(static v => v is not ulong);
    }

    private static object? Operand(BridgeCase c, BridgeSet set) => c.Key.Contains("5UL", StringComparison.Ordinal) ? 5UL : set.Left;

    /// <summary>
    /// C# 10.2.11: a non-negative signed constant converts to <c>ulong</c>, so the operator is <c>ulong</c>'s (lifted beside
    /// <c>ulong?</c>); a negative constant converts to nothing and C# has no operator for the pair.
    /// </summary>
    private static BridgeExpectation ExpectUInt64Constant(BridgeCase c) {
        var kind = c.Op!.Value;
        var constant = SignedConstantOf(c);
        if (Convert.ToInt64(constant, CultureInfo.InvariantCulture) < 0)
            return BridgeExpectation.Undefined;
        var asUInt64 = Convert.ToUInt64(constant, CultureInfo.InvariantCulture);
        var constantOnLeft = !c.Key.StartsWith("x", StringComparison.Ordinal) && !c.Key.StartsWith("5UL", StringComparison.Ordinal);
        var result = IsComparison(kind) ? typeof(bool) : typeof(ulong);
        var type = c.Left.Nullable && !c.Key.Contains("5UL", StringComparison.Ordinal) && !IsComparison(kind) ? typeof(ulong?) : result;
        var runs = c.Sets.Select(set => {
            var other = Operand(c, set);
            var (l, r) = constantOnLeft ? ((object?)asUInt64, other) : (other, asUInt64);
            return other is null ? Run(set.Name, () => LiftedNull(kind, l, r)) : Run(set.Name, () => Binder(kind, l, r));
        }).ToArray();
        return new(true, TypeName(type), runs);
    }

    /// <summary>
    /// C# 10.2.11 beside <c>uint</c>: a non-negative <c>int</c> constant converts to <c>uint</c>, so the operator is
    /// <c>uint</c>'s; a negative one does not, and binary promotion applies. A shift count is an <c>int</c> anyway.
    /// <paramref name="constantConversion"/> false is the provider's answer, which models no constant (T-26).
    /// </summary>
    private static BridgeExpectation ExpectUInt32Constant(BridgeCase c, bool constantConversion) {
        var kind = c.Op!.Value;
        var constant = (int)SignedConstantOf(c);
        var constantOnLeft = !c.Key.StartsWith("x", StringComparison.Ordinal);
        object operand = constantConversion && constant >= 0 && kind is not (OperatorKind.ShiftLeft or OperatorKind.ShiftRight)
            ? (uint)constant
            : constant;
        object? probe;
        try {
            probe = constantOnLeft ? Binder(kind, operand, 7u) : Binder(kind, 7u, operand);
        }
        catch (RuntimeBinderException) {
            return BridgeExpectation.Undefined;
        }
        var runs = c.Sets.Select(set => Run(set.Name, () => constantOnLeft ? Binder(kind, operand, set.Left) : Binder(kind, set.Left, operand))).ToArray();
        return new(true, TypeName(probe!.GetType()), runs);
    }

    private static readonly Type[] IntConstantTargets = [typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(uint), typeof(ulong)];

    /// <summary>
    /// Whether C# converts the constant 1 of the constant's type to <paramref name="target"/>: an implicit numeric conversion
    /// (as the binder converts a value), or, with <paramref name="constantConversion"/>, C# 10.2.11: an <c>int</c> constant
    /// in range to <c>sbyte</c>, <c>byte</c>, <c>short</c>, <c>ushort</c>, <c>uint</c> or <c>ulong</c>, a non-negative
    /// <c>long</c> constant to <c>ulong</c>.
    /// </summary>
    private static bool ConvertsConstant(object constant, Type target, bool constantConversion) {
        if (constantConversion && ((constant is int && IntConstantTargets.Contains(target)) || (constant is long && target == typeof(ulong))))
            return true;
        try {
            Implicit(constant, target);
            return true;
        }
        catch (RuntimeBinderException) {
            return false;
        }
    }

    private static BridgeExpectation ExpectConstantParameter(BridgeCase c, bool constantConversion) {
        var constant = ((Constant)(c.Body(new Parameter("x"), null) switch {
            Constant k => k,
            Invoke invoke => invoke.Arguments[0],
            var other => throw new InvalidOperationException($"unexpected body {other}")
        })).Value!;
        if (c.Key.StartsWith("return", StringComparison.Ordinal)) {
            return ConvertsConstant(constant, c.Return, constantConversion)
                ? new(true, TypeName(c.Return), [$"S1={ConstantFoldGrid.Format(Convert.ChangeType(1, c.Return, CultureInfo.InvariantCulture))}"])
                : BridgeExpectation.Undefined;
        }
        var parameter = c.Key.StartsWith(nameof(BridgeProbeTargets.TakeUInt32), StringComparison.Ordinal) ? typeof(uint) : typeof(ulong);
        return ConvertsConstant(constant, parameter, constantConversion) ? new(true, parameter.Name, ["S1=1"]) : BridgeExpectation.Undefined;
    }

    /// <summary>
    /// The provider's answer, which T-19's bridge gives: C#'s, except where only C#'s constant-expression conversion
    /// (spec 10.2.11) makes the answer, which the provider does not model (beside <c>uint</c>, into an unsigned or narrower
    /// parameter). Those cells are T-26's (lock amendment 4). The owner's <c>ulong</c> exception is modeled (T-18's helper).
    /// </summary>
    public static readonly Lazy<IReadOnlyDictionary<BridgeCase, BridgeExpectation>> ProviderExpectations = new(() => {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var expectations = new Dictionary<BridgeCase, BridgeExpectation>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var c in Cases) {
                expectations[c] = c.Grid switch {
                    "uint32-constant" => ExpectUInt32Constant(c, constantConversion: false),
                    "constant-parameter" => ExpectConstantParameter(c, constantConversion: false),
                    _ => Expectation(c)
                };
            }
            return expectations;
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    });

    public static BridgeExpectation ProviderExpectation(BridgeCase c) => ProviderExpectations.Value[c];

    /// <summary>The provider's operator for a plain operator cell: its result type, or null when it states none.</summary>
    public static Type? ProviderResult(BridgeCase c) {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var left = registry.GetTypeDefinition(c.Left.Underlying)!;
        var right = c.Right is null ? null : registry.GetTypeDefinition(c.Right.Underlying)!;
        return left.GetOperator(c.Op!.Value, right) is { } resolution && resolution.Result.TryGetRuntimeType(out var type) ? type : null;
    }

    /// <summary>
    /// Whether an answer is the expectation: the same type and values, or a positioned refusal (an analysis error or the
    /// bridge's own message, or the main emitter's, which refuses the body before the bridge sees it) where the expectation has no operator.
    /// </summary>
    public static bool Matches(string observed, BridgeExpectation expected) =>
        expected.Defined
            ? observed == expected.Render
            : observed.StartsWith("analysis-rejected", StringComparison.Ordinal) || observed.StartsWith("main-rejected", StringComparison.Ordinal)
                || observed.StartsWith("bridge-rejected", StringComparison.Ordinal);

    /// <summary>Whether the cell's answer is the provider's (<see cref="ProviderExpectation"/>).</summary>
    public static bool Agrees(BridgeCase c) => Matches(Outcome(c).Observed, ProviderExpectation(c));

    // ---- the main emitter: the operator ledger's ABI column ----

    private static readonly Lazy<IReadOnlyDictionary<string, OracleCell>> LedgerCells = new(static () =>
        OperatorOracle.Run.Value.Cells.ToDictionary(static cell => $"{cell.Left} {cell.Op} {cell.Right ?? "-"}", StringComparer.Ordinal));

    public static OracleCell LedgerCell(BridgeCase c) => LedgerCells.Value[c.Key];

    /// <summary>What the main emitter answers for the same operator over two typed parameters: its type and values, or refused.</summary>
    public static string MainAnswer(BridgeCase c) {
        var abi = LedgerCell(c).Abi;
        return abi.StartsWith("not run", StringComparison.Ordinal) || abi.StartsWith("emit throws", StringComparison.Ordinal) ? "refused" : abi;
    }

    /// <summary>A bridge answer in the ledger's form: <c>Type; S1=…; S2=…</c>, or refused.</summary>
    public static string BridgeAnswer(string observed) => observed.StartsWith("ran; ", StringComparison.Ordinal) ? observed["ran; ".Length..] : "refused";

    /// <summary>Whether the ledger records a VM divergence from the tree for the cell (any <c>AbiDivergesFromTree</c> reason).</summary>
    public static bool LedgerDiverges(BridgeCase c) => LedgerCell(c).Divergences.Any(static d => d.Kind == OracleKind.AbiDivergesFromTree);

    /// <summary>Set 1 (lock amendment 2): the plain operator cells where the bridge's answer is not the main emitter's.</summary>
    public static IReadOnlyList<BridgeCase> Set1() =>
        [.. Cases.Where(static c => c.Grid == "operator" && BridgeAnswer(Outcome(c).Observed) != MainAnswer(c))];

    /// <summary>Set 2 (lock amendment 2): the set-1 cells the bridge already answered like the provider on master.</summary>
    public static IReadOnlyList<BridgeCase> Set2() => [.. Set1().Where(MasterAgrees)];

    /// <summary>Whether the bridge decides the cell now: neither the analysis nor the main emitter refused it first.</summary>
    public static bool BridgeDecides(BridgeCase c) => Outcome(c).Stage is not ("analysis-rejected" or "main-rejected");

    /// <summary>
    /// The row that owns a cell the provider defines but the bridge never decides: the main emitter compiles every lambda of
    /// the program into its function table before the call quotes it (<c>AbiEmitter.Build</c>) and refuses this body (T-21);
    /// the lowered analysis rejects the lifted <c>!</c> on <c>bool?</c> (T-28). Null: no owner.
    /// </summary>
    public static string? OwnerOfUndecided(BridgeCase c) => Outcome(c).Stage switch {
        "main-rejected" => "T-21 (the main emitter refuses the body while compiling the function table, before the bridge quotes it)",
        "analysis-rejected" when c.Op is OperatorKind.Not && c.Left.Nullable => "T-28 (the lowered analysis rejects the lifted ! on bool?)",
        _ => null
    };

    // ---- the master snapshot ----

    public static string MasterAnswersPath => Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "Interpretation", "LambdaBridgeMasterAnswers.txt");

    public static IEnumerable<string> Lines() => Cases.Select(static c => $"{c.Line} = {Outcome(c).Render}");

    public static readonly Lazy<IReadOnlyDictionary<string, string>> MasterAnswers = new(static () =>
        File.ReadAllLines(MasterAnswersPath).ToDictionary(
            static line => line[..line.IndexOf(" = ", StringComparison.Ordinal)],
            static line => line[(line.IndexOf(" = ", StringComparison.Ordinal) + 3)..],
            StringComparer.Ordinal));

    /// <summary>The cell's answer on master (fd927c77), as persisted.</summary>
    public static string MasterAnswer(BridgeCase c) => MasterAnswers.Value[c.Line];

    private static string ObservedOf(string render) => render.StartsWith("ran replaced; ", StringComparison.Ordinal) ? "ran; " + render["ran replaced; ".Length..] : render;

    /// <summary>
    /// An answer for the change set: a run keeps its type and values, a positioned refusal (analysis, main emitter or the
    /// bridge's own message) is <c>refused</c> whatever its text, and a raw exception stays itself.
    /// </summary>
    public static string Normalized(string render) =>
        render.StartsWith("ran", StringComparison.Ordinal) ? ObservedOf(render)
        : render.StartsWith("emit-throws", StringComparison.Ordinal) ? render
        : "refused";

    /// <summary>The master answer without the replacement flag.</summary>
    public static string MasterObserved(BridgeCase c) => ObservedOf(MasterAnswer(c));

    public static bool MasterRefused(BridgeCase c) => !MasterAnswer(c).StartsWith("ran", StringComparison.Ordinal);

    /// <summary>Whether the bridge decided the cell on master: neither the analysis nor the main emitter refused it first.</summary>
    public static bool BridgeDecidesOnMaster(BridgeCase c) =>
        !MasterAnswer(c).StartsWith("analysis-rejected", StringComparison.Ordinal) && !MasterAnswer(c).StartsWith("main-rejected", StringComparison.Ordinal);

    public static bool MasterAgrees(BridgeCase c) => Matches(MasterObserved(c), ProviderExpectation(c));

    /// <summary>
    /// The family of a change between two answers, keyed by kind group and how it changes, for the delta table:
    /// <c>refused becomes works</c>, <c>runs becomes refused</c>, <c>message only</c> (a raw exception becomes the
    /// bridge's positioned refusal), <c>type</c>, <c>type and value</c>, <c>value</c>.
    /// </summary>
    public static string Family(BridgeCase c, string before, string after) {
        string change;
        var (ranBefore, ranAfter) = (before.StartsWith("ran", StringComparison.Ordinal), after.StartsWith("ran", StringComparison.Ordinal));
        if (!ranBefore && ranAfter)
            change = "refused becomes works";
        else if (ranBefore && !ranAfter)
            change = "runs becomes refused";
        else if (!ranBefore && !ranAfter)
            change = "message only";
        else {
            var (b, a) = (ObservedOf(before).Split("; "), ObservedOf(after).Split("; "));
            var typeChanges = b[1] != a[1];
            var valueChanges = !b.Skip(2).SequenceEqual(a.Skip(2));
            change = typeChanges ? valueChanges ? "type and value" : "type" : "value";
        }
        return $"{Group(c)}: {change}";
    }

    public static string Group(BridgeCase c) => c.Grid switch {
        "operator" or "nullable" => $"{c.Grid} {KindGroup(c.Op!.Value)}",
        _ => c.Grid
    };

    private static string KindGroup(OperatorKind kind) => kind switch {
        _ when IsArithmetic(kind) => "arithmetic",
        _ when IsComparison(kind) => "comparison",
        OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor => "bitwise",
        OperatorKind.ShiftLeft or OperatorKind.ShiftRight => "shift",
        OperatorKind.UnaryMinus => "unary minus",
        OperatorKind.BitwiseNot => "bitwise not",
        _ => "logical"
    };
}

/// <summary>
/// T-19: the lambda bridge's numeric promotion reads the type provider. The bridge is reached only for a module lambda
/// passed where a CLR parameter is <c>Expression&lt;D&gt;</c>; the DSL builds no lambda, so no shipped document reaches it.
/// </summary>
public class LambdaBridgeProviderTests {
    private static IEnumerable<BridgeCase> InGrid(string grid) => BridgeGrid.Cases.Where(c => c.Grid == grid);

    [Test]
    public async Task Grid_CellCountsPerGrid_ArePinned() {
        var counts = BridgeGrid.Cases.GroupBy(static c => c.Grid).ToDictionary(static g => g.Key, static g => g.Count());
        await Assert.That(BridgeGrid.Operands.Count).IsEqualTo(13);
        await Assert.That(counts["operator"]).IsEqualTo(3081);
        // Amendment 7: the nullable shapes, one by one: 18 binary kinds × 13 × (3 same-underlying + 12 others), plus 3 unary kinds × 13.
        await Assert.That(counts["nullable"]).IsEqualTo(3549);
        await Assert.That(counts["conditional"]).IsEqualTo(676);
        await Assert.That(counts["return"]).IsEqualTo(676);
        await Assert.That(counts["uint64-constant"]).IsEqualTo(528);
        await Assert.That(counts["uint32-constant"]).IsEqualTo(64);
        await Assert.That(counts["constant-parameter"]).IsEqualTo(36);
        await Assert.That(BridgeGrid.Cases.Select(static c => c.Line).Distinct().Count()).IsEqualTo(BridgeGrid.Cases.Count);
    }

    [Test]
    public async Task Grid_NoCellIsAHarnessError() {
        var errors = BridgeGrid.Cases.Where(static c => BridgeGrid.Outcome(c).Stage == "harness-error")
            .Select(static c => $"{c.Line}: {BridgeGrid.Outcome(c).Message}").ToArray();
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Oracle_ProviderAgreesWithTheBinder_OnEveryPlainOperatorCell() {
        var disagreements = InGrid("operator").Where(static c => {
            var expected = BridgeGrid.Expectation(c);
            var provider = BridgeGrid.ProviderResult(c);
            return c.Op is OperatorKind.And or OperatorKind.Or or OperatorKind.Not
                ? expected.Defined != (provider is not null)
                : expected.Defined != (provider is not null) || (provider is not null && expected.Type != BridgeGrid.TypeName(provider));
        }).Select(static c => $"{c.Key}: binder {BridgeGrid.Expectation(c).Type ?? "none"}, provider {BridgeGrid.ProviderResult(c)?.Name ?? "none"}").ToArray();
        await Assert.That(disagreements).IsEmpty();
    }

    [Test]
    public async Task MasterAnswers_FileHasALineForEveryCell() {
        if (Environment.GetEnvironmentVariable("POLY_BRIDGE_MASTER_WRITE") == "1")
            File.WriteAllLines(BridgeGrid.MasterAnswersPath, BridgeGrid.Lines());
        await Assert.That(BridgeGrid.MasterAnswers.Value.Keys.Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(BridgeGrid.Cases.Select(static c => c.Line).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The run against master's answers: every cell whose answer changed (a positioned refusal counts as one answer, whatever
    /// its text), by family with count and checksum. Empty on master; after T-19 it is the predicted delta, confirmed.
    /// </summary>
    [Test]
    public async Task Run_DiffersFromMasterAnswers_OnlyOnTheDeltaCells() {
        var families = BridgeGrid.Cases
            .Where(static c => BridgeGrid.Normalized(BridgeGrid.Outcome(c).Render) != BridgeGrid.Normalized(BridgeGrid.MasterAnswer(c)))
            .GroupBy(static c => BridgeGrid.Family(c, BridgeGrid.MasterAnswer(c), BridgeGrid.Outcome(c).Render))
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => $"{g.Key} = {ConstantFoldGrid.Pin([.. g.Select(static c => c.Line)])}")
            .ToArray();
        await Assert.That(string.Join("\n", families)).IsEqualTo(string.Join("\n", ObservedDelta));
    }

    private static readonly string[] ObservedDelta = [];

    /// <summary>
    /// The predicted delta: the cells the bridge decides (neither the analysis nor the main emitter refused them first) where master's answer is not the provider's,
    /// by family. T-19's bridge gives the provider's answer, so this is the set T-19 changes.
    /// </summary>
    [Test]
    public async Task Master_DisagreesWithTheProvider_OnThePredictedDeltaFamilies() {
        var families = BridgeGrid.Cases
            .Where(static c => BridgeGrid.BridgeDecidesOnMaster(c) && !BridgeGrid.MasterAgrees(c))
            .GroupBy(static c => BridgeGrid.Family(c, BridgeGrid.MasterAnswer(c), BridgeGrid.ProviderExpectation(c).Render))
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => $"{g.Key} = {ConstantFoldGrid.Pin([.. g.Select(static c => c.Line)])}")
            .ToArray();
        await Assert.That(string.Join("\n", families)).IsEqualTo(string.Join("\n", PredictedDelta));
    }

    /// <summary>
    /// The cells the provider defines but the bridge never decides, because the analysis or the main emitter refuses them
    /// first; each has its owning row. T-19 does not change them.
    /// </summary>
    [Test]
    public async Task CellsTheBridgeDoesNotDecide_WhereTheProviderDefinesThem_AreOwned() {
        var cells = BridgeGrid.Cases.Where(static c => !BridgeGrid.BridgeDecides(c) && BridgeGrid.ProviderExpectation(c).Defined).ToArray();
        var unowned = cells.Where(static c => BridgeGrid.OwnerOfUndecided(c) is null)
            .Select(static c => $"{c.Line}: {BridgeGrid.Outcome(c).Render}").ToArray();
        await Assert.That(unowned).IsEmpty();
        var owners = cells.GroupBy(static c => BridgeGrid.OwnerOfUndecided(c)!)
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => $"{g.Key} = {ConstantFoldGrid.Pin([.. g.Select(static c => c.Line)])}")
            .ToArray();
        await Assert.That(string.Join("\n", owners)).IsEqualTo(string.Join("\n", UndecidedOwners));
    }

    private static readonly string[] UndecidedOwners = [
        "T-21 (the main emitter refuses the body while compiling the function table, before the bridge quotes it) = 97 a63ff0548b6291dd",
        "T-28 (the lowered analysis rejects the lifted ! on bool?) = 1 add0d5c7271ecdf8"
    ];

    private static BridgeCase Cell(string grid, string key) => BridgeGrid.Cases.Single(c => c.Grid == grid && c.Key == key);

    /// <summary>
    /// Issue 832's wrong VM values (owner T-21): the main emitter answers -1 for unary minus on a Decimal and on a Duration,
    /// and a wrong value and type for <c>UInt8 | UInt64</c>. Inside a bridged lambda the same expressions are right, on
    /// master already; T-21 fixes the main emitter and inverts the ledger's pin
    /// (<c>Probe_WrongVmValues_Issue832_PinnedAsTheyAreToday_T21InvertsThem</c>).
    /// </summary>
    [Test]
    public async Task Bridge_RightWhereMainIsWrong_Issue832Cells_PinnedForT21() {
        var negate = Cell("operator", "Decimal UnaryMinus -");
        await Assert.That(BridgeGrid.Outcome(negate).Observed).IsEqualTo("ran; Decimal; S1=-0.1; S2=-0.2");
        await Assert.That(BridgeGrid.LedgerCell(negate).Abi).IsEqualTo("Decimal; S1=-1; S2=-1");
        var or = Cell("operator", "UInt8 BitwiseOr UInt64");
        await Assert.That(BridgeGrid.Outcome(or).Observed).IsEqualTo("ran; UInt64; S1=255; S2=18446744073709551615");
        await Assert.That(BridgeGrid.LedgerCell(or).Abi).IsEqualTo("Byte; S1=255; S2=-1");
        // Duration is not a governed operand (the bridge's fence is the primitives): its negation is TimeSpan's own operator.
        var duration = new BridgeOperand(OperatorOracle.Operands.Single(static o => o.Name == "Duration"), false);
        var minusDuration = new BridgeCase("named", "Duration UnaryMinus -", OperatorKind.UnaryMinus, duration, null, typeof(object),
            static (x, _) => new UnaryMinus(x), [new("S1", duration.Operand.Stress, null), new("S2", duration.Operand.Plain, null)]);
        await Assert.That(BridgeGrid.Evaluate(minusDuration).Observed).IsEqualTo("ran; TimeSpan; S1=-01:00:00; S2=-01:30:00");
        await Assert.That(BridgeGrid.LedgerCell(minusDuration).Abi).IsEqualTo("TimeSpan; S1=-1; S2=-1");
    }

    private static string FamiliesOf(IEnumerable<BridgeCase> cells, Func<BridgeCase, string> family) =>
        string.Join("\n", cells.GroupBy(family)
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => $"{g.Key} = {ConstantFoldGrid.Pin([.. g.Select(static c => c.Line)])}"));

    /// <summary>
    /// The rule (owner statement 2026-11-12; T-19's done-when): every cell the bridge decides is the provider's answer:
    /// the operator <c>GetOperator</c> states over the operands with their nullable unwrapped (lifted back), the conditional's
    /// <c>GetCommonType</c>, an implicit <c>ResolveConversionFrom</c> for a return or an argument, or a positioned refusal
    /// where the provider has none. The failing families are listed.
    /// </summary>
    [Test]
    public async Task Bridge_AgreesWithTheProvider_OnEveryCellItDecides() {
        var disagreeing = BridgeGrid.Cases.Where(static c => BridgeGrid.BridgeDecides(c) && !BridgeGrid.Agrees(c));
        await Assert.That(FamiliesOf(disagreeing, static c => BridgeGrid.Family(c, BridgeGrid.Outcome(c).Render, BridgeGrid.ProviderExpectation(c).Render)))
            .IsEqualTo("");
    }

    /// <summary>
    /// The nullable shapes, one by one (lock amendment 7, 3,549 cells): a nullable operand lifts the provider's operator, so
    /// <c>int? + int</c> is <c>int?</c> and null gives null (master unwrapped and threw), and a comparison stays <c>bool</c>.
    /// </summary>
    [Test]
    public async Task Bridge_NullableLift_EveryNullableShapeIsTheProvidersLiftedOperator() {
        var nullable = InGrid("nullable").ToArray();
        await Assert.That(nullable.Length).IsEqualTo(3549);
        var disagreeing = nullable.Where(static c => BridgeGrid.BridgeDecides(c) && !BridgeGrid.Agrees(c));
        await Assert.That(FamiliesOf(disagreeing, static c => BridgeGrid.Family(c, BridgeGrid.Outcome(c).Render, BridgeGrid.ProviderExpectation(c).Render)))
            .IsEqualTo("");
    }

    /// <summary>
    /// The owner's exception (#679 6842272727) through T-18's shared rule (<c>ConstantOperandRule</c>): a non-negative signed
    /// constant beside <c>ulong</c>, <c>ulong?</c> or a <c>ulong</c> constant is judged as UInt64; a negative one stays an
    /// analysis error, as in C#.
    /// </summary>
    [Test]
    public async Task Bridge_UInt64WithNonNegativeConstant_StaysAccepted() {
        var cells = InGrid("uint64-constant").ToArray();
        var negative = cells.Where(static c => c.Key.Contains("(-1)", StringComparison.Ordinal)).ToArray();
        await Assert.That(negative.Length).IsEqualTo(264);
        await Assert.That(negative.All(static c => BridgeGrid.Outcome(c).Stage == "analysis-rejected")).IsTrue();
        var decided = cells.Except(negative).Where(BridgeGrid.BridgeDecides).ToArray();
        await Assert.That(decided.Length).IsEqualTo(236);
        var wrong = decided.Where(static c => BridgeGrid.Outcome(c).Observed != BridgeGrid.Expectation(c).Render)
            .Select(static c => $"{c.Key}: {BridgeGrid.Outcome(c).Observed}, C# {BridgeGrid.Expectation(c).Render}").ToArray();
        await Assert.That(wrong).IsEmpty();
    }

    /// <summary>
    /// Amendment 3: the stage each cell reaches, per grid, so a harness that sent every cell to one stage could not pass.
    /// A replaced body (non-nullable Decimal arithmetic, rewritten to <c>Decimal.Add</c> by the analysis) counts as ran.
    /// </summary>
    [Test]
    public async Task Grid_StagesPerGrid_AreThePredicted() {
        var stages = BridgeGrid.Cases.GroupBy(static c => $"{c.Grid} {BridgeGrid.Outcome(c).Stage}")
            .OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => $"{g.Key} = {g.Count()}");
        await Assert.That(string.Join("\n", stages)).IsEqualTo(string.Join("\n", PredictedStages));
        await Assert.That(BridgeGrid.Cases.Count(static c => BridgeGrid.Outcome(c) is { Stage: "ran", Replaced: true })).IsGreaterThanOrEqualTo(95);
    }

    private static readonly string[] PredictedStages = [
        "conditional bridge-rejected = 265",
        "conditional main-rejected = 107",
        "conditional ran = 304",
        "constant-parameter analysis-rejected = 8",
        "constant-parameter bridge-rejected = 25",
        "constant-parameter ran = 3",
        "nullable analysis-rejected = 808",
        "nullable bridge-rejected = 265",
        "nullable main-rejected = 335",
        "nullable ran = 2141",
        "operator analysis-rejected = 749",
        "operator bridge-rejected = 224",
        "operator main-rejected = 301",
        "operator ran = 1807",
        "return bridge-rejected = 484",
        "return ran = 192",
        "uint32-constant bridge-rejected = 4",
        "uint32-constant ran = 60",
        "uint64-constant analysis-rejected = 264",
        "uint64-constant main-rejected = 28",
        "uint64-constant ran = 236"
    ];

    /// <summary>
    /// Fail closed with a positioned message: no cell throws a raw exception from <c>System.Linq.Expressions</c>, and every
    /// cell the bridge decides and the provider leaves undefined is the bridge's own refusal (not an analysis error, which
    /// would pass vacuously). The refusal families with a bridge-rejected cell are pinned; a family whose undefined cells all
    /// stop before the bridge is an untested backstop.
    /// </summary>
    [Test]
    public async Task Bridge_RefusesUndefinedCells_WithItsOwnPositionedMessage() {
        var raw = BridgeGrid.Cases.Where(static c => BridgeGrid.Outcome(c).Stage == "emit-throws").Select(static c => c.Line).ToArray();
        await Assert.That(raw).IsEmpty();
        var undefined = BridgeGrid.Cases.Where(static c => !BridgeGrid.ProviderExpectation(c).Defined).ToArray();
        var notOwn = undefined.Where(static c => BridgeGrid.BridgeDecides(c)
                && !(BridgeGrid.Outcome(c) is { Stage: "bridge-rejected", Message: { } m } && m.StartsWith("VM compile rejected: lambda bridge", StringComparison.Ordinal)))
            .Select(static c => $"{c.Line}: {BridgeGrid.Outcome(c).Render}").ToArray();
        await Assert.That(notOwn).IsEmpty();
        var families = undefined.Where(static c => BridgeGrid.Outcome(c).Stage == "bridge-rejected")
            .GroupBy(BridgeGrid.Group).OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => $"{g.Key} = {g.Count()}");
        await Assert.That(string.Join("\n", families)).IsEqualTo(string.Join("\n", BridgeRefusalFamilies));
        var backstops = undefined.GroupBy(BridgeGrid.Group).Where(static g => g.All(static c => !BridgeGrid.BridgeDecides(c)))
            .Select(static g => g.Key).Order(StringComparer.Ordinal);
        await Assert.That(string.Join("\n", backstops)).IsEqualTo(string.Join("\n", UntestedBackstops));
    }

    private static readonly string[] BridgeRefusalFamilies = [
        "conditional = 265",
        "constant-parameter = 25",
        "nullable bitwise not = 1",
        "nullable logical = 6",
        "nullable shift = 258",
        "operator bitwise = 84",
        "operator bitwise not = 4",
        "operator shift = 134",
        "operator unary minus = 2",
        "return = 484",
        "uint32-constant = 4"
    ];

    /// <summary>
    /// The refusal families whose undefined cells never reach the bridge: the lowered analysis (T-18) or the main emitter's
    /// function-table compile (T-21) refuses each first, so the bridge's refusal there is not exercised by this grid.
    /// </summary>
    private static readonly string[] UntestedBackstops = [
        "nullable arithmetic",
        "nullable bitwise",
        "nullable comparison",
        "nullable unary minus",
        "operator arithmetic",
        "operator comparison",
        "operator logical",
        "uint64-constant"
    ];

    /// <summary>
    /// Lock amendment 4: the provider models no constant-expression conversion (C# 10.2.11) outside the owner's
    /// <c>ulong</c> exception, so a non-negative <c>int</c> constant beside a <c>uint</c> takes the provider's operator
    /// (<c>uint + 1</c> is Int64, as the owner ruled for the analysis), and a constant into an unsigned or narrower return is
    /// refused. These are the only cells the bridge decides differently from C#; T-26 models the conversion and removes them.
    /// </summary>
    [Test]
    public async Task Bridge_DisagreesWithCSharp_OnlyOnTheConstantConversionCells_OwnedByT26() {
        var disagreeing = BridgeGrid.Cases.Where(static c => BridgeGrid.BridgeDecides(c) && !BridgeGrid.Matches(BridgeGrid.Outcome(c).Observed, BridgeGrid.Expectation(c))).ToArray();
        await Assert.That(disagreeing.All(static c => c.Grid is "uint32-constant" or "constant-parameter")).IsTrue();
        await Assert.That(ConstantFoldGrid.Pin([.. disagreeing.Select(static c => c.Line)])).IsEqualTo("23 e0fc5956255e264f");
    }

    /// <summary>
    /// Lock amendment 2. Set 1, the plain operator cells where the bridge's answer is not the main emitter's (the operator
    /// ledger's ABI column for the same cell), is a subset of the ledger's VM-divergence cells: where the two disagree, the
    /// main emitter is the one the ledger records as wrong (T-21). Set 2, the set-1 cells the bridge already answered like
    /// the provider on master, is a subset of set 1. Both are pinned; T-21 inverts them.
    /// </summary>
    [Test]
    public async Task Bridge_DiffersFromMain_OnlyOnLedgerDivergenceCells() {
        var set1 = BridgeGrid.Set1();
        var set2 = BridgeGrid.Set2();
        var outside = set1.Where(static c => !BridgeGrid.LedgerDiverges(c))
            .Select(static c => $"{c.Key}: bridge {BridgeGrid.BridgeAnswer(BridgeGrid.Outcome(c).Observed)}, main {BridgeGrid.MainAnswer(c)}").ToArray();
        await Assert.That(outside).IsEmpty();
        await Assert.That(set2.All(c => set1.Contains(c))).IsTrue();
        await Assert.That(ConstantFoldGrid.Pin([.. set1.Select(static c => c.Line)])).IsEqualTo("709 4661008facbc71f6");
        await Assert.That(ConstantFoldGrid.Pin([.. set2.Select(static c => c.Line)])).IsEqualTo("391 8983a5ccdda34ae8");
    }

    private static readonly string[] PredictedDelta = [
        "conditional: refused becomes works = 84 d6ced5cc4370eb72",
        "conditional: runs becomes refused = 206 c6602fc15602ec42",
        "conditional: type = 55 769bfe9b069c85ec",
        "conditional: type and value = 61 9b901a1e6893cad8",
        "constant-parameter: refused becomes works = 1 6c2c50a175a712d3",
        "constant-parameter: runs becomes refused = 7 604c7bfeebc44280",
        "nullable arithmetic: refused becomes works = 15 40719004b8751909",
        "nullable arithmetic: type = 5 f004e8b6060c0526",
        "nullable arithmetic: type and value = 725 8c0b3ca7d04eebb9",
        "nullable bitwise not: runs becomes refused = 1 2b860d34c6aa768c",
        "nullable bitwise not: type = 2 6d073e1263735747",
        "nullable bitwise not: type and value = 2 f314427d68973cf3",
        "nullable bitwise: refused becomes works = 3 6b577ede0da9ee35",
        "nullable bitwise: type = 12 4f05b7132a6286ac",
        "nullable bitwise: type and value = 246 01c6136a79c85d75",
        "nullable comparison: value = 864 83d47e721bc9cc34",
        "nullable shift: runs becomes refused = 258 f657670ef8f09975",
        "nullable shift: type and value = 132 0f7ac8bb237bc36c",
        "nullable unary minus: type = 1 570450c6c0ea1a48",
        "operator arithmetic: refused becomes works = 15 b4c34b31b0adc22a",
        "operator arithmetic: type = 17 16d10786f3e2942c",
        "operator arithmetic: type and value = 23 5822ce164f0620b6",
        "operator bitwise not: message only = 3 b4225ef5d81f099a",
        "operator bitwise not: refused becomes works = 1 a92930969d106721",
        "operator bitwise not: runs becomes refused = 1 4fa3ce6cd2fbb7de",
        "operator bitwise not: type = 2 a07a6e97145a5356",
        "operator bitwise not: type and value = 2 6eaa4b8be167ed34",
        "operator bitwise: message only = 6 ad015cd6aa788eeb",
        "operator bitwise: refused becomes works = 3 a67bb393dfc9a792",
        "operator bitwise: runs becomes refused = 78 98c82e8ba2ad09a4",
        "operator bitwise: type = 30 7e64603254e54daa",
        "operator shift: runs becomes refused = 134 3f558e65b48ba156",
        "operator unary minus: message only = 2 74e2293bd02cf24b",
        "operator unary minus: refused becomes works = 5 f33c6acf21a1a017",
        "operator unary minus: type = 1 7bd03e71bbf7b6c9",
        "return: message only = 2 3d8ce1a5cdf9641f",
        "return: refused becomes works = 106 65ef8d87d823d381",
        "return: runs becomes refused = 105 db9b7aa20e9c22b4",
        "uint32-constant: refused becomes works = 1 38045c880a5b88a8",
        "uint32-constant: runs becomes refused = 4 4e60f7fc222b77eb",
        "uint32-constant: type = 17 4db844325a33697b",
        "uint32-constant: type and value = 14 1ff0dc34846800c7",
        "uint32-constant: value = 12 4d5d04ebb876e2c0",
        "uint64-constant: value = 48 69d7cbf64731443b"
    ];
}