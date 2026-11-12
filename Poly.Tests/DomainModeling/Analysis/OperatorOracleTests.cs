using System.Diagnostics;
using System.Globalization;

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
    AbiRejectsDefined
}

/// <summary>One operand definition and the values the VM column feeds it. <c>Stress</c> exposes 32-bit wrap, 2^53+1, decimal exactness.</summary>
internal sealed record OracleOperand(string Name, string? DslType, Type? Clr, object? Stress, object? Plain);

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

    private static readonly Poly.DomainModeling.Meaning.DomainTypeModel Model = OperatorOperands.Model();

    public static readonly Lazy<(IReadOnlyList<OracleCell> Cells, TimeSpan Elapsed)> Run = new(() => {
        var watch = Stopwatch.StartNew();
        var cells = new List<OracleCell>();
        foreach (var kind in Enum.GetValues<OperatorKind>()) {
            foreach (var left in Operands) {
                if (OperatorShape.ArityOf(kind) == 1) {
                    cells.Add(Evaluate(left, kind, null));
                    continue;
                }
                foreach (var right in Operands)
                    cells.Add(Evaluate(left, kind, right));
            }
        }
        watch.Stop();
        return (cells, watch.Elapsed);
    });

    private static ITypeDefinition Def(OracleOperand operand) => Model.Resolve(operand.Name).Definition!;

    private static OracleCell Evaluate(OracleOperand left, OperatorKind kind, OracleOperand? right) {
        var divergences = new List<OracleDivergence>();
        var resolution = Def(left).GetOperator(kind, right is null ? null : Def(right));
        var tree = DescribeTree(left, right, resolution);

        var analyzer = AnalyzerVerdict(left, kind, right);
        if (analyzer is "accepts" or "rejects" && (analyzer == "accepts") != (resolution is not null))
            divergences.Add(new(OracleKind.AnalyzerDisagrees, $"analyzer {analyzer}, tree {(resolution is { } r ? r.Result.Name : "none")}"));

        var lowered = "n/a";
        var abi = "n/a";
        if (left.Clr is not null && (right is null || right.Clr is not null))
            (lowered, abi) = RunVm(left, kind, right, resolution, divergences);

        return new OracleCell(left.Name, kind, right?.Name, tree, analyzer ?? "n/a", lowered, abi, divergences);
    }

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

    private static string DslOp(OperatorKind kind) => kind switch {
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>"accepts" or "rejects" from the domain analyzer, or null when the domain cannot express an operand (no Int32 or Float type).</summary>
    private static string? AnalyzerVerdict(OracleOperand left, OperatorKind kind, OracleOperand? right) {
        if (left.DslType is null || (right is not null && right.DslType is null))
            return null;
        var expression = right is null ? "not L" : $"L {DslOp(kind)} R";
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
        foreach (var (name, leftValue, rightValue) in ArgumentSets(left, right)) {
            try {
                var args = right is null ? new[] { leftValue } : [leftValue, rightValue];
                using var exec = Interpreter.Execute(program.Program, args);
                var actual = vmType == typeof(bool) ? (object?)exec.GetValue<bool>() : exec.Result.Value;
                runs.Add($"{name}={Format(actual)}");
                if (resolution is { } r) {
                    if (Expected(kind, r, leftValue, rightValue) is not { } expected)
                        unchecked_ = true;
                    else if (Format(expected) != Format(actual))
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

    /// <summary>The value the tree's statement denotes: each operand converted as the interface says, then the operator applied by C#.</summary>
    private static object? Expected(OperatorKind kind, OperatorResolution resolution, object? leftValue, object? rightValue) {
        try {
            var parameters = resolution.Signature.Parameters.Select(static p => p.ParameterTypeDefinition).ToArray();
            static object? Convert(object? value, ITypeDefinition target) =>
                value is null || !target.TryGetRuntimeType(out var type) || value.GetType() == type
                    ? value
                    : System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            dynamic x = Convert(leftValue, parameters[0])!;
            dynamic? y = parameters.Length > 1 ? Convert(rightValue, parameters[1]) : null;
            return kind switch {
                OperatorKind.Add => x + y,
                OperatorKind.Subtract => x - y,
                OperatorKind.Multiply => x * y,
                OperatorKind.Divide => x / y,
                OperatorKind.Equal => x == y,
                OperatorKind.NotEqual => x != y,
                OperatorKind.LessThan => x < y,
                OperatorKind.LessThanOrEqual => x <= y,
                OperatorKind.GreaterThan => x > y,
                OperatorKind.GreaterThanOrEqual => x >= y,
                OperatorKind.And => (bool)x && (bool)y!,
                OperatorKind.Or => (bool)x || (bool)y!,
                OperatorKind.Not => !(bool)x,
                _ => null
            };
        }
        catch (Exception) {
            return null;
        }
    }
}

/// <summary>
/// An owner row for a divergence, matched by kind, operand names, operator and a fragment of the reason.
/// <c>*</c> matches anything. Every divergence needs a rule and every rule needs a divergence: a stale rule fails.
/// </summary>
internal sealed record LedgerRule(OracleKind Kind, string Lefts, string Ops, string Rights, string ReasonContains, string Owner) {
    public bool Matches(OracleCell cell, OracleDivergence divergence) =>
        divergence.Kind == Kind
        && In(Lefts, cell.Left) && In(Ops, cell.Op.ToString()) && In(Rights, cell.Right ?? "-")
        && divergence.Reason.Contains(ReasonContains, StringComparison.Ordinal);

    private static bool In(string set, string value) => set == "*" || set.Split(',').Contains(value);
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
    private const string Arithmetic = "Add,Subtract,Multiply,Divide";

    private static readonly LedgerRule[] AnalyzerRules = [
        // The domain analyzer types an entity operand as Unknown and so accepts everything beside it.
        new(OracleKind.AnalyzerDisagrees, "Order", "*", "*", "analyzer accepts", "T-06 (Unknown operands: R1)"),
        new(OracleKind.AnalyzerDisagrees, "*", "*", "Order", "analyzer accepts", "T-06 (Unknown operands: R1)"),
        // Enum member literals compared as Text, and enum ordering.
        new(OracleKind.AnalyzerDisagrees, "Status,Text", Comparisons, "Status,Text", "analyzer accepts", "T-06 (enum member literals)"),
        new(OracleKind.AnalyzerDisagrees, "Status", Ordering, "Status", "analyzer accepts", "T-07 (ordering legality)"),
        // Same-category pairs the analyzer accepts and no platform defines.
        new(OracleKind.AnalyzerDisagrees, "Text,Boolean", Ordering, "Text,Boolean", "analyzer accepts", "T-07 (#631 ordering legality)"),
        // Temporal cells: the analyzer's offset permission and its rejection of CLR-defined duration arithmetic.
        new(OracleKind.AnalyzerDisagrees, "Date,DateTime", "Add,Subtract", "Date,DateTime,Decimal,Number,Duration", "analyzer accepts", "T-10 (temporal offset permission)"),
        new(OracleKind.AnalyzerDisagrees, "Duration,Time,Number", "*", "Duration,Time,Number", "analyzer rejects", "T-10 (temporal operands the CLR defines)")
    ];

    private static readonly LedgerRule[] LoweredRules = [
        new(OracleKind.LoweredAnalysisDisagrees, "*", "*", "*", "analysis error on a defined cell", "T-18 (SyntaxTypeCompatibilityAnalyzer arithmetic rules; temporal operands with T-10)"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "*", "*", "typed Decimal", "T-17 (PromoteNumeric / ResolveAddType)"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "Add", "*", "typed String", "T-17 (ResolveAddType: any string operand makes Add a string)"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "Add,Subtract", "*", "typed DateOnly", "T-10 (RewriteDateOffset)"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "Add,Subtract", "*", "typed DateTime", "T-10 (RewriteDateOffset)"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", Comparisons, "*", "typed Boolean", "T-18 (Compatible accepts same-category and Float-with-Decimal pairs)")
    ];

    private static readonly LedgerRule[] AbiRules = [
        new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "executes an undefined cell", "T-21 (the ABI must reject what the tree does not define)"),
        new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "unstated conversion", "T-21 (T-04 and Lower state the conversion node; the emitter stops converting)")
    ];

    private static readonly LedgerRule[] RejectedDefinedRules = [];

    private static async Task AssertOwned(OracleKind kind, LedgerRule[] rules) {
        var (cells, _) = OperatorOracle.Run.Value;
        var unowned = new List<string>();
        var used = new HashSet<LedgerRule>();
        foreach (var cell in cells) {
            foreach (var divergence in cell.Divergences.Where(d => d.Kind == kind)) {
                var matching = rules.Where(rule => rule.Matches(cell, divergence)).ToArray();
                if (matching.Length == 0)
                    unowned.Add($"{cell.Key}: {divergence.Reason}");
                foreach (var rule in matching)
                    used.Add(rule);
            }
        }
        var stale = rules.Where(rule => !used.Contains(rule)).Select(static rule => $"{rule.Lefts} {rule.Ops} {rule.Rights} [{rule.ReasonContains}] -> {rule.Owner}").ToArray();
        await Assert.That(unowned).IsEmpty();
        await Assert.That(stale).IsEmpty();
    }

    [Test]
    public async Task AnalyzerDisagrees_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.AnalyzerDisagrees, AnalyzerRules);

    [Test]
    public async Task LoweredAnalysisDisagrees_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.LoweredAnalysisDisagrees, LoweredRules);

    [Test]
    public async Task AbiDivergesFromTree_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.AbiDivergesFromTree, AbiRules);

    [Test]
    public async Task AbiRejectsDefined_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.AbiRejectsDefined, RejectedDefinedRules);

    [Test]
    public async Task Ledger_CoversTheWholeGrid_WithinBudget_AndChecksEveryDefinedValue() {
        var (cells, elapsed) = OperatorOracle.Run.Value;
        var binary = Enum.GetValues<OperatorKind>().Count(static k => OperatorShape.ArityOf(k) == 2);
        await Assert.That(cells.Count).IsEqualTo((binary * OperatorOracle.Operands.Length * OperatorOracle.Operands.Length) + OperatorOracle.Operands.Length);
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

    [Test]
    public async Task Probe_Float64TimesDecimal_IsNoneInTheTree_TypedDecimalByTheLoweredAnalysis_AndRunByTheAbi() {
        var cell = Cell("Float", OperatorKind.Multiply, "Decimal");
        await Assert.That(cell.Tree).IsEqualTo("none");
        await Assert.That(cell.Lowered).IsEqualTo("Decimal");
        await Assert.That(cell.Abi).StartsWith("Decimal;");
    }
}