using System.Globalization;

using Microsoft.CSharp.RuntimeBinder;

using Poly.Analysis;
using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Review F2 (lock amendment 4): does the VM execute a <c>char?</c> operand beside a number correctly? Every cell the
/// lowered analysis's <c>NullableCharRule</c> decides is run on the interpreter with that rule's error alone bypassed
/// (the analysis is otherwise exactly what compile sees), and compared with the C# binder: its verdict on the
/// unwrapped operands, the lifted result type, and the value for present and absent operands. Cells whose verdict
/// another rule would still decide are named. Each class is pinned by count and by a checksum of its cell keys.
/// </summary>
internal static class NullableCharGrid {
    public sealed record Cell(Type Left, OperatorKind Op, Type Right, string Class) {
        public string Key => $"{ExtraOperandGrid.Name(Left)} {Op} {ExtraOperandGrid.Name(Right)}";
    }

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool Arithmetic(OperatorKind kind) =>
        kind is OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide or OperatorKind.Modulo;

    /// <summary>The rule's own predicate, written independently of the analyzer.</summary>
    private static bool RuleApplies(OperatorKind kind, Type left, Type right) {
        var (l, r) = (left == typeof(char?), right == typeof(char?));
        if (!l && !r)
            return false;
        static bool Number(Type t) => Unwrap(t) is var u && (u.IsPrimitive && u != typeof(bool) && u != typeof(char) || u == typeof(decimal));
        return Arithmetic(kind) || (l && Number(right)) || (r && Number(left));
    }

    /// <summary>The probe values per unwrapped operand type: a stress value and a plain one.</summary>
    private static readonly Dictionary<Type, (object Stress, object Plain)> Values = BuildValues();

    private static Dictionary<Type, (object, object)> BuildValues() {
        var values = OperatorOracle.Operands.Where(static o => o.Clr is not null && o.Stress is not null)
            .ToDictionary(static o => o.Clr!, static o => (o.Stress!, o.Plain!));
        values[typeof(IntPtr)] = ((nint)int.MaxValue, (nint)7);
        values[typeof(UIntPtr)] = ((nuint)uint.MaxValue, (nuint)7);
        return values;
    }

    /// <summary>The message the rule reports, which compile surfaces.</summary>
    private static string RuleMessage(OperatorKind kind, Type left, Type right) => Arithmetic(kind)
        ? $"arithmetic operand is not numeric (got '{left.Name}' and '{right.Name}')"
        : $"comparison between incompatible types '{left.Name}' and '{right.Name}'";

    public static readonly Lazy<IReadOnlyList<Cell>> Cells = new(() => {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var cells = new List<Cell>();
            foreach (var kind in ExtraOperandGrid.Kinds)
                foreach (var left in ExtraOperandGrid.Operands)
                    foreach (var right in ExtraOperandGrid.Operands) {
                        if (!RuleApplies(kind, left, right))
                            continue;
                        var resolution = Clr(Unwrap(left)).GetOperator(kind, Clr(Unwrap(right)));
                        if (resolution is null)
                            continue;
                        var node = ExtraOperandGrid.Build(kind, new Parameter("a", new ClrTypeReference(left)), new Parameter("b", new ClrTypeReference(right)));
                        var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
                        var errors = analysis.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
                        // Only cells the rule decides: string concatenation and enum comparisons are judged before it.
                        if (errors.Length == 0 || errors.Length > 1 || errors[0].Message != RuleMessage(kind, left, right))
                            continue;
                        cells.Add(new Cell(left, kind, right, Classify(kind, left, right, node, analysis, resolution.Value)));
                    }
            return cells;
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    });

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    private static string Classify(OperatorKind kind, Type left, Type right, Node node, AnalysisResult analysis, OperatorResolution resolution) {
        if (Arithmetic(kind) && !resolution.PlatformApplied
            && !(resolution.Signature.DeclaringTypeDefinition.TryGetRuntimeType(out var declaring) && declaring == typeof(decimal)))
            return "still rejected without the rule: VmExecutesDeclaredOperator";
        VmProgram program;
        try {
            program = AbiEmitter.Emit(node, analysis, CompilationMode.Normal);
        }
        catch (Exception ex) {
            return $"VM refuses: {ex.GetType().Name}";
        }
        var resultType = resolution.Result.GetRuntimeType()!;
        var expectedType = Arithmetic(kind) ? typeof(Nullable<>).MakeGenericType(resultType) : typeof(bool);
        var typeNote = program.RootClrType == expectedType
            ? ""
            : $"VM result type {Display(program.RootClrType)}, C# {Display(expectedType)}; ";
        foreach (var (l, r) in Arguments(left, right)) {
            var expected = Binder(kind, l, r);
            if (expected.StartsWith("binder rejects", StringComparison.Ordinal))
                return expected;
            string actual;
            try {
                using var exec = Interpreter.Execute(program, s => s.SetArgs(l, r));
                actual = kind is OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
                    or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual
                    ? Format(exec.RawValue != 0)
                    : exec.Result.Value is null ? "null" : Format(typeof(ExecutionResult).GetMethod(nameof(ExecutionResult.GetValue))!.MakeGenericMethod(resultType).Invoke(exec, null));
            }
            catch (Exception ex) {
                return $"VM throws: {ex.GetType().Name}";
            }
            if (actual != expected)
                return typeNote + "VM value differs";
        }
        return typeNote.Length == 0 ? "VM agrees with C#" : typeNote + "values agree";
    }

    private static string Display(Type? type) =>
        type is null ? "?" : Nullable.GetUnderlyingType(type) is { } u ? u.Name + "?" : type.Name;

    /// <summary>Stress beside plain, plain beside stress, then each nullable operand absent.</summary>
    private static IEnumerable<(object? Left, object? Right)> Arguments(Type left, Type right) {
        var (lv, rv) = (Values[Unwrap(left)], Values[Unwrap(right)]);
        yield return (lv.Stress, rv.Plain);
        yield return (lv.Plain, rv.Stress);
        if (Nullable.GetUnderlyingType(left) is not null)
            yield return (null, rv.Plain);
        if (Nullable.GetUnderlyingType(right) is not null)
            yield return (lv.Plain, null);
    }

    /// <summary>The C# binder on the operand values, lifted: an absent operand gives null arithmetic, equality only with absence, and false ordering.</summary>
    private static string Binder(OperatorKind kind, object? l, object? r) {
        if (l is null || r is null) {
            return kind switch {
                OperatorKind.Equal => Format(l is null && r is null),
                OperatorKind.NotEqual => Format(!(l is null && r is null)),
                OperatorKind.LessThan or OperatorKind.LessThanOrEqual or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual => Format(false),
                _ => "null"
            };
        }
        try {
            dynamic x = l;
            dynamic y = r;
            object result = kind switch {
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
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return Format(result);
        }
        catch (RuntimeBinderException) {
            return "binder rejects the pair";
        }
        catch (Exception ex) {
            return $"binder throws {ex.GetType().Name}";
        }
    }

    private static string Format(object? value) => value switch {
        null => "null",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };
}

public class NullableCharProofTests {
    private static Parameter Param<T>(string name) => new(name, TypeReference.To<T>());

    /// <summary>The emitted program for a tree whose only analysis error is the rule's, so the VM runs what it would run without the rule.</summary>
    private static async Task<long> RunBypassingTheRule(Node node, string ruleMessage, params object?[] args) {
        var analysis = Interpreter.Analyze(node);
        var errors = analysis.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Message).ToArray();
        await Assert.That(errors).IsEquivalentTo([ruleMessage]);
        using var exec = Interpreter.Execute(AbiEmitter.Emit(node, analysis, CompilationMode.Normal), s => s.SetArgs(args));
        return exec.Result.Value is null ? long.MinValue : exec.RawValue;
    }

    private const string GreaterMessage = "comparison between incompatible types 'Nullable`1' and 'Int32'";
    private const string AddMessage = "arithmetic operand is not numeric (got 'Nullable`1' and 'Int32')";

    [Test]
    public async Task Probe_NullableCharPresent_GreaterThanInt_IsTrue() =>
        await Assert.That(await RunBypassingTheRule(new GreaterThan(Param<char?>("c"), Param<int>("n")), GreaterMessage, 'a', 96)).IsEqualTo(1L);

    [Test]
    public async Task Probe_NullableCharAbsent_GreaterThanInt_IsFalse() =>
        await Assert.That(await RunBypassingTheRule(new GreaterThan(Param<char?>("c"), Param<int>("n")), GreaterMessage, null, 96)).IsEqualTo(0L);

    [Test]
    public async Task Probe_NullableCharPresent_EqualInt_IsTrue() =>
        await Assert.That(await RunBypassingTheRule(new Equal(Param<char?>("c"), Param<int>("n")), "comparison between incompatible types 'Nullable`1' and 'Int32'", 'a', 97)).IsEqualTo(1L);

    [Test]
    public async Task Probe_NullableCharPresent_PlusInt_Is98() {
        var node = new Add(Param<char?>("c"), Param<int>("n"));
        var analysis = Interpreter.Analyze(node);
        await Assert.That(analysis.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Message).ToArray()).IsEquivalentTo([AddMessage]);
        using var exec = Interpreter.Execute(AbiEmitter.Emit(node, analysis, CompilationMode.Normal), s => s.SetArgs('a', 1));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(98);
    }

    [Test]
    public async Task Probe_NullableCharAbsent_PlusInt_IsAbsent() {
        var node = new Add(Param<char?>("c"), Param<int>("n"));
        var analysis = Interpreter.Analyze(node);
        using var exec = Interpreter.Execute(AbiEmitter.Emit(node, analysis, CompilationMode.Normal), s => s.SetArgs(null, 1));
        await Assert.That(exec.Result.Value).IsNull();
    }

    /// <summary>Count and key checksum per class over every cell the rule decides.</summary>
    /// <summary>
    /// Count and key checksum per class over every cell the rule decides (513). Every cell that would run without the
    /// rule matches the C# binder's verdict and value. 80 small-integer cells state the operand's type as the result
    /// (`Byte?` where C# says `Int32?`): the nullable variant of the ledger's T-17 `type: analysis` and T-21 `type: VM`
    /// cells, whose non-nullable siblings (`byte + char`) the analysis accepts. 14 IntPtr cells stay rejected by
    /// `VmExecutesDeclaredOperator` with or without the rule.
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Checksum)> ExpectedClasses = new(StringComparer.Ordinal) {
        ["VM agrees with C#"] = (419, "7e7f9f61fbdda971"),
        ["VM result type Byte?, C# Int32?; values agree"] = (20, "7a49cb56b4a77f07"),
        ["VM result type Int16?, C# Int32?; values agree"] = (20, "f939a2362ca5604a"),
        ["VM result type SByte?, C# Int32?; values agree"] = (20, "12cce72358c0b831"),
        ["VM result type UInt16?, C# Int32?; values agree"] = (20, "56aa713b2e2de5c2"),
        ["still rejected without the rule: VmExecutesDeclaredOperator"] = (14, "9d401c19b653b1db"),
    };

    [Test]
    public async Task EveryCellTheRuleDecides_RunOnTheVm_ComparedWithTheCSharpBinder() {
        var cells = NullableCharGrid.Cells.Value;
        var actual = cells.GroupBy(static c => c.Class, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => (Count: g.Count(), Checksum: LedgerCheck.Checksum(g.Select(static c => c.Key))), StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (name, found) in actual.OrderBy(static c => c.Key, StringComparer.Ordinal)) {
            if (!ExpectedClasses.TryGetValue(name, out var expected))
                problems.Add($"unpinned class [\"{name}\"] = ({found.Count}, \"{found.Checksum}\"): {string.Join("; ", cells.Where(c => c.Class == name).Take(6).Select(static c => c.Key))}");
            else if (expected != found)
                problems.Add($"class [\"{name}\"]: expected ({expected.Count}, \"{expected.Checksum}\"), actual ({found.Count}, \"{found.Checksum}\")");
        }
        foreach (var name in ExpectedClasses.Keys.Where(k => !actual.ContainsKey(k)))
            problems.Add($"class [\"{name}\"] expected but empty");
        await Assert.That(problems).IsEmpty();
        await Assert.That(cells.Count).IsEqualTo(513);
    }
}