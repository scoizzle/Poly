using System.Globalization;
using System.Reflection;

using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.Interpretation;

/// <summary>A delegate-position probe: the lambda is a VM closure re-entered through its function-table entry.</summary>
public static class DelegateProbe<TA, TB, TR> {
    public static void Apply(Func<TA, TB, TR> f, TA a, TB b) => _ = f(a, b);
}

/// <summary>Runs a delegate lambda, so its body is the function-table entry the main emitter compiled.</summary>
public static class ActionRunner {
    public static void Run(Action body) => body();
}

/// <summary>The probe a module method forwards its <c>Expression&lt;D&gt;</c> parameter to.</summary>
public static class QuoteDoorProbe {
    public static bool Apply(System.Linq.Expressions.Expression<Func<ulong?, object>> f, ulong? a) {
        BridgeProbeLog.Record(f, () => f.Compile()(a));
        return true;
    }
}

/// <summary>
/// T-19b: a literal lambda passed directly where the resolved parameter is <c>Expression&lt;D&gt;</c> is quoted by the
/// lambda bridge; these are the shapes around that decision (delegate position, a stored lambda, one node used both as a
/// delegate and as a quote, a quote inside a delegate lambda, the module-callee door), the <c>nuint</c> probe and the
/// families outside the bridge's fence, judged against compiled C#.
/// </summary>
public class QuoteOnlyLambdaTests {
    private static Member Static(Type type, string name) => new(new ClrTypeReference(type), name);

    private static string TypeName(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    private static Type BodyType(System.Linq.Expressions.LambdaExpression tree) =>
        tree.ReturnType == typeof(object)
            && tree.Body is System.Linq.Expressions.UnaryExpression { NodeType: System.Linq.Expressions.ExpressionType.Convert } box
            ? box.Operand.Type
            : tree.Body.Type;

    private static Exception Unwrap(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : ex;

    /// <summary>
    /// The stage a program reaches, as the bridge grid names them: <c>analysis-rejected</c>, <c>bridge-rejected</c> (the
    /// bridge's own message), <c>main-rejected</c> (an emit exception the bridge did not mark), <c>emit-throws</c> (a marked
    /// exception without the bridge's message), or <c>ran</c> with the quoted body's type and the value the probe recorded.
    /// </summary>
    internal static string Run(Node call, object?[] args, ITypeDefinitionProvider? provider = null) {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var analysis = Interpreter.Analyze(call, provider ?? ClrTypeDefinitionRegistry.Shared);
            if (analysis.Diagnostics.FirstOrDefault(static d => d.Severity == DiagnosticSeverity.Error) is { } error)
                return $"analysis-rejected: {error.Message}";
            Poly.Interpretation.Vm.VmProgram program;
            try {
                program = Interpreter.Compile(call, analysis);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("VM compile rejected: lambda bridge", StringComparison.Ordinal)) {
                return $"bridge-rejected: {ex.Message}";
            }
            catch (Exception ex) when (!ex.Data.Contains(Poly.Interpretation.Vm.Abi.AbiEmitter.LambdaBridgeExceptionKey)) {
                return $"main-rejected: {ex.GetType().Name}: {ex.Message}";
            }
            catch (Exception ex) {
                return $"emit-throws: {ex.GetType().Name}: {ex.Message}";
            }
            BridgeProbeLog.Take();
            try {
                using var exec = Interpreter.Execute(program, args);
            }
            catch (Exception ex) {
                return $"run-throws: {Unwrap(ex).GetType().Name}: {Unwrap(ex).Message}";
            }
            if (BridgeProbeLog.Take() is not { } record)
                return "ran; the probe recorded nothing";
            return record.Error is { } runError
                ? $"ran; {TypeName(BodyType(record.Tree))}; throws {Unwrap(runError).GetType().Name}"
                : $"ran; {TypeName(BodyType(record.Tree))}; {ConstantFoldGrid.Format(record.Value)}";
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static (Parameter A, Parameter B, Lambda Lambda) Binary(Type left, Type right, Func<Parameter, Parameter, Node> body) {
        var x = new Parameter("x", new ClrTypeReference(left));
        var y = new Parameter("y", new ClrTypeReference(right));
        return (new Parameter("a", new ClrTypeReference(left)), new Parameter("b", new ClrTypeReference(right)), new Lambda([x, y], body(x, y)));
    }

    private static Member Quote(Type left, Type right) =>
        Static(typeof(BridgeProbe<,,>).MakeGenericType(left, right, typeof(object)), "Apply");

    private static Member Delegate(Type left, Type right) =>
        Static(typeof(DelegateProbe<,,>).MakeGenericType(left, right, typeof(object)), "Apply");

    /// <summary>A body the main emitter refuses, by kind: the arms differ in representation, no operator, a raw Expression API exception.</summary>
    private static readonly (string Name, Type Left, Type Right, Func<Parameter, Parameter, Node> Body, object Plain, object Other, string Refusal)[] RefusingBodies = [
        ("arms differ", typeof(decimal), typeof(int), static (x, y) => new Conditional(new Equal(x, x), x, y), 0.1m, 7,
            "conditional arms differ in representation"),
        ("no operator", typeof(ulong?), typeof(ulong?), static (x, _) => new Add(x, new Constant(1)), 7UL, 7UL,
            "no operator for 'UInt64' and 'Int32'"),
        ("raw bitwise", typeof(long), typeof(decimal), static (x, y) => new BitwiseAnd(x, y), 7L, 0.2m,
            "The binary operator And is not defined")
    ];

    /// <summary>
    /// The positive control for "no cell is <c>main-rejected</c> in expression position": the same three bodies passed where
    /// the parameter is a delegate keep their function-table entry, so the main emitter still refuses them there.
    /// </summary>
    [Test]
    public async Task DelegatePosition_RefusingBody_StaysMainRejected() {
        foreach (var (name, left, right, body, plain, other, refusal) in RefusingBodies) {
            var (a, b, lambda) = Binary(left, right, body);
            var stage = Run(new Invoke(Delegate(left, right), lambda, a, b), [plain, other]);
            await Assert.That(stage).StartsWith("main-rejected: ");
            await Assert.That(stage).Contains(refusal);
        }
    }

    /// <summary>A lambda stored in a variable keeps its table entry (T-19b skips only a literal at the boundary).</summary>
    [Test]
    public async Task StoredLambda_RefusingBody_AtExpressionPosition_StaysMainRejected() {
        var (a, b, lambda) = Binary(typeof(decimal), typeof(int), static (x, y) => new Conditional(new Equal(x, x), x, y));
        var f = new Variable("f");
        var node = new Block([
            new Assignment(f, lambda),
            new Invoke(Quote(typeof(decimal), typeof(int)), new Variable("f"), a, b)
        ], [f]);
        var stage = Run(node, [0.1m, 7]);
        await Assert.That(stage).StartsWith("main-rejected: ");
        await Assert.That(stage).Contains("conditional arms differ in representation");
    }

    /// <summary>One lambda node used both as a delegate and as a quote keeps its table entry: the delegate needs it.</summary>
    [Test]
    public async Task LambdaUsedAsDelegateAndAsQuote_KeepsItsTableEntry() {
        var (a, b, lambda) = Binary(typeof(decimal), typeof(int), static (x, y) => new Conditional(new Equal(x, x), x, y));
        var node = new Block([
            new Invoke(Delegate(typeof(decimal), typeof(int)), lambda, a, b),
            new Invoke(Quote(typeof(decimal), typeof(int)), lambda, a, b)
        ]);
        var stage = Run(node, [0.1m, 7]);
        await Assert.That(stage).StartsWith("main-rejected: ");
        await Assert.That(stage).Contains("conditional arms differ in representation");
    }

    /// <summary>
    /// A quote-only lambda inside a delegate lambda: the outer body is a function-table entry, and the quote inside it is
    /// the bridge's. On master the inner lambda's own table entry refused the body after the bridge quoted it; it has no
    /// entry now, so the bridge's answer (C#'s: the conditional is Decimal) runs.
    /// </summary>
    [Test]
    public async Task QuoteOnlyLambda_InsideADelegateLambda_IsQuotedByTheBridge() {
        var (_, _, inner) = Binary(typeof(decimal), typeof(int), static (x, y) => new Conditional(new Equal(x, x), x, y));
        var outer = new Lambda([], new Invoke(Quote(typeof(decimal), typeof(int)), inner, new Constant(0.1m), new Constant(7)));
        var stage = Run(new Invoke(Static(typeof(ActionRunner), nameof(ActionRunner.Run)), outer), []);
        await Assert.That(stage).IsEqualTo("ran; Decimal; 0.1");
    }

    private static ITypeDefinitionProvider Provider(TypeDefinitionNode typeNode) {
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var context = AnalysisContext.CreateDefault();
        analyzer.Analyze(context, typeNode);
        return context.TypeDefinitions;
    }

    /// <summary>
    /// The module-callee door (lock amendment 1): a module method whose parameter is <c>Expression&lt;Func&lt;ulong?,
    /// object&gt;&gt;</c> forwards it to a CLR probe; the caller passes a literal lambda whose body the main emitter refuses.
    /// </summary>
    [Test]
    public async Task ModuleCalleeDoor_LiteralLambda_IsDecidedAtTheBoundary() {
        var f = new Parameter("f", new ClrTypeReference(typeof(System.Linq.Expressions.Expression<Func<ulong?, object>>)));
        var value = new Parameter("value", new ClrTypeReference(typeof(ulong?)));
        var typeNode = new TypeDefinitionNode(
            "QuoteDoor", "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Apply",
                    new PrimitiveTypeReference(PrimitiveType.Boolean),
                    Parameters: [f, value],
                    Body: new Invoke(Static(typeof(QuoteDoorProbe), nameof(QuoteDoorProbe.Apply)), f, value),
                    IsStatic: true)
            ]);
        var x = new Parameter("x", new ClrTypeReference(typeof(ulong?)));
        var a = new Parameter("a", new ClrTypeReference(typeof(ulong?)));
        var call = new Invoke(new Member(new TypeReference("Sample.QuoteDoor"), "Apply"),
            new Lambda([x], new Add(x, new Constant(1))), a);
        // C#: x + 1 over ulong? is ulong? (the constant converts), 7 + 1 = 8.
        await Assert.That(Run(call, [7UL], Provider(typeNode))).IsEqualTo("ran; UInt64?; 8");
    }

    /// <summary>
    /// The one shape where the call site can disagree with the boundary decision: a module method the analysis resolves,
    /// whose body the VM cannot compile (a lambda inside it), called on a variable, so the call falls through to name
    /// dispatch and compiles the lambda argument as a closure value. The boundary decided the lambda is quoted, so it has
    /// no function-table entry: the emitter's guard refuses the closure at compile time, where master failed at run time.
    /// </summary>
    [Test]
    public async Task ModuleMethodWithoutACompilableBody_LiteralLambdaArgument() {
        var f = new Parameter("f", new ClrTypeReference(typeof(System.Linq.Expressions.Expression<Func<ulong?, object>>)));
        var typeNode = new TypeDefinitionNode(
            "QuoteDoorHost", "Sample",
            Constructors: [new ConstructorDefinitionNode([], new Block([]))],
            Methods: [
                new MethodDefinitionNode(
                    "Take",
                    new PrimitiveTypeReference(PrimitiveType.Boolean),
                    Parameters: [f],
                    Body: new Block([new Invoke(new Lambda([], new Constant(1L))), new Constant(true)]))
            ]);
        var x = new Parameter("x", new ClrTypeReference(typeof(ulong?)));
        var host = new Variable("host");
        var node = new Block([
            new Assignment(host, new New(new TypeReference("Sample.QuoteDoorHost"))),
            new Invoke(new Member(new Variable("host"), "Take"), new Lambda([x], new Equal(x, x)))
        ], [host]);
        var stage = Run(node, [], Provider(typeNode));
        await Assert.That(stage).StartsWith("main-rejected: InvalidOperationException: VM compile rejected: ");
        await Assert.That(stage).Contains("has no function-table entry");
    }

    /// <summary>
    /// Lock amendment 9: the bridge's shape checks before the quote (<c>CheckDelegateShape</c>: arity and declared parameter
    /// types; the open-generic refusal) carry the bridge's mark and message. No program reaches them through the analysis:
    /// overload resolution refuses a lambda whose arity or declared parameter type does not match, and resolves no open
    /// generic method. This pins that, so the marks are a net and a shape that reaches them shows up here first.
    /// </summary>
    [Test]
    public async Task QuotedLambda_ShapeMismatch_IsRefusedByTheAnalysisFirst() {
        var (a, _, lambda) = Binary(typeof(int), typeof(int), static (x, y) => new Add(x, y));
        var probe = Static(typeof(BridgeProbe<,>).MakeGenericType(typeof(int), typeof(object)), "Apply");
        await Assert.That(Run(new Invoke(probe, lambda, a), [7])).StartsWith("analysis-rejected: no matching member 'Apply'");
        var wide = new Parameter("x", new ClrTypeReference(typeof(long)));
        await Assert.That(Run(new Invoke(probe, new Lambda([wide], wide), a), [7])).StartsWith("analysis-rejected: no matching member 'Apply'");
    }

    /// <summary>
    /// The <c>nuint</c> probe (T-18b's re-review): <c>ConstantOperandRule</c> covers <c>nuint</c>, and the bridge's branch
    /// for it converts both operands to <c>ulong</c>. The stage and the quoted body's static type per cell.
    /// </summary>
    [Test]
    public async Task NUIntBesideAConstant_Probe() {
        var lines = new List<string>();
        foreach (var (name, operand, ret, body) in new (string, Type, Type, Func<Parameter, Node>)[] {
            ("nuint x + 1 -> nuint", typeof(nuint), typeof(nuint), static x => new Add(x, new Constant(1))),
            ("nuint x + 1 -> object", typeof(nuint), typeof(object), static x => new Add(x, new Constant(1))),
            ("nuint x > 0 -> object", typeof(nuint), typeof(object), static x => new GreaterThan(x, new Constant(0))),
            ("nuint? x + 1 -> object", typeof(nuint?), typeof(object), static x => new Add(x, new Constant(1))),
            ("nuint? x > 0 -> object", typeof(nuint?), typeof(object), static x => new GreaterThan(x, new Constant(0)))
        }) {
            var x = new Parameter("x", new ClrTypeReference(operand));
            var a = new Parameter("a", new ClrTypeReference(operand));
            var probe = Static(typeof(BridgeProbe<,>).MakeGenericType(operand, ret), "Apply");
            lines.Add($"{name} = {Run(new Invoke(probe, new Lambda([x], body(x)), a), [(nuint)7])}");
        }
        await Assert.That(string.Join("\n", lines)).IsEqualTo(string.Join("\n", NUIntProbe));
    }

    /// <summary>
    /// The main emitter accepts these bodies, so the bridge decides them on master already. The value is C#'s, the static
    /// type is not: C# types <c>x + 1</c> as <c>nuint</c>, the bridge's constant branch states <c>ulong</c> for both
    /// operands and the result, so the quote is <c>UInt64</c> and the <c>nuint</c> return is refused. Owner T-21 (it
    /// replaces <c>OperandsFor</c>).
    /// </summary>
    private static readonly string[] NUIntProbe = [
        "nuint x + 1 -> nuint = bridge-rejected: VM compile rejected: lambda bridge cannot convert 'UInt64' to 'UIntPtr' for lambda return: the type provider states no implicit conversion.",
        "nuint x + 1 -> object = ran; UInt64; 8",
        "nuint x > 0 -> object = ran; Boolean; True",
        "nuint? x + 1 -> object = ran; UInt64?; 8",
        "nuint? x > 0 -> object = ran; Boolean; True"
    ];

    // ---- outside the fence: the families the bridge leaves to the Expression API, judged against compiled C# ----

    /// <summary>A cell of the fence probe; <see cref="CSharp"/> null: C# refuses the body (a compile error).</summary>
    private sealed record FenceCell(string Name, Type Left, Type? Right, Func<Parameter, Parameter?, Node> Body,
        System.Linq.Expressions.LambdaExpression? CSharp, (object? Left, object? Right)[] Sets);

    private static object Op(string name) => OperatorOracle.Operands.Single(o => o.Name == name).Stress!;

    private static object Plain(string name) => OperatorOracle.Operands.Single(o => o.Name == name).Plain!;

    private static FenceCell Cell<TA, TB>(string name, Func<Parameter, Parameter?, Node> body,
        System.Linq.Expressions.Expression<Func<TA, TB, object?>> csharp, params (object? Left, object? Right)[] sets) =>
        new(name, typeof(TA), typeof(TB), body, csharp, sets);

    private static FenceCell Cell<TA>(string name, Func<Parameter, Parameter?, Node> body,
        System.Linq.Expressions.Expression<Func<TA, object?>> csharp, params object?[] sets) =>
        new(name, typeof(TA), null, body, csharp, [.. sets.Select(static s => (s, (object?)null))]);

    private static FenceCell Refused<TA>(string name, Func<Parameter, Parameter?, Node> body, params object?[] sets) =>
        new(name, typeof(TA), null, body, null, [.. sets.Select(static s => (s, (object?)null))]);

    private static FenceCell Refused<TA, TB>(string name, Func<Parameter, Parameter?, Node> body, params (object? Left, object? Right)[] sets) =>
        new(name, typeof(TA), typeof(TB), body, null, sets);

    /// <summary>The VM's body is <c>x == x ? x : y</c>; C#'s condition only has to be true (for a null <c>x</c> too).</summary>
    private static Node Pick(Parameter x, Node other) => new Conditional(new Equal(x, x), x, other);

    public static bool Always(object? _) => true;

    private static FenceCell[] FenceCells() {
        var (dt1, dt2) = (Op("DateTime"), Plain("DateTime"));
        var (ts1, ts2) = (Op("Duration"), Plain("Duration"));
        var (d1, d2) = (Op("Date"), Plain("Date"));
        var (t1, t2) = (Op("Time"), Plain("Time"));
        var (g1, g2) = (Op("Uuid"), Plain("Uuid"));
        var (s1, s2) = (Op("Text"), Plain("Text"));
        return [
            Cell<DateTime, DateTime>("DateTime - DateTime", static (x, y) => new Subtract(x, y!), static (x, y) => x - y, (dt1, dt2), (dt2, dt1)),
            Cell<DateTime, TimeSpan>("DateTime + Duration", static (x, y) => new Add(x, y!), static (x, y) => x + y, (dt1, ts1), (dt2, ts2)),
            Cell<DateTime, DateTime>("DateTime < DateTime", static (x, y) => new LessThan(x, y!), static (x, y) => x < y, (dt1, dt2), (dt2, dt1)),
            Cell<DateTime?, DateTime>("DateTime? == DateTime", static (x, y) => new Equal(x, y!), static (x, y) => x == y, (dt1, dt1), (null, dt2)),
            Cell<TimeSpan, TimeSpan>("Duration + Duration", static (x, y) => new Add(x, y!), static (x, y) => x + y, (ts1, ts2)),
            Cell<TimeSpan?, TimeSpan>("Duration? + Duration", static (x, y) => new Add(x, y!), static (x, y) => x + y, (ts1, ts2), (null, ts2)),
            Cell<TimeSpan>("-Duration", static (x, _) => new UnaryMinus(x), static x => -x, ts1, ts2),
            Cell<DateOnly, DateOnly>("Date == Date", static (x, y) => new Equal(x, y!), static (x, y) => x == y, (d1, d2), (d1, d1)),
            Cell<TimeOnly, TimeOnly>("Time < Time", static (x, y) => new LessThan(x, y!), static (x, y) => x < y, (t1, t2), (t2, t1)),
            Cell<Guid, Guid>("Uuid == Uuid", static (x, y) => new Equal(x, y!), static (x, y) => x == y, (g1, g2), (g1, g1)),
            Cell<Guid?, Guid>("Uuid? == Uuid", static (x, y) => new Equal(x, y!), static (x, y) => x == y, (g1, g1), (null, g2)),
            Cell<string, string>("Text + Text", static (x, y) => new Add(x, y!), static (x, y) => x + y, (s1, s2)),
            Cell<string, string>("Text == Text", static (x, y) => new Equal(x, y!), static (x, y) => x == y, (s1, s2), (s1, s1)),
            Cell<string>("Text == null", static (x, _) => new Equal(x, new Constant(null)), static x => x == null, s1, null),
            Cell<DayOfWeek, DayOfWeek>("DayOfWeek == DayOfWeek", static (x, y) => new Equal(x, y!), static (x, y) => x == y, (DayOfWeek.Monday, DayOfWeek.Friday), (DayOfWeek.Monday, DayOfWeek.Monday)),
            Cell<DayOfWeek, DayOfWeek>("DayOfWeek < DayOfWeek", static (x, y) => new LessThan(x, y!), static (x, y) => x < y, (DayOfWeek.Monday, DayOfWeek.Friday), (DayOfWeek.Friday, DayOfWeek.Monday)),
            Cell<DayOfWeek, int>("DayOfWeek + Int32", static (x, y) => new Add(x, y!), static (x, y) => x + y, (DayOfWeek.Monday, 2)),
            Cell<object, object>("Object == Object", static (x, y) => new Equal(x, y!), static (x, y) => x == y, (s1, s2), (s1, s1)),
            Cell<object>("Object == null", static (x, _) => new Equal(x, new Constant(null)), static x => x == null, s1, null),
            Cell<DateTime>("(Object)DateTime", static (x, _) => new TypeCast(x, new ClrTypeReference(typeof(object))), static x => (object)x, dt1),
            Cell<int>("(DayOfWeek)Int32", static (x, _) => new TypeCast(x, new ClrTypeReference(typeof(DayOfWeek))), static x => (DayOfWeek)x, 2),
            Cell<DayOfWeek>("(Int32)DayOfWeek", static (x, _) => new TypeCast(x, new ClrTypeReference(typeof(int))), static x => (int)x, DayOfWeek.Friday),
            Cell<object>("Object as Text", static (x, _) => new TypeAs(x, new ClrTypeReference(typeof(string))), static x => x as string, s1, 7),
            Cell<object>("Object is Text", static (x, _) => new TypeIs(x, new ClrTypeReference(typeof(string))), static x => x is string, s1, 7),
            Cell<object>("(Duration)Object", static (x, _) => new TypeCast(x, new ClrTypeReference(typeof(TimeSpan))), static x => (TimeSpan)x, ts1),
            // C# CS0077: 'as' needs a reference or nullable type.
            Refused<object>("Object as Int32", static (x, _) => new TypeAs(x, new ClrTypeReference(typeof(int))), s1),
            // Coalesce and conditional outside the fence: the function-table compile refused some of these on master.
            Cell<DayOfWeek?>("DayOfWeek? ?? DayOfWeek", static (x, _) => new Coalesce(x, new Constant(DayOfWeek.Monday)), static x => x ?? DayOfWeek.Monday, DayOfWeek.Friday, null),
            Refused<DayOfWeek?, int>("DayOfWeek? ?? Int32", static (x, y) => new Coalesce(x, y!), (DayOfWeek.Friday, 1)),
            Refused<string>("Text ?? Int32(1)", static (x, _) => new Coalesce(x, new Constant(1)), s1),
            Cell<DateTime?, DateTime>("DateTime? ?? DateTime", static (x, y) => new Coalesce(x, y!), static (x, y) => x ?? y, (dt1, dt2), (null, dt2)),
            Cell<Guid?, Guid>("Uuid? ?? Uuid", static (x, y) => new Coalesce(x, y!), static (x, y) => x ?? y, (g1, g2), (null, g2)),
            Refused<string>("x ? Text : DateTime", static (x, _) => Pick(x, new Constant(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc))), s1),
            Cell<DateTime?, DateTime>("x ? DateTime? : DateTime", static (x, y) => Pick(x, y!), static (x, y) => Always(x) ? x : y, (dt1, dt2), (null, dt2)),
            Cell<TimeSpan, TimeSpan?>("x ? Duration : Duration?", static (x, y) => Pick(x, y!), static (x, y) => Always(x) ? x : y, (ts1, ts2), (ts1, null)),
            Refused<DayOfWeek, int>("x ? DayOfWeek : Int32", static (x, y) => Pick(x, y!), (DayOfWeek.Friday, 1)),
            Cell<object, int>("x ? Object : Int32", static (x, y) => Pick(x, y!), static (x, y) => Always(x) ? x : y, (s1, 7))
        ];
    }

    /// <summary>What compiled C# says for a cell: the body's type and the value per set.</summary>
    private static string CSharpAnswer(FenceCell cell) {
        if (cell.CSharp is null)
            return "refused";
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var compiled = cell.CSharp.Compile();
            var values = cell.Sets.Select(set => {
                try {
                    return ConstantFoldGrid.Format(cell.Right is null ? compiled.DynamicInvoke(set.Left) : compiled.DynamicInvoke(set.Left, set.Right));
                }
                catch (Exception ex) {
                    return $"throws {Unwrap(ex).GetType().Name}";
                }
            });
            return $"ran; {TypeName(BodyType(cell.CSharp))}; {string.Join(" | ", values)}";
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>The cell through the VM, one run per set: the stage, or the type and the values joined as C#'s are.</summary>
    private static string VmAnswer(FenceCell cell) {
        var stages = cell.Sets.Select(set => {
            var x = new Parameter("x", new ClrTypeReference(cell.Left));
            var a = new Parameter("a", new ClrTypeReference(cell.Left));
            Node call;
            if (cell.Right is { } right) {
                var y = new Parameter("y", new ClrTypeReference(right));
                var b = new Parameter("b", new ClrTypeReference(right));
                call = new Invoke(Quote(cell.Left, right), new Lambda([x, y], cell.Body(x, y)), a, b);
                return Run(call, [set.Left, set.Right]);
            }
            var probe = Static(typeof(BridgeProbe<,>).MakeGenericType(cell.Left, typeof(object)), "Apply");
            call = new Invoke(probe, new Lambda([x], cell.Body(x, null)), a);
            return Run(call, [set.Left]);
        }).ToArray();
        if (!stages.All(static s => s.StartsWith("ran; ", StringComparison.Ordinal)))
            return stages.First(static s => !s.StartsWith("ran; ", StringComparison.Ordinal));
        var type = stages[0].Split("; ")[1];
        return $"ran; {type}; {string.Join(" | ", stages.Select(static s => string.Join("; ", s.Split("; ").Skip(2))))}";
    }

    /// <summary>
    /// Lock amendment 3: outside the bridge's fence (temporal, Guid, string, enum, reference and null equality, casts) the
    /// Expression API types the body. A cell that runs must be C#'s answer, from the compiled C# expression; a refusal must be
    /// positioned (never a raw <c>emit-throws</c>). The stage per cell is pinned.
    /// </summary>
    [Test]
    public async Task OutsideTheFence_RunsAsCSharpOrRefusesWithAPosition() {
        var cells = FenceCells();
        var answers = cells.Select(static c => (Cell: c, Vm: VmAnswer(c), CSharp: CSharpAnswer(c))).ToArray();
        var wrong = answers.Where(static a => a.Vm.StartsWith("ran", StringComparison.Ordinal) && a.Vm != a.CSharp)
            .Select(static a => $"{a.Cell.Name}: VM {a.Vm}, C# {a.CSharp}").ToArray();
        await Assert.That(wrong).IsEmpty();
        var raw = answers.Where(static a => !a.Vm.StartsWith("ran", StringComparison.Ordinal)
                && !a.Vm.StartsWith("analysis-rejected", StringComparison.Ordinal)
                && !a.Vm.StartsWith("bridge-rejected", StringComparison.Ordinal))
            .Select(static a => $"{a.Cell.Name}: {a.Vm}").ToArray();
        await Assert.That(raw).IsEmpty();
        var refused = answers.Where(static a => a.CSharp == "refused" && !a.Vm.StartsWith("analysis-rejected", StringComparison.Ordinal)
                && !a.Vm.StartsWith("bridge-rejected", StringComparison.Ordinal))
            .Select(static a => $"{a.Cell.Name}: {a.Vm}").ToArray();
        await Assert.That(refused).IsEmpty();
        var stages = answers.Select(static a => $"{a.Cell.Name} = {a.Vm.Split(':', ';')[0]}");
        await Assert.That(string.Join("\n", stages)).IsEqualTo(string.Join("\n", FenceStages));
    }

    /// <summary>
    /// The stage per cell. The analysis refuses temporal arithmetic and nullable temporal equality (T-23) and enum
    /// arithmetic (T-25); the bridge refuses enum ordering, which the Expression API does not define (T-25).
    /// </summary>
    private static readonly string[] FenceStages = [
        "DateTime - DateTime = analysis-rejected",
        "DateTime + Duration = analysis-rejected",
        "DateTime < DateTime = ran",
        "DateTime? == DateTime = analysis-rejected",
        "Duration + Duration = analysis-rejected",
        "Duration? + Duration = analysis-rejected",
        "-Duration = ran",
        "Date == Date = ran",
        "Time < Time = ran",
        "Uuid == Uuid = ran",
        "Uuid? == Uuid = ran",
        "Text + Text = ran",
        "Text == Text = ran",
        "Text == null = ran",
        "DayOfWeek == DayOfWeek = ran",
        "DayOfWeek < DayOfWeek = bridge-rejected",
        "DayOfWeek + Int32 = analysis-rejected",
        "Object == Object = ran",
        "Object == null = ran",
        "(Object)DateTime = ran",
        "(DayOfWeek)Int32 = ran",
        "(Int32)DayOfWeek = ran",
        "Object as Text = ran",
        "Object is Text = ran",
        "(Duration)Object = ran",
        "Object as Int32 = analysis-rejected",
        // Refused by the function-table compile on master (main-rejected); the bridge decides them now: C# defines the
        // first, fifth, sixth and the last, so they run; it refuses the other three, which are the bridge's refusal.
        "DayOfWeek? ?? DayOfWeek = ran",
        "DayOfWeek? ?? Int32 = bridge-rejected",
        "Text ?? Int32(1) = bridge-rejected",
        "DateTime? ?? DateTime = ran",
        "Uuid? ?? Uuid = ran",
        "x ? Text : DateTime = bridge-rejected",
        "x ? DateTime? : DateTime = ran",
        "x ? Duration : Duration? = ran",
        "x ? DayOfWeek : Int32 = bridge-rejected",
        "x ? Object : Int32 = ran"
    ];
}