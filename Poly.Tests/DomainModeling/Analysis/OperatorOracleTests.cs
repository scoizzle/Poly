using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.DomainModeling.Analysis;

internal enum OracleKind {
    AnalyzerDisagrees,
    LoweredAnalysisDisagrees,
    AbiDivergesFromTree,
    AbiRejectsDefined,
    /// <summary>The tree defines a cell C# rejects, by a named permanent Poly policy (T-22: the shift count).</summary>
    PolicyDefinesWhatCSharpRejects
}

/// <summary>
/// One operand definition and the values the VM column feeds it. <c>Stress</c> exposes 32-bit wrap, 2^53+1, decimal exactness.
/// <c>ClrOnly</c> operands have no domain name: their definition is the CLR type's (T-24's small, unsigned, char and Float32).
/// </summary>
internal sealed record OracleOperand(string Name, string? DslType, Type? Clr, object? Stress, object? Plain, bool ClrOnly = false);

internal sealed record OracleDivergence(OracleKind Kind, string Reason);

/// <summary>
/// One operator cell in three columns (R6): what the interface states (the operator, its result, the operand
/// conversions), what the lowered-tree analysis resolves, and what the VM's ABI executes.
/// </summary>
internal sealed record OracleCell(
    string Left, OperatorKind Op, string? Right,
    string Tree, string Analyzer, string Lowered, string Abi,
    IReadOnlyList<OracleDivergence> Divergences) {
    public string Key => $"{Left} {Op} {Right}";
}

internal static class OperatorOracle {
    public static readonly OracleOperand[] Operands = [
        new("Number", "Number", typeof(long), 9007199254740993L, 7L),
        new("Int32", null, typeof(int), int.MaxValue, 7),
        new("Decimal", "Decimal", typeof(decimal), 0.1m, 0.2m),
        new("Float", null, typeof(double), 0.1d, 2.5d),
        // 2^24 + 1.5 is not a Float32; every stress value converts to Float32 the same way on every architecture.
        new("Float32", null, typeof(float), 16777216f, 1.5f, ClrOnly: true),
        new("Int8", null, typeof(sbyte), sbyte.MaxValue, (sbyte)7, ClrOnly: true),
        new("Int16", null, typeof(short), short.MaxValue, (short)7, ClrOnly: true),
        new("UInt8", null, typeof(byte), byte.MaxValue, (byte)7, ClrOnly: true),
        new("UInt16", null, typeof(ushort), ushort.MaxValue, (ushort)7, ClrOnly: true),
        new("UInt32", null, typeof(uint), uint.MaxValue, 7u, ClrOnly: true),
        new("UInt64", null, typeof(ulong), ulong.MaxValue, 7UL, ClrOnly: true),
        new("Char", null, typeof(char), 'a', 'b', ClrOnly: true),
        new("Text", "Text", typeof(string), "ab", "cd"),
        new("Boolean", "Boolean", typeof(bool), true, false),
        new("Date", "Date", typeof(DateOnly), new DateOnly(2024, 2, 28), new DateOnly(2024, 3, 1)),
        new("Time", "Time", typeof(TimeOnly), new TimeOnly(10, 0), new TimeOnly(11, 30)),
        new("DateTime", "DateTime", typeof(DateTime), new DateTime(2024, 2, 28, 10, 0, 0, DateTimeKind.Utc), new DateTime(2024, 3, 1, 8, 0, 0, DateTimeKind.Utc)),
        new("Duration", "Duration", typeof(TimeSpan), TimeSpan.FromHours(1), TimeSpan.FromMinutes(90)),
        new("Uuid", "Uuid", typeof(Guid), Guid.Parse("00000000-0000-0000-0000-0000000000a1"), Guid.Parse("00000000-0000-0000-0000-0000000000b2")),
        new("Status", "Status", null, null, null),
        new("Order", "Order", null, null, null),
        new("Money", "Money", null, null, null)
    ];

    /// <summary>
    /// T-18b: the native integers. A wrap at the top and a negative; a nuint at or above 2^63, which a signed word reads as
    /// negative. Apart from <see cref="Operands"/>, which the constant-fold grid and the extra-operand grid also read, so
    /// their grids stay the 22-operand ones.
    /// </summary>
    public static readonly OracleOperand[] NativeOperands = [
        new("NInt", null, typeof(nint), nint.MaxValue, (nint)(-7), ClrOnly: true),
        new("NUInt", null, typeof(nuint), unchecked((nuint)0x8000_0000_0000_0005UL), (nuint)7, ClrOnly: true)
    ];

    /// <summary>The ledger's operands: the 22, then the native integers.</summary>
    public static readonly OracleOperand[] GridOperands = [.. Operands, .. NativeOperands];

    private static readonly Poly.DomainModeling.Meaning.DomainTypeModel Model = OperatorOperands.Model();

    /// <summary>The grid, under the invariant culture: the checksums hash reason strings, and concatenation formats values.</summary>
    public static readonly Lazy<(IReadOnlyList<OracleCell> Cells, TimeSpan Elapsed)> Run = new(() => {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var watch = Stopwatch.StartNew();
            var cells = new List<OracleCell>();
            foreach (var kind in Enum.GetValues<OperatorKind>()) {
                foreach (var left in GridOperands) {
                    if (OperatorShape.ArityOf(kind) == 1) {
                        cells.Add(Evaluate(left, kind, null));
                        continue;
                    }
                    foreach (var right in GridOperands)
                        cells.Add(Evaluate(left, kind, right));
                }
            }
            watch.Stop();
            return (cells, watch.Elapsed);
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    });

    private static ITypeDefinition Def(OracleOperand operand) =>
        operand.ClrOnly ? OperatorOperands.Clr(operand.Clr!) : Model.Resolve(operand.Name).Definition!;

    private static OracleCell Evaluate(OracleOperand left, OperatorKind kind, OracleOperand? right) {
        var divergences = new List<OracleDivergence>();
        var resolution = Def(left).GetOperator(kind, right is null ? null : Def(right));
        var tree = DescribeTree(left, right, resolution);

        // Poly's shift-count policy (ClrBuiltInRules.IsPolicyShiftCount): the tree defines the cell, and C# (Roslyn for a
        // native operand, the runtime binder otherwise) rejects it. That is the policy's whole difference from C#.
        if (resolution is not null && IsPolicyShiftCell(kind, right) && CSharpRejectsShift(kind, left.Clr!, right!.Clr!))
            divergences.Add(new(OracleKind.PolicyDefinesWhatCSharpRejects, "C# rejects a shift count that is not int; Poly converts it to int unchecked"));

        var analyzer = AnalyzerVerdict(left, kind, right);
        if (analyzer is "accepts" or "rejects" && (analyzer == "accepts") != (resolution is not null))
            divergences.Add(new(OracleKind.AnalyzerDisagrees, $"analyzer {analyzer}, tree {(resolution is { } r ? r.Result.Name : "none")}"));

        var lowered = "n/a";
        var abi = "n/a";
        if (kind is OperatorKind.UnaryPlus)
            (lowered, abi) = ("n/a: no UnaryPlus node", "n/a: no UnaryPlus node");
        else if (left.Clr is not null && (right is null || right.Clr is not null))
            (lowered, abi) = RunVm(left, kind, right, resolution, divergences);

        return new OracleCell(left.Name, kind, right?.Name, tree, analyzer ?? "n/a", lowered, abi, divergences);
    }

    /// <summary>A shift whose count is one of the policy's wider integer types: long, uint, ulong, nint, nuint.</summary>
    private static bool IsPolicyShiftCell(OperatorKind kind, OracleOperand? right) =>
        kind is OperatorKind.ShiftLeft or OperatorKind.ShiftRight
        && right?.Clr is { } count && count.GetPrimitiveType() is { } primitive && ClrBuiltInRules.IsPolicyShiftCount(primitive);

    /// <summary>C#'s verdict on the shift, from Roslyn for a native operand and from the runtime binder otherwise.</summary>
    private static bool CSharpRejectsShift(OperatorKind kind, Type left, Type count) {
        if (Poly.Tests.Introspection.CSharpOperatorOracle.IsNativeForm(left) || Poly.Tests.Introspection.CSharpOperatorOracle.IsNativeForm(count))
            return !Poly.Tests.Introspection.CSharpOperatorOracle.Answer(kind, left, count).Defined;
        try {
            dynamic x = Activator.CreateInstance(left)!;
            dynamic y = Activator.CreateInstance(count)!;
            _ = kind is OperatorKind.ShiftLeft ? x << y : x >> y;
            return false;
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
            return true;
        }
    }

    /// <summary>The policy's count: the value converted to int unchecked.</summary>
    private static int PolicyCount(object count) => count switch {
        long value => unchecked((int)value),
        uint value => unchecked((int)value),
        ulong value => unchecked((int)value),
        nint value => unchecked((int)value),
        nuint value => unchecked((int)value),
        _ => throw new ArgumentOutOfRangeException(nameof(count), count, "not a policy shift count")
    };

    private static string DescribeTree(OracleOperand left, OracleOperand? right, OperatorResolution? resolution) {
        if (resolution is not { } found)
            return "none";
        var operands = right is null ? new[] { left } : new[] { left, right };
        var parameters = found.Signature.Parameters.ToArray();
        var conversions = new List<string>();
        for (var i = 0; i < operands.Length; i++) {
            var operand = Def(operands[i]);
            var parameter = parameters[i].ParameterTypeDefinition;
            conversions.Add(ReferenceEquals(parameter, operand)
                ? "id"
                : parameter.ResolveConversionFrom(operand) is { } c ? $"{c.Kind}{(c.Method is null ? "/intrinsic" : "/declared")}" : "?");
        }
        return $"{found.Result.Name}{(found.PlatformApplied ? " [platform]" : " [declared]")} conv({string.Join(",", conversions)})";
    }

    /// <summary>The DSL spelling, or null for a kind the platform defines and the DSL has no syntax for.</summary>
    private static string? DslOp(OperatorKind kind) => kind switch {
        OperatorKind.Add => "+",
        OperatorKind.Subtract => "-",
        OperatorKind.Multiply => "*",
        OperatorKind.Divide => "/",
        OperatorKind.Equal => "==",
        OperatorKind.NotEqual => "!=",
        OperatorKind.LessThan => "<",
        OperatorKind.LessThanOrEqual => "<=",
        OperatorKind.GreaterThan => ">",
        OperatorKind.GreaterThanOrEqual => ">=",
        OperatorKind.And => "and",
        OperatorKind.Or => "or",
        OperatorKind.Not => "not",
        OperatorKind.Modulo or OperatorKind.UnaryMinus or OperatorKind.UnaryPlus
            or OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor or OperatorKind.BitwiseNot
            or OperatorKind.ShiftLeft or OperatorKind.ShiftRight => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>"accepts" or "rejects" from the domain analyzer, or null when the domain cannot express an operand (no Int32 or Float type) or the operator.</summary>
    private static string? AnalyzerVerdict(OracleOperand left, OperatorKind kind, OracleOperand? right) {
        if (left.DslType is null || (right is not null && right.DslType is null) || DslOp(kind) is not { } op)
            return null;
        var expression = right is null ? $"{op} L" : $"L {op} R";
        var properties = right is null ? $"L: {left.DslType}" : $"L: {left.DslType}\n  R: {right.DslType}";
        var dsl = $$"""
            domain Test
            Status: enum { On, Off }
            Money: value { Amount: Decimal }
            Order: entity { Total: Decimal }
            E: entity {
              {{properties}}
              P: policy { {{expression}} }
            }
            """;
        try {
            var changes = new PolyDslParser(dsl).Parse();
            var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
            return result.Succeeded ? "accepts" : "rejects";
        }
        catch (Exception ex) {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static Node Build(OperatorKind kind, Node left, Node? right) => kind switch {
        OperatorKind.Add => new Add(left, right!),
        OperatorKind.Subtract => new Subtract(left, right!),
        OperatorKind.Multiply => new Multiply(left, right!),
        OperatorKind.Divide => new Divide(left, right!),
        OperatorKind.Equal => new Equal(left, right!),
        OperatorKind.NotEqual => new NotEqual(left, right!),
        OperatorKind.LessThan => new LessThan(left, right!),
        OperatorKind.LessThanOrEqual => new LessThanOrEqual(left, right!),
        OperatorKind.GreaterThan => new GreaterThan(left, right!),
        OperatorKind.GreaterThanOrEqual => new GreaterThanOrEqual(left, right!),
        OperatorKind.And => new And(left, right!),
        OperatorKind.Or => new Or(left, right!),
        OperatorKind.Not => new Not(left),
        OperatorKind.Modulo => new Modulo(left, right!),
        OperatorKind.BitwiseAnd => new BitwiseAnd(left, right!),
        OperatorKind.BitwiseOr => new BitwiseOr(left, right!),
        OperatorKind.BitwiseXor => new BitwiseXor(left, right!),
        OperatorKind.ShiftLeft => new ShiftLeft(left, right!),
        OperatorKind.ShiftRight => new ShiftRight(left, right!),
        OperatorKind.UnaryMinus => new UnaryMinus(left),
        OperatorKind.BitwiseNot => new BitwiseNot(left),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string Format(object? value) => value switch {
        null => "null",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    private static string Short(string text) => text.Length <= 70 ? text : text[..70];

    private static (string Lowered, string Abi) RunVm(
        OracleOperand left, OperatorKind kind, OracleOperand? right, OperatorResolution? resolution, List<OracleDivergence> divergences) {
        var a = new Parameter("a", new ClrTypeReference(left.Clr!));
        var b = right is null ? null : new Parameter("b", new ClrTypeReference(right.Clr!));
        var node = Build(kind, a, b);
        var expectedType = resolution is { } found && found.Result.TryGetRuntimeType(out var tree) ? tree : null;

        // Lowered-tree analysis, read once.
        AnalysisResult analysis;
        try {
            analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
        }
        catch (Exception ex) {
            divergences.Add(new(OracleKind.LoweredAnalysisDisagrees, $"analysis throws {ex.GetType().Name}"));
            return ($"throws {ex.GetType().Name}", "not run");
        }
        var error = analysis.Diagnostics.FirstOrDefault(static d => d.Severity == DiagnosticSeverity.Error);
        string lowered;
        if (error is not null) {
            lowered = $"error: {Short(error.Message)}";
            if (resolution is not null)
                divergences.Add(new(OracleKind.LoweredAnalysisDisagrees, "analysis error on a defined cell"));
            return (lowered, "not run (analysis error)");
        }
        var resolved = analysis.GetResolvedType(node);
        lowered = resolved is null ? "unresolved" : resolved.Name;
        var loweredType = resolved is not null && resolved.TryGetRuntimeType(out var lt) ? lt : null;
        if (resolution is null)
            divergences.Add(new(OracleKind.LoweredAnalysisDisagrees, $"analysis accepts an undefined cell, typed {lowered}"));
        else if (loweredType != expectedType)
            divergences.Add(new(OracleKind.LoweredAnalysisDisagrees, $"type: analysis {lowered}, tree {resolution.Value.Result.Name}"));

        // The ABI: compile with that analysis, then run.
        VmProgramLike program;
        try {
            program = new VmProgramLike(Interpreter.Compile(node, analysis));
        }
        catch (Exception ex) {
            if (resolution is not null)
                divergences.Add(new(OracleKind.AbiRejectsDefined, $"emit throws {ex.GetType().Name}"));
            return (lowered, $"emit throws {ex.GetType().Name}: {Short(ex.Message)}");
        }

        var runs = new List<string>();
        var vmType = program.Program.RootClrType;
        var valueMismatches = new List<string>();
        var unchecked_ = false;
        // T-18b: a native-integer cell's word is read as the type the program states (a nuint at or above 2^63 is not a
        // negative long); when that type is not the tree's, a word that is the tree's value under the tree's type is a
        // type divergence only. The 8,800 cells without a native operand keep the raw read they were pinned with.
        var native = Poly.Tests.Introspection.CSharpOperatorOracle.IsNativeForm(left.Clr!)
            || (right?.Clr is { } rightClr && Poly.Tests.Introspection.CSharpOperatorOracle.IsNativeForm(rightClr));
        foreach (var (name, leftValue, rightValue) in ArgumentSets(left, right)) {
            try {
                var args = right is null ? new[] { leftValue } : [leftValue, rightValue];
                using var exec = Interpreter.Execute(program.Program, args);
                var actual = native ? Poly.Tests.Introspection.NativeIntegerGrid.Read(exec, vmType)
                    : vmType == typeof(bool) ? (object?)exec.GetValue<bool>() : exec.Result.Value;
                runs.Add($"{name}={Format(actual)}");
                if (resolution is { } r) {
                    if (Expected(kind, r, left.Clr!, right?.Clr, leftValue, rightValue) is not { } expected)
                        unchecked_ = true;
                    else if (Format(expected) != Format(actual)
                        && !(native && vmType != expectedType && Format(Poly.Tests.Introspection.NativeIntegerGrid.Read(exec, expectedType)) == Format(expected)))
                        valueMismatches.Add($"{name}: VM {Format(actual)}, tree {Format(expected)}");
                }
            }
            catch (Exception ex) {
                runs.Add($"{name}=throws {ex.GetType().Name}");
                if (resolution is not null)
                    divergences.Add(new(OracleKind.AbiRejectsDefined, $"run throws {ex.GetType().Name}"));
            }
        }

        var abi = $"{vmType?.Name ?? "?"}; {string.Join("; ", runs)}{(unchecked_ ? " (value unchecked)" : "")}";
        if (resolution is null) {
            divergences.Add(new(OracleKind.AbiDivergesFromTree, $"executes an undefined cell, typed {vmType?.Name}"));
        }
        else {
            var reasons = new List<string>();
            if (vmType != expectedType)
                reasons.Add($"type: VM {vmType?.Name}, tree {expectedType?.Name}");
            reasons.AddRange(valueMismatches.Select(static m => $"value {m}"));
            if (reasons.Count > 0)
                divergences.Add(new(OracleKind.AbiDivergesFromTree, string.Join("; ", reasons)));
            // The probe tree is the bare operator over two parameters: it states no conversion node, so any
            // operand conversion the interface requires was applied by the ABI on its own (R6).
            if (UnstatedConversions(left, right, r0: resolution.Value) is { Length: > 0 } unstated)
                divergences.Add(new(OracleKind.AbiDivergesFromTree, $"unstated conversion: {unstated}"));
        }
        return (lowered, abi);
    }

    private static string UnstatedConversions(OracleOperand left, OracleOperand? right, OperatorResolution r0) {
        var operands = right is null ? new[] { left } : new[] { left, right };
        var parameters = r0.Signature.Parameters.ToArray();
        var parts = new List<string>();
        for (var i = 0; i < operands.Length; i++) {
            var operand = Def(operands[i]);
            var parameter = parameters[i].ParameterTypeDefinition;
            if (!ReferenceEquals(parameter, operand))
                parts.Add($"{operands[i].Name} to {parameter.Name}");
        }
        return string.Join(", ", parts);
    }

    private readonly record struct VmProgramLike(Poly.Interpretation.Vm.VmProgram Program);

    private static IEnumerable<(string Name, object? Left, object? Right)> ArgumentSets(OracleOperand left, OracleOperand? right) {
        yield return ("S1", left.Stress, right?.Plain);
        yield return ("S2", left.Plain, right?.Stress);
    }

    /// <summary>
    /// The value the tree's statement denotes: each operand converted as the interface says, then the operator applied by C#.
    /// A native-integer cell is answered by Roslyn (T-18b lock amendment 6): the runtime binder has no native-integer rules.
    /// </summary>
    private static object? Expected(OperatorKind kind, OperatorResolution resolution, Type leftType, Type? rightType, object? leftValue, object? rightValue) {
        try {
            // Poly's shift-count policy: C#'s shift by the count converted to int unchecked (Roslyn's int-count cell for a
            // native left operand, the runtime binder otherwise).
            if (kind is OperatorKind.ShiftLeft or OperatorKind.ShiftRight
                && rightType?.GetPrimitiveType() is { } countType && ClrBuiltInRules.IsPolicyShiftCount(countType)) {
                var count = PolicyCount(rightValue!);
                if (Poly.Tests.Introspection.CSharpOperatorOracle.IsNativeForm(leftType)) {
                    var answer = Poly.Tests.Introspection.CSharpOperatorOracle.Value(kind, leftType, typeof(int), leftValue, count);
                    return answer.Throws is null ? answer.Value : null;
                }
                var promoted = resolution.Signature.Parameters.First().ParameterTypeDefinition.GetRuntimeType()!;
                dynamic shifted = System.Convert.ChangeType(leftValue is char c0 ? (int)c0 : leftValue!, promoted, CultureInfo.InvariantCulture);
                return kind is OperatorKind.ShiftLeft ? shifted << count : shifted >> count;
            }
            if (Poly.Tests.Introspection.CSharpOperatorOracle.IsNativeForm(leftType) || (rightType is not null && Poly.Tests.Introspection.CSharpOperatorOracle.IsNativeForm(rightType))) {
                var answer = Poly.Tests.Introspection.CSharpOperatorOracle.Value(kind, leftType, rightType, leftValue, rightValue);
                return answer.Throws is null ? answer.Value : null;
            }
            var parameters = resolution.Signature.Parameters.Select(static p => p.ParameterTypeDefinition).ToArray();
            // Into Object is boxing. Char goes through Int32: ChangeType has no char to float, double or decimal.
            static object? Convert(object? value, ITypeDefinition target) =>
                value is null || !target.TryGetRuntimeType(out var type) || value.GetType() == type || type == typeof(object)
                    ? value
                    : System.Convert.ChangeType(value is char c ? (int)c : value, type, CultureInfo.InvariantCulture);
            dynamic x = Convert(leftValue, parameters[0])!;
            dynamic? y = parameters.Length > 1 ? Convert(rightValue, parameters[1]) : null;
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
                OperatorKind.And => (bool)x && (bool)y!,
                OperatorKind.Or => (bool)x || (bool)y!,
                OperatorKind.Not => !(bool)x,
                OperatorKind.BitwiseAnd => x & y,
                OperatorKind.BitwiseOr => x | y,
                OperatorKind.BitwiseXor => x ^ y,
                OperatorKind.ShiftLeft => x << y,
                OperatorKind.ShiftRight => x >> y,
                OperatorKind.UnaryMinus => -x,
                OperatorKind.BitwiseNot => ~x,
                _ => null
            };
        }
        catch (Exception) {
            return null;
        }
    }
}

/// <summary>
/// An owner row for a divergence, matched by kind, operand names, operator and the reason: <see cref="ReasonPattern"/>
/// is a regular expression found in the reason (a plain fragment matches as text; <c>^</c> and <c>$</c> anchor it).
/// <c>*</c> matches anything. <see cref="ExpectedCount"/> is the number of divergences the row owns: a fixing slice
/// edits the number or deletes the row, and growth fails as surely as shrinkage.
/// </summary>
internal sealed record LedgerRule(OracleKind Kind, string Lefts, string Ops, string Rights, string ReasonPattern, string Owner, int ExpectedCount, string Checksum = "") {
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    public bool Matches(OracleCell cell, OracleDivergence divergence) =>
        divergence.Kind == Kind
        && In(Lefts, cell.Left) && In(Ops, cell.Op.ToString()) && In(Rights, cell.Right ?? "-")
        && Patterns.GetOrAdd(ReasonPattern, static pattern => new Regex(pattern, RegexOptions.CultureInvariant)).IsMatch(divergence.Reason);

    /// <summary>
    /// What the checksum hashes for one owned divergence: the cell and this rule's kind and fragment, never the reason
    /// text, so the values the VM computed (which may differ by architecture) stay out of the cell identity.
    /// </summary>
    public string KeyOf(OracleCell cell) => $"{cell.Key}|{Kind}|{ReasonPattern}";

    public string Describe() => $"{Kind} [{Lefts}] [{Ops}] [{Rights}] \"{ReasonPattern}\" -> {Owner}";

    private static bool In(string set, string value) => set == "*" || set.Split(',').Contains(value);
}

internal static class LedgerCheck {
    /// <summary>Order-independent, machine-independent fingerprint of the cells a rule owns: SHA-256 over the sorted <see cref="LedgerRule.KeyOf"/> keys, 16 hex characters.</summary>
    public static string Checksum(IEnumerable<string> keys) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", keys.Order(StringComparer.Ordinal))))).ToLowerInvariant()[..16];

    private const string Instruction = "a fixing slice lowers ExpectedCount and updates the checksum, or deletes the rule when it reaches 0; growth is a regression or a new divergence that needs an owner";

    /// <summary>
    /// The ratchet: every divergence has exactly one owning rule, and every rule owns exactly its expected count.
    /// Returns the problems; empty means the ledger is exact.
    /// </summary>
    public static IReadOnlyList<string> Compare(IEnumerable<(OracleCell Cell, OracleDivergence Divergence)> divergences, IReadOnlyList<LedgerRule> rules) {
        var problems = new List<string>();
        var counts = rules.ToDictionary(static rule => rule, static _ => 0);
        var keys = rules.ToDictionary(static rule => rule, static _ => new List<string>());
        foreach (var (cell, divergence) in divergences) {
            var owners = rules.Where(rule => rule.Matches(cell, divergence)).ToArray();
            if (owners.Length == 0)
                problems.Add($"unowned: {cell.Key}: {divergence.Reason}");
            else if (owners.Length > 1)
                problems.Add($"claimed by {owners.Length} rules: {cell.Key}: {divergence.Reason}");
            foreach (var owner in owners) {
                counts[owner]++;
                keys[owner].Add(owner.KeyOf(cell));
            }
        }
        foreach (var rule in rules) {
            if (rule.ExpectedCount <= 0)
                problems.Add($"rule expects no cells (delete it): {rule.Describe()}");
            else if (counts[rule] != rule.ExpectedCount)
                problems.Add($"rule {rule.Describe()}: expected {rule.ExpectedCount}, matched {counts[rule]} (checksum {Checksum(keys[rule])}); {Instruction}");
            else if (Checksum(keys[rule]) != rule.Checksum)
                problems.Add($"rule {rule.Describe()}: same count {counts[rule]} but the cells changed: expected checksum {rule.Checksum}, actual {Checksum(keys[rule])}; {Instruction}");
        }
        return problems;
    }
}

/// <summary>
/// The T-03b operator cross-check ledger. Every (left, operator, right) cell over the CLR-backed and modeled
/// operands is read in three columns: what the interface states, what the lowered-tree analysis resolves, and
/// what the VM's ABI executes, with probe values that expose 32-bit wrap, 2^53+1, decimal exactness and integer
/// truncation. A divergence is one of four kinds and must be owned by the row that removes it.
/// </summary>
public class OperatorOracleTests {
    private const string Comparisons = "Equal,NotEqual,LessThan,LessThanOrEqual,GreaterThan,GreaterThanOrEqual";
    private const string Ordering = "LessThan,LessThanOrEqual,GreaterThan,GreaterThanOrEqual";
    private const string Arithmetic = "Add,Subtract,Multiply,Divide,Modulo";

    private const string AllButOrder = "Number,Int32,Decimal,Float,Text,Boolean,Date,Time,DateTime,Duration,Uuid,Status,Money";

    private static readonly LedgerRule[] AnalyzerRules = [
        // T-05b: an entity is a family compared by name, so the 203 cells the analyzer accepted beside an entity (T-05's,
        // mislabelled T-06) close; entity ordering stays accepted, an interim rule T-07 removes.
        new(OracleKind.AnalyzerDisagrees, "Order", Ordering, "Order", "analyzer accepts", "T-07 (ordering legality: entities)", 4, "6e8a8b7f1e28e52c"),
        // Enum member literals compared as Text, and enum ordering.
        new(OracleKind.AnalyzerDisagrees, "Status", Comparisons, "Text", "analyzer accepts", "T-07 (enum member literals)", 6, "82d668327422d63d"),
        new(OracleKind.AnalyzerDisagrees, "Text", Comparisons, "Status", "analyzer accepts", "T-07 (enum member literals)", 6, "79e5d5c94c300ad5"),
        new(OracleKind.AnalyzerDisagrees, "Status", Ordering, "Status", "analyzer accepts", "T-07 (ordering legality)", 4, "cd41fe5cc653aa1f"),
        // Same-category pairs the analyzer accepts and no platform defines.
        new(OracleKind.AnalyzerDisagrees, "Text,Boolean", Ordering, "Text,Boolean", "analyzer accepts", "T-07 (#631 ordering legality)", 8, "91cd7271c7004160"),
        // Temporal cells: the analyzer's offset permission and its rejection of CLR-defined duration arithmetic.
        new(OracleKind.AnalyzerDisagrees, "Date,DateTime", "Add,Subtract", "Date,DateTime,Decimal,Number,Duration", "analyzer accepts", "T-10 (temporal offset permission)", 11, "edbb26e174a06a9f"),
        new(OracleKind.AnalyzerDisagrees, "Duration,Time,Number", "*", "Duration,Time,Number", "analyzer rejects", "T-10 (temporal operands the CLR defines)", 7, "0409473254198adb"),
        // Permanent: C# defines string + object, and Poly allows only Text + Text. A named language policy, not a gap.
        new(OracleKind.AnalyzerDisagrees, "*", "Add", "*", "analyzer rejects, tree String", "T-07 (language policy: only Text + Text concatenates)", 16, "a66331455543d1e9")
    ];

    // The lowered analysis reads the provider for arithmetic and comparison (T-18): an undefined Decimal-with-X, UInt64
    // with a signed operand or Decimal-with-Float cell is rejected there. The domain DSL has no Float, so the analyzer
    // column is n/a for those; the remaining owners are the lowered analysis (T-07, T-10) and the ABI (T-21). T-22a judges bitwise, shift and unary minus through the provider: its 1,785 accepted undefined cells and 263 type cells are gone. T-17 typed every arithmetic node through the provider and deleted the arithmetic type rule (240 cells: the 150 of the 19 CLR operands and 90 native-integer pairs); the stated type now is C#'s.
    private static readonly LedgerRule[] LoweredRules = [
        new(OracleKind.LoweredAnalysisDisagrees, "*", "*", "*", "analysis error on a defined cell", "T-21 (the ABI cannot execute declared temporal and Duration-scalar arithmetic: VmExecutesDeclaredOperator); 7 temporal cells with T-10; T-18b adds 6, Duration with nint or nuint (T-21: 'the rule analysis error on a defined cell is about 46 (Duration × native integers)')", 46, "ec7a9e70d97b4629"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "Add,Subtract", "*", "typed DateOnly", "T-10 (RewriteDateOffset); T-18b adds Date ± nint or nuint, 4 (T-10: 'T-18b adds 8 Date/DateTime ± nint/nuint cells to the date fence's accepted-undefined line')", 24, "6782a41016a1baec"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "Add,Subtract", "*", "typed DateTime", "T-10 (RewriteDateOffset); T-18b adds DateTime ± nint or nuint, 4 (T-10: 'T-18b adds 8 Date/DateTime ± nint/nuint cells to the date fence's accepted-undefined line')", 24, "1ba415e2415d00aa"),
        // Text and Boolean ordering: the lowered analysis keeps accepting it (TextAndBooleanOrderingRule); the DSL and the interpreter order Text.
        new(OracleKind.LoweredAnalysisDisagrees, "Text,Boolean", Ordering, "Text,Boolean", "typed Boolean", "T-07 (#631 ordering legality)", 8, "a2d892f80f998382")
    ];

    private static readonly LedgerRule[] AbiRules = [
        // T-22a rejects the undefined bitwise, shift and unary-minus cells: what still executes is the 48 Date/DateTime offset
        // cells (T-10) and the 4 Text ordering cells (T-07; Boolean ordering the emitter refuses).
        new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "executes an undefined cell", "T-21 (the ABI must reject what the tree does not define); T-18b adds 231 native-integer cells: the accepted-undefined bitwise, shift and date cells above that the VM runs; T-22a leaves the date and Text-ordering cells", 52, "536f37f3fdec568c"),
        // T-22a adds 50: the shift-count policy cells of a small integer or char left, whose left converts to Int32 as for an int count.
        new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "unstated conversion", "T-21 (T-04 and Lower state the conversion node; the emitter stops converting); T-18b adds 520 native-integer cells (T-18b lock amendment 7: 'its unstated conversion is then T-21's ledger line'); T-22a adds the 50 shift-count policy cells of a small left", 2299, "0e5ea4c8443835bf"),
        // Wrong values (issue 832; -Decimal and -Duration give -1). T-22a states the bitwise and shift type, so the 255 type-only and
        // 8 type-and-value cells of the emitter's own promotion are gone: the 8 keep their value line, and 8 shift-count policy cells
        // of a uint or ulong left join it with the value master computed (the uint and ulong shift of an int count is on this line too).
        new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "^value S", "T-21 (issue 832: wrong values)", 131, "f28a43b234dbae1f")
    ];

    private static readonly LedgerRule[] RejectedDefinedRules = [];

    /// <summary>
    /// Permanent, not a gap (owner ruling, #677, 2026-11-13): Poly accepts a shift count of any built-in integer type and
    /// converts it to int unchecked (<c>ClrBuiltInRules.IsPolicyShiftCount</c>); C# accepts only a count that converts
    /// implicitly to int. The 11 integral lefts by the 5 wider counts by the 2 shifts.
    /// </summary>
    private static readonly LedgerRule[] PolicyRules = [
        new(OracleKind.PolicyDefinesWhatCSharpRejects, "*", "ShiftLeft,ShiftRight", "Number,UInt32,UInt64,NInt,NUInt", "C# rejects a shift count", "T-22 (permanent Poly policy: a shift count of any built-in integer type, converted to int unchecked)", 110, "ac208929c6fd23eb")
    ];

    private static async Task AssertOwned(OracleKind kind, LedgerRule[] rules, int expectedTotal) {
        var (cells, _) = OperatorOracle.Run.Value;
        var divergences = cells.SelectMany(cell => cell.Divergences.Where(d => d.Kind == kind).Select(d => (cell, d))).ToArray();
        await Assert.That(LedgerCheck.Compare(divergences, rules)).IsEmpty();
        // The kind total is the sum of its rules, and both are pinned.
        await Assert.That(divergences.Length).IsEqualTo(rules.Sum(static rule => rule.ExpectedCount));
        await Assert.That(divergences.Length).IsEqualTo(expectedTotal);
    }

    [Test]
    public async Task AnalyzerDisagrees_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.AnalyzerDisagrees, AnalyzerRules, 62);

    [Test]
    public async Task LoweredAnalysisDisagrees_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.LoweredAnalysisDisagrees, LoweredRules, 102);

    [Test]
    public async Task AbiDivergesFromTree_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.AbiDivergesFromTree, AbiRules, 2482);

    [Test]
    public async Task AbiRejectsDefined_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.AbiRejectsDefined, RejectedDefinedRules, 0);

    [Test]
    public async Task PolicyDefinesWhatCSharpRejects_IsExactlyTheShiftCountPolicy() =>
        await AssertOwned(OracleKind.PolicyDefinesWhatCSharpRejects, PolicyRules, 110);

    /// <summary>
    /// T-18b: the 8,800 cells of the 22-operand grid, the ledger before the native integers, each rule's (count,
    /// checksum) on them, in rule order. A rule that grew grew only by cells with a native-integer operand.
    /// </summary>
    private static readonly (int Count, string Checksum)[] MasterLowered = [
        (40, "8f48a0defcb327e9"), (20, "7ba4ce6c196e991e"), (20, "5b5cc5a3f2ece6cd"), (8, "a2d892f80f998382")
    ];

    /// <summary>T-22a moved these (its PR lists each cell): 1059 to 44, 1729 to 1759, and 115 to 129.</summary>
    private static readonly (int Count, string Checksum)[] MasterAbi = [
        (44, "fb7ed96371e3fd22"), (1759, "c6974bac9345ce1e"), (129, "dc63aa48893c21ef")
    ];

    private static bool IsNative(string? operand) => operand is "NInt" or "NUInt";

    [Test]
    public async Task Ledger_TheMasterGridCells_AreOwnedAsBeforeT18b() {
        var (cells, _) = OperatorOracle.Run.Value;
        var problems = new List<string>();
        foreach (var (rules, master) in new[] { (LoweredRules, MasterLowered), (AbiRules, MasterAbi) }) {
            for (var i = 0; i < rules.Length; i++) {
                var keys = cells.Where(static c => !IsNative(c.Left) && !IsNative(c.Right))
                    .SelectMany(c => c.Divergences.Where(d => rules[i].Matches(c, d)).Select(_ => rules[i].KeyOf(c)))
                    .ToArray();
                if ((keys.Length, LedgerCheck.Checksum(keys)) != master[i])
                    problems.Add($"{rules[i].Describe()}: before T-18b {master[i]}, now ({keys.Length}, {LedgerCheck.Checksum(keys)})");
            }
        }
        await Assert.That(problems).IsEmpty();
        await Assert.That(cells.Count(static c => !IsNative(c.Left) && !IsNative(c.Right))).IsEqualTo(8800);
    }

    [Test]
    public async Task Ledger_CoversTheWholeGrid_WithinBudget_AndChecksEveryDefinedValue() {
        var (cells, elapsed) = OperatorOracle.Run.Value;
        var binary = Enum.GetValues<OperatorKind>().Count(static k => OperatorShape.ArityOf(k) == 2);
        var unary = Enum.GetValues<OperatorKind>().Length - binary;
        await Assert.That(cells.Count).IsEqualTo((binary * OperatorOracle.GridOperands.Length * OperatorOracle.GridOperands.Length) + (unary * OperatorOracle.GridOperands.Length));
        await Assert.That(cells.Count).IsEqualTo(10464);
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromMinutes(2));
        // A defined cell the VM ran must have had its value compared with what the tree states.
        var unchecked_ = cells.Where(static c => c.Abi.Contains("(value unchecked)", StringComparison.Ordinal)).Select(static c => c.Key).ToArray();
        await Assert.That(unchecked_).IsEmpty();
    }

    private static OracleCell Cell(string left, OperatorKind op, string? right) =>
        OperatorOracle.Run.Value.Cells.Single(c => c.Left == left && c.Op == op && c.Right == right);

    [Test]
    public async Task Probe_Int32PlusInt32_TreeAndAbiBothSay32BitWrap() {
        // The T-03b seed said the VM returns long. With typed Int32 operands it does not: it wraps at 32 bits,
        // as the tree (C#) states. Only constants widen (ArithmeticNodeTests.Add_TwoIntegers).
        var cell = Cell("Int32", OperatorKind.Add, "Int32");
        await Assert.That(cell.Tree).StartsWith("Int32");
        await Assert.That(cell.Abi).StartsWith("Int32;");
        await Assert.That(cell.Abi).Contains("S1=-2147483642");
        await Assert.That(cell.Divergences).IsEmpty();
    }

    [Test]
    public async Task Probe_NumberOpNumber_IsIntegerTruncation_AndFloatPlusNumberLosesPrecisionAbove2Pow53() {
        await Assert.That(Cell("Number", OperatorKind.Divide, "Number").Abi).Contains("S1=1286742750677284");
        await Assert.That(Cell("Number", OperatorKind.Add, "Float").Abi).Contains("S1=9007199254740994");
        await Assert.That(Cell("Number", OperatorKind.Add, "Float").Tree).Contains("conv(Implicit/intrinsic,id)");
    }

    [Test]
    public async Task Probe_DecimalPlusDecimal_IsExact_AndDecimalPlusNumberConvertsWithoutATreeNode() {
        await Assert.That(Cell("Decimal", OperatorKind.Add, "Decimal").Abi).Contains("S1=0.3").And.Contains("Decimal;");
        var mixed = Cell("Decimal", OperatorKind.Add, "Number");
        await Assert.That(mixed.Tree).Contains("conv(id,Implicit/declared)");
        await Assert.That(mixed.Divergences.Any(static d => d.Kind == OracleKind.AbiDivergesFromTree && d.Reason.StartsWith("unstated conversion", StringComparison.Ordinal))).IsTrue();
    }

    /// <summary>Issue 815: the platform defines no Float64 * Decimal, and the lowered analysis now says so (T-18); the ABI never runs it.</summary>
    [Test]
    public async Task Probe_Float64TimesDecimal_IsNoneInTheTree_RejectedByTheLoweredAnalysis() {
        var cell = Cell("Float", OperatorKind.Multiply, "Decimal");
        await Assert.That(cell.Tree).IsEqualTo("none");
        await Assert.That(cell.Lowered).StartsWith("error: arithmetic operand is not numeric (got 'Double' and 'Decimal')");
        await Assert.That(cell.Abi).IsEqualTo("not run (analysis error)");
    }

    /// <summary>
    /// WRONG VALUES, pinned as they are today (issue 832, owner T-21). The VM returns -1 for unary minus on a Decimal
    /// and on a Duration whatever the operand, and <c>UInt8 | UInt64</c> reads its UInt64 word as a signed one (T-22a
    /// typed it UInt64, the provider's type; it was Byte). These
    /// are not the right answers; T-21's fix inverts them. The ledger's checksums are value-independent, so without
    /// these a wrong value that moved to a different wrong value would be invisible. The operands are exactly
    /// representable and the text is the oracle's invariant-culture rendering, so the pin is the same on arm64 and x64.
    /// </summary>
    [Test]
    public async Task Probe_WrongVmValues_Issue832_PinnedAsTheyAreToday_T21InvertsThem() {
        await Assert.That(Cell("Decimal", OperatorKind.UnaryMinus, null).Abi).IsEqualTo("Decimal; S1=-1; S2=-1");
        await Assert.That(Cell("Duration", OperatorKind.UnaryMinus, null).Abi).IsEqualTo("TimeSpan; S1=-1; S2=-1");
        await Assert.That(Cell("UInt8", OperatorKind.BitwiseOr, "UInt64").Abi).IsEqualTo("UInt64; S1=255; S2=-1");
    }

    /// <summary>
    /// A tool, not a pin: with <c>POLY_ORACLE_DUMP</c> set to a file path, prints every cell's four columns and its
    /// divergences, one line per cell, so a slice can diff the grid on its base against its head. Nothing is recorded.
    /// </summary>
    [Test]
    public async Task Dump_WhenRequested_PrintsEveryCell() {
        if (Environment.GetEnvironmentVariable("POLY_ORACLE_DUMP") is not { } path)
            return;
        var lines = OperatorOracle.Run.Value.Cells
            .Select(static c => $"{c.Key}\t{c.Tree}\t{c.Lowered}\t{c.Abi}\t{string.Join(" | ", c.Divergences.Select(static d => $"{d.Kind}: {d.Reason}"))}")
            .ToArray();
        File.WriteAllLines(path, lines);
        await Assert.That(lines.Length).IsEqualTo(OperatorOracle.Run.Value.Cells.Count);
    }

    [Test]
    public async Task AnalyzerColumn_NeverSwallowsAnException() {
        // An exception in the analyzer column is a finding, not a blank cell.
        var errors = OperatorOracle.Run.Value.Cells.Where(static c => c.Analyzer.StartsWith("error:", StringComparison.Ordinal))
            .Select(static c => $"{c.Key}: {c.Analyzer}").ToArray();
        await Assert.That(errors).IsEmpty();
    }

    // ---- the ratchet itself, on synthetic input ----

    private static OracleCell SyntheticCell(string left, OperatorKind op, string right) =>
        new(left, op, right, "none", "n/a", "n/a", "n/a", []);

    private static (OracleCell, OracleDivergence) Item(string left, OperatorKind op, string right, string reason) =>
        (SyntheticCell(left, op, right), new OracleDivergence(OracleKind.AbiDivergesFromTree, reason));

    private static LedgerRule Rule(string lefts, string reason, int count) =>
        new(OracleKind.AbiDivergesFromTree, lefts, "*", "*", reason, "T-99", count);

    private static LedgerRule Pinned(LedgerRule rule, IEnumerable<(OracleCell Cell, OracleDivergence Divergence)> items) =>
        rule with { Checksum = LedgerCheck.Checksum(items.Where(i => rule.Matches(i.Cell, i.Divergence)).Select(i => rule.KeyOf(i.Cell))) };

    [Test]
    public async Task VmReasonRules_B3B4B5_ClaimEachReasonShapeExactlyOnce() {
        // The three VM rules must partition the reasons RunVm writes: a type only, a type and a value, a value only, two values.
        LedgerRule[] rules = [
            new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "^type: VM [^;]*$", "B3", 1),
            new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "^type: VM .*; value S", "B4", 1),
            new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "^value S", "B5", 1)
        ];
        var cell = SyntheticCell("A", OperatorKind.Add, "B");
        var shapes = new Dictionary<string, string> {
            ["type only"] = "type: VM Int32, tree Int64",
            ["type and value"] = "type: VM Int32, tree Int64; value S1: VM -1, tree 5",
            ["value only"] = "value S1: VM -1, tree 5",
            ["two values"] = "value S1: VM -1, tree 5; value S2: VM -1, tree 6"
        };
        var expectedOwner = new Dictionary<string, string> { ["type only"] = "B3", ["type and value"] = "B4", ["value only"] = "B5", ["two values"] = "B5" };
        foreach (var (shape, reason) in shapes) {
            var owners = rules.Where(rule => rule.Matches(cell, new OracleDivergence(OracleKind.AbiDivergesFromTree, reason))).Select(static rule => rule.Owner).ToArray();
            await Assert.That(owners).IsEquivalentTo([expectedOwner[shape]]);
        }
    }

    [Test]
    public async Task Ratchet_ExactCountsAndCells_HaveNoProblem() {
        var items = new[] { Item("A", OperatorKind.Add, "B", "x"), Item("A", OperatorKind.Add, "C", "x"), Item("D", OperatorKind.Add, "C", "y") };
        await Assert.That(LedgerCheck.Compare(items, [Pinned(Rule("A", "x", 2), items), Pinned(Rule("D", "y", 1), items)])).IsEmpty();
    }

    [Test]
    public async Task Ratchet_SameCountOneCellSwapped_Fails() {
        // A fix and a regression inside one rule leave the count equal; the checksum of the cells does not.
        var before = new[] { Item("A", OperatorKind.Add, "B", "x"), Item("A", OperatorKind.Add, "C", "x") };
        var after = new[] { Item("A", OperatorKind.Add, "B", "x"), Item("A", OperatorKind.Add, "D", "x") };
        var problems = LedgerCheck.Compare(after, [Pinned(Rule("A", "x", 2), before)]);
        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).Contains("same count 2 but the cells changed").And.Contains("lowers ExpectedCount and updates the checksum");
        // Order of the cells does not matter.
        await Assert.That(LedgerCheck.Compare([after[1], after[0]], [Pinned(Rule("A", "x", 2), after)])).IsEmpty();
        await Assert.That(LedgerCheck.Checksum(["b", "a"])).IsEqualTo(LedgerCheck.Checksum(["a", "b"]));
    }

    [Test]
    public async Task Ratchet_OneMoreMatchingCell_FailsNamingTheRuleAndBothCounts() {
        var items = new[] { Item("A", OperatorKind.Add, "B", "x"), Item("A", OperatorKind.Add, "C", "x"), Item("A", OperatorKind.Add, "D", "x") };
        var problems = LedgerCheck.Compare(items, [Rule("A", "x", 2)]);
        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).Contains("-> T-99").And.Contains("expected 2").And.Contains("matched 3");
    }

    [Test]
    public async Task Ratchet_OneFewerMatchingCell_Fails() {
        var items = new[] { Item("A", OperatorKind.Add, "B", "x") };
        var problems = LedgerCheck.Compare(items, [Rule("A", "x", 2)]);
        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).Contains("expected 2").And.Contains("matched 1");
    }

    [Test]
    public async Task Ratchet_CellMatchingTwoRules_Fails() {
        var items = new[] { Item("A", OperatorKind.Add, "B", "x") };
        var problems = LedgerCheck.Compare(items, [Rule("A", "x", 1), Rule("*", "x", 1)]);
        await Assert.That(problems.Any(static p => p.StartsWith("claimed by 2 rules", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Ratchet_UnownedDivergence_AndStaleRule_Fail() {
        var items = new[] { Item("A", OperatorKind.Add, "B", "x") };
        var problems = LedgerCheck.Compare(items, [Rule("Z", "x", 1)]);
        await Assert.That(problems.Any(static p => p.StartsWith("unowned:", StringComparison.Ordinal))).IsTrue();
        await Assert.That(problems.Any(static p => p.Contains("expected 1, matched 0", StringComparison.Ordinal))).IsTrue();
        await Assert.That(LedgerCheck.Compare([], [Rule("Z", "x", 0)]).Count).IsEqualTo(1);
    }
}