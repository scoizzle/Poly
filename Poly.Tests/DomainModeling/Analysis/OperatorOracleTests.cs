using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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

    private static readonly Poly.DomainModeling.Meaning.DomainTypeModel Model = OperatorOperands.Model();

    /// <summary>The grid, under the invariant culture: the checksums hash reason strings, and concatenation formats values.</summary>
    public static readonly Lazy<(IReadOnlyList<OracleCell> Cells, TimeSpan Elapsed)> Run = new(() => {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
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
/// An owner row for a divergence, matched by kind, operand names, operator and a fragment of the reason.
/// <c>*</c> matches anything. <see cref="ExpectedCount"/> is the number of divergences the row owns: a fixing slice
/// edits the number or deletes the row, and growth fails as surely as shrinkage.
/// </summary>
internal sealed record LedgerRule(OracleKind Kind, string Lefts, string Ops, string Rights, string ReasonContains, string Owner, int ExpectedCount, string Checksum = "") {
    public bool Matches(OracleCell cell, OracleDivergence divergence) =>
        divergence.Kind == Kind
        && In(Lefts, cell.Left) && In(Ops, cell.Op.ToString()) && In(Rights, cell.Right ?? "-")
        && divergence.Reason.Contains(ReasonContains, StringComparison.Ordinal);

    public string Describe() => $"{Kind} [{Lefts}] [{Ops}] [{Rights}] \"{ReasonContains}\" -> {Owner}";

    private static bool In(string set, string value) => set == "*" || set.Split(',').Contains(value);
}

internal static class LedgerCheck {
    /// <summary>Order-independent, machine-independent fingerprint of the cells a rule owns: SHA-256 over the sorted keys, 16 hex characters.</summary>
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
                keys[owner].Add($"{cell.Key}|{divergence.Reason}");
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
    private const string Arithmetic = "Add,Subtract,Multiply,Divide";

    private const string AllButOrder = "Number,Int32,Decimal,Float,Text,Boolean,Date,Time,DateTime,Duration,Uuid,Status,Money";

    private static readonly LedgerRule[] AnalyzerRules = [
        // The domain analyzer types an entity operand as Unknown and so accepts everything beside it.
        new(OracleKind.AnalyzerDisagrees, "Order", "*", "*", "analyzer accepts", "T-06 (Unknown operands: R1)", 107, "b0d496ca8a8249e4"),
        new(OracleKind.AnalyzerDisagrees, AllButOrder, "*", "Order", "analyzer accepts", "T-06 (Unknown operands: R1)", 96, "062dbd948638e7d4"),
        // Enum member literals compared as Text, and enum ordering.
        new(OracleKind.AnalyzerDisagrees, "Status", Comparisons, "Text", "analyzer accepts", "T-06 (enum member literals)", 6, "07623f4b9c636bc9"),
        new(OracleKind.AnalyzerDisagrees, "Text", Comparisons, "Status", "analyzer accepts", "T-06 (enum member literals)", 6, "7ed848eea23806f2"),
        new(OracleKind.AnalyzerDisagrees, "Status", Ordering, "Status", "analyzer accepts", "T-07 (ordering legality)", 4, "b83ca30815274930"),
        // Same-category pairs the analyzer accepts and no platform defines.
        new(OracleKind.AnalyzerDisagrees, "Text,Boolean", Ordering, "Text,Boolean", "analyzer accepts", "T-07 (#631 ordering legality)", 8, "8ea030c0d8f2ea50"),
        // Temporal cells: the analyzer's offset permission and its rejection of CLR-defined duration arithmetic.
        new(OracleKind.AnalyzerDisagrees, "Date,DateTime", "Add,Subtract", "Date,DateTime,Decimal,Number,Duration", "analyzer accepts", "T-10 (temporal offset permission)", 11, "6a8a5c891edeb92e"),
        new(OracleKind.AnalyzerDisagrees, "Duration,Time,Number", "*", "Duration,Time,Number", "analyzer rejects", "T-10 (temporal operands the CLR defines)", 7, "7e7c7cf1f38d3237")
    ];

    // Float64 x Decimal and every Decimal-with-X cell the lowered analysis types: the domain DSL has no Float, so
    // the analyzer column is n/a for those; the owners are the lowered analysis (T-17, T-18) and the ABI (T-21).
    private static readonly LedgerRule[] LoweredRules = [
        new(OracleKind.LoweredAnalysisDisagrees, "*", "*", "*", "analysis error on a defined cell", "T-18 (SyntaxTypeCompatibilityAnalyzer arithmetic rules; temporal operands with T-10)", 16, "a5e2a93e2074fb1c"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "*", "*", "typed Decimal", "T-17 (PromoteNumeric / ResolveAddType)", 62, "6365dee6a207e45a"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "Add", "*", "typed String", "T-17 (ResolveAddType: any string operand makes Add a string)", 20, "20c644f58d9bdcc1"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "Add,Subtract", "*", "typed DateOnly", "T-10 (RewriteDateOffset)", 6, "67ddd6e443b61139"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", "Add,Subtract", "*", "typed DateTime", "T-10 (RewriteDateOffset)", 6, "de2a2fb0b866b225"),
        new(OracleKind.LoweredAnalysisDisagrees, "*", Comparisons, "*", "typed Boolean", "T-18 (Compatible accepts same-category and Float-with-Decimal pairs)", 20, "d4268c300cd7d982")
    ];

    private static readonly LedgerRule[] AbiRules = [
        new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "executes an undefined cell", "T-21 (the ABI must reject what the tree does not define)", 110, "3b2144722f34faf2"),
        new(OracleKind.AbiDivergesFromTree, "*", "*", "*", "unstated conversion", "T-21 (T-04 and Lower state the conversion node; the emitter stops converting)", 100, "bab370fb22c12a8c")
    ];

    private static readonly LedgerRule[] RejectedDefinedRules = [];

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
        await AssertOwned(OracleKind.AnalyzerDisagrees, AnalyzerRules, 245);

    [Test]
    public async Task LoweredAnalysisDisagrees_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.LoweredAnalysisDisagrees, LoweredRules, 130);

    [Test]
    public async Task AbiDivergesFromTree_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.AbiDivergesFromTree, AbiRules, 210);

    [Test]
    public async Task AbiRejectsDefined_EveryDivergenceIsOwned_AndNoEntryIsStale() =>
        await AssertOwned(OracleKind.AbiRejectsDefined, RejectedDefinedRules, 0);

    [Test]
    public async Task Ledger_CoversTheWholeGrid_WithinBudget_AndChecksEveryDefinedValue() {
        var (cells, elapsed) = OperatorOracle.Run.Value;
        var binary = Enum.GetValues<OperatorKind>().Count(static k => OperatorShape.ArityOf(k) == 2);
        var unary = Enum.GetValues<OperatorKind>().Length - binary;
        await Assert.That(cells.Count).IsEqualTo((binary * OperatorOracle.Operands.Length * OperatorOracle.Operands.Length) + (unary * OperatorOracle.Operands.Length));
        await Assert.That(cells.Count).IsEqualTo(8800);
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
        rule with { Checksum = LedgerCheck.Checksum(items.Where(i => rule.Matches(i.Cell, i.Divergence)).Select(static i => $"{i.Cell.Key}|{i.Divergence.Reason}")) };

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