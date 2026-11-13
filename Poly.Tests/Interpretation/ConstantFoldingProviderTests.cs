using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.Interpretation;

/// <summary>One constant cell of the fold grid: an operator over two literal operands, with one of the ledger's value sets.</summary>
internal sealed record FoldGridRow(OracleOperand Left, OperatorKind Op, OracleOperand? Right, string Set, object? LeftValue, object? RightValue) {
    public string Cell => $"{Left.Name} {Op} {Right?.Name ?? "-"}";
    public string Key => $"{Cell} {Set}";
}

/// <summary>
/// The fold grid (T-20): every operator kind over two constant operands of the operator ledger's CLR-backed operands,
/// with the ledger's two value sets (S1: left stress, right plain; S2: left plain, right stress). Each row records what
/// the standard pipeline folds it to (value and type), what the unfolded VM computes (Parameter operands, run as the
/// ledger runs them), what the runtime binder computes for the operator the provider states, and what the printed C#
/// computes when Roslyn compiles the authored tree.
/// </summary>
internal static class ConstantFoldGrid {
    /// <summary>The one read of the operator ledger's operand set (T-05a may reshape it): its CLR-backed operands.</summary>
    public static IReadOnlyList<OracleOperand> Operands { get; } = [.. OperatorOracle.Operands.Where(static o => o.Clr is not null)];

    private static readonly Regex ValueWrong = new("^value S|^type: VM .*; value S", RegexOptions.CultureInvariant);

    /// <summary>
    /// The one read of the operator ledger's VM column: the cells whose VM value is wrong (issue 832, owner T-21), that is
    /// a divergence reason that starts with <c>value</c> or with <c>type: VM …; value</c>.
    /// </summary>
    public static IReadOnlySet<string> LedgerValueWrongCells { get; } = OperatorOracle.Run.Value.Cells
        .Where(static c => c.Divergences.Any(static d => d.Kind == OracleKind.AbiDivergesFromTree && ValueWrong.IsMatch(d.Reason)))
        .Select(static c => $"{c.Left} {c.Op} {c.Right ?? "-"}")
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<FoldGridRow> Rows { get; } = BuildRows();

    private static FoldGridRow[] BuildRows() {
        var rows = new List<FoldGridRow>();
        foreach (var kind in Enum.GetValues<OperatorKind>()) {
            if (kind is OperatorKind.UnaryPlus)
                continue;
            foreach (var left in Operands) {
                if (OperatorShape.ArityOf(kind) == 1) {
                    rows.Add(new(left, kind, null, "S1", left.Stress, null));
                    rows.Add(new(left, kind, null, "S2", left.Plain, null));
                    continue;
                }
                foreach (var right in Operands) {
                    rows.Add(new(left, kind, right, "S1", left.Stress, right.Plain));
                    rows.Add(new(left, kind, right, "S2", left.Plain, right.Stress));
                }
            }
        }
        return [.. rows];
    }

    /// <summary>And, Or and Not fold through <c>FoldAnd</c>, <c>FoldOr</c> and the Boolean arm, which T-20 does not touch.</summary>
    public static bool IsLogical(OperatorKind kind) => kind is OperatorKind.And or OperatorKind.Or or OperatorKind.Not;

    /// <summary>
    /// The folds T-20 adds, by name only (the lock): Decimal with Int32 or Int64 arithmetic, Int32 bitwise and shift,
    /// Boolean <c>&amp;</c>, <c>|</c> and <c>^</c>, and <c>%</c> over the mixed pairs whose other arithmetic folds today.
    /// Boolean <c>==</c> and <c>!=</c> are named too, and already fold.
    /// </summary>
    public static bool IsNamedNewFold(FoldGridRow row) {
        var (l, r) = (row.Left.Name, row.Right?.Name);
        return row.Op switch {
            OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide =>
                (l, r) is ("Decimal", "Int32" or "Number") or ("Int32" or "Number", "Decimal"),
            OperatorKind.Modulo =>
                (l, r) is ("Decimal", "Int32" or "Number") or ("Int32" or "Number", "Decimal")
                    or ("Int32", "Number" or "Float") or ("Number" or "Float", "Int32"),
            OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor =>
                (l, r) is ("Int32", "Int32") or ("Boolean", "Boolean"),
            OperatorKind.ShiftLeft or OperatorKind.ShiftRight => (l, r) is ("Int32", "Int32"),
            OperatorKind.BitwiseNot => l == "Int32",
            _ => false
        };
    }

    public static Node Build(OperatorKind kind, Node left, Node? right) => kind switch {
        OperatorKind.Add => new Add(left, right!),
        OperatorKind.Subtract => new Subtract(left, right!),
        OperatorKind.Multiply => new Multiply(left, right!),
        OperatorKind.Divide => new Divide(left, right!),
        OperatorKind.Modulo => new Modulo(left, right!),
        OperatorKind.Equal => new Equal(left, right!),
        OperatorKind.NotEqual => new NotEqual(left, right!),
        OperatorKind.LessThan => new LessThan(left, right!),
        OperatorKind.LessThanOrEqual => new LessThanOrEqual(left, right!),
        OperatorKind.GreaterThan => new GreaterThan(left, right!),
        OperatorKind.GreaterThanOrEqual => new GreaterThanOrEqual(left, right!),
        OperatorKind.And => new And(left, right!),
        OperatorKind.Or => new Or(left, right!),
        OperatorKind.Not => new Not(left),
        OperatorKind.BitwiseAnd => new BitwiseAnd(left, right!),
        OperatorKind.BitwiseOr => new BitwiseOr(left, right!),
        OperatorKind.BitwiseXor => new BitwiseXor(left, right!),
        OperatorKind.ShiftLeft => new ShiftLeft(left, right!),
        OperatorKind.ShiftRight => new ShiftRight(left, right!),
        OperatorKind.UnaryMinus => new UnaryMinus(left),
        OperatorKind.BitwiseNot => new BitwiseNot(left),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static Node ConstantTree(FoldGridRow row) =>
        Build(row.Op, new Constant(row.LeftValue), row.Right is null ? null : new Constant(row.RightValue));

    public static string Format(object? value) => value switch {
        null => "null",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    /// <summary>The value and its CLR type: <c>Int32:3</c>. Decimal keeps its scale (<c>Decimal:1.500</c>).</summary>
    public static string Typed(object? value) => value is null ? "null" : $"{value.GetType().Name}:{Format(value)}";

    private static string Value(string typed) => typed.IndexOf(':') is var colon and >= 0 ? typed[(colon + 1)..] : typed;

    public static bool SameValue(string a, string b) => Value(a) == Value(b);

    private static readonly Lazy<IReadOnlyDictionary<FoldGridRow, string?>> FoldsByRow = new(() => {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            return ConstantFoldGrid.Rows.ToDictionary(static row => row, Fold);
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    });

    /// <summary>The rows the pass folds, each with what it folded to.</summary>
    public static IReadOnlyList<(FoldGridRow Row, string Fold)> Folded() =>
        [.. Rows.Where(static row => FoldsByRow.Value[row] is not null).Select(static row => (row, FoldsByRow.Value[row]!))];

    public static string? FoldOf(FoldGridRow row) => FoldsByRow.Value[row];

    /// <summary>What the pass folded the row's constant tree to: the value and its type, and the cast it wrapped the constant in; null when it did not fold.</summary>
    private static string? Fold(FoldGridRow row) {
        var node = ConstantTree(row);
        AnalysisResult analysis;
        try {
            analysis = Interpreter.Analyze(node);
        }
        catch (Exception ex) {
            return $"analysis throws {ex.GetType().Name}";
        }
        if (analysis.GetMetadata<ConstantValueMetadata>(node) is not { } folded)
            return null;
        var cast = analysis.GetNodeReplacement(node) is TypeCast && analysis.GetResolvedType(node) is { } resolved ? $" cast {resolved.Name}" : "";
        return Typed(folded.Value) + cast;
    }

    /// <summary>The unfolded VM: the same operator over Parameter operands, run with the row's values, as the ledger runs it.</summary>
    public static string UnfoldedVm(FoldGridRow row) {
        var a = new Parameter("a", new ClrTypeReference(row.Left.Clr!));
        var b = row.Right is null ? null : new Parameter("b", new ClrTypeReference(row.Right.Clr!));
        var node = Build(row.Op, a, b);
        try {
            var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
            var program = Interpreter.Compile(node, analysis);
            var args = row.Right is null ? new[] { row.LeftValue } : [row.LeftValue, row.RightValue];
            using var exec = Interpreter.Execute(program, args);
            return program.RootClrType == typeof(bool) ? Typed(exec.GetValue<bool>()) : Typed(exec.Result.Value);
        }
        catch (Exception ex) {
            return $"throws {ex.GetType().Name}";
        }
    }

    public static OperatorResolution? Resolution(FoldGridRow row) =>
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(row.Left.Clr!).GetOperator(row.Op,
            row.Right is null ? null : ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(row.Right.Clr!));

    /// <summary>
    /// The C# value through the runtime binder: each operand converted to the parameter type of the operator the provider
    /// states, then the operator applied (T-24's oracle). Null when the provider states no operator.
    /// </summary>
    public static string? Binder(FoldGridRow row) {
        if (Resolution(row) is not { } resolution)
            return null;
        try {
            var parameters = resolution.Signature.Parameters.Select(static p => p.ParameterTypeDefinition).ToArray();
            static object? Convert(object? value, ITypeDefinition target) =>
                value is null || !target.TryGetRuntimeType(out var type) || value.GetType() == type || type == typeof(object)
                    ? value
                    : System.Convert.ChangeType(value is char c ? (int)c : value, type, CultureInfo.InvariantCulture);
            dynamic x = Convert(row.LeftValue, parameters[0])!;
            dynamic? y = parameters.Length > 1 ? Convert(row.RightValue, parameters[1]) : null;
            object? value = row.Op switch {
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
            return Typed(value);
        }
        catch (Exception ex) {
            return $"throws {(ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex).GetType().Name}";
        }
    }

    private static readonly Lazy<IReadOnlyDictionary<FoldGridRow, ParityRow>> ParityByRow = new(() =>
        Folded().ToDictionary(static f => f.Row, static f => AstParity.Compare(f.Row.Key, ConstantTree(f.Row))));

    /// <summary>The printed-C# row for a folded row: Roslyn compiles the authored constant tree.</summary>
    public static ParityRow Parity(FoldGridRow row) => ParityByRow.Value[row];

    /// <summary>
    /// CS0220 is Roslyn's checked constant context: the writer prints <c>int.MaxValue + 'b'</c> without <c>unchecked</c>
    /// (found, not fixed). The operator is defined, and the binder's unchecked value is the C# value.
    /// </summary>
    public static bool RoslynRejectsOnlyTheCheckedConstant(ParityRow row) =>
        row.Printed == ParityDisposal.Rejected && row.PrintedOutcome.StartsWith("Roslyn compile errors:", StringComparison.Ordinal)
        && row.PrintedOutcome.Contains("error CS0220", StringComparison.Ordinal)
        && Regex.Matches(row.PrintedOutcome, "error CS").Count == 1;

    public static bool RoslynRejectsTheOperator(ParityRow row) =>
        row.Printed == ParityDisposal.Rejected && row.PrintedOutcome.StartsWith("Roslyn compile errors:", StringComparison.Ordinal)
        && !RoslynRejectsOnlyTheCheckedConstant(row);

    /// <summary>Order-independent fingerprint of a line set: SHA-256 over the sorted lines, 16 hex characters.</summary>
    public static string Checksum(IEnumerable<string> lines) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines.Order(StringComparer.Ordinal))))).ToLowerInvariant()[..16];

    public static string Pin(IReadOnlyCollection<string> lines) => $"{lines.Count} {Checksum(lines)}";
}

/// <summary>
/// T-20: constant folding takes its numeric rules from the type provider (<c>GetOperator</c>). The grid tests are
/// generated: the master fold list is persisted as counts and checksums, the oracle compares every fold with the C#
/// value (the binder and Roslyn) and with the unfolded VM, and the cells where the VM computes a wrong value are derived
/// from the operator ledger, never listed by hand.
/// </summary>
public class ConstantFoldingProviderTests {
    // ---- (a) the master fold list ----

    /// <summary>
    /// The fold list of master (0f79ba45), persisted as <see cref="MasterFolds"/> (2,640 rows, each with its value and
    /// type) and re-derived from the head: the kept folds (folded now) plus the 36 short-circuit rows and the 828
    /// provider-undefined rows, whose master values <see cref="MasterUndefinedRowsAsOnMaster"/> reconstructs. Equal
    /// checksums mean every master fold is either kept to the same value and type or a listed delta row with the value
    /// master gave it. A change detector, not a proof.
    /// </summary>
    [Test]
    public async Task MasterFoldList_IsReconstructedFromTheHead() {
        var kept = ConstantFoldGrid.Folded().Where(static f => !ConstantFoldGrid.IsNamedNewFold(f.Row))
            .Select(static f => $"{f.Row.Key} = {f.Fold}");
        var shortCircuit = ShortCircuitRows().Select(static row => $"{row.Key} = Boolean:{(row.Op is OperatorKind.Or ? "True" : "False")}");
        var undefined = MasterUndefinedRowsAsOnMaster();
        await Assert.That(ConstantFoldGrid.Pin([.. kept, .. shortCircuit, .. undefined])).IsEqualTo(MasterFolds);
    }

    /// <summary>
    /// The 828 provider-undefined rows master folded, with master's values: a comparison or shift the unfolded VM
    /// still computes (Decimal against Float, signed against UInt64, Int64 shifted by Int64, Text ordering) has the VM's
    /// value, which is value-neutral; an equality across mismatched types without a Decimal or Float operand was
    /// <c>object.Equals</c>, false. The checksum is the one recorded on master (<see cref="MasterUndefinedFolds"/>).
    /// </summary>
    [Test]
    public async Task MasterUndefinedFolds_AreReconstructedWithTheirMasterValues() =>
        await Assert.That(ConstantFoldGrid.Pin(MasterUndefinedRowsAsOnMaster())).IsEqualTo(MasterUndefinedFolds);

    private static readonly HashSet<string> NumericOperands = ["Number", "Int32", "Decimal", "Float", "Float32", "Int8", "Int16", "UInt8", "UInt16", "UInt32", "UInt64", "Char"];

    private static bool IsComparison(OperatorKind kind) => kind is OperatorKind.Equal or OperatorKind.NotEqual
        or OperatorKind.LessThan or OperatorKind.LessThanOrEqual or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual;

    /// <summary>The 672 rows master folded through <c>object.Equals</c>: <c>==</c>/<c>!=</c> across mismatched types with a non-numeric operand and no Decimal or Float one.</summary>
    private static bool IsMismatchedEquality(FoldGridRow row) =>
        row.Op is OperatorKind.Equal or OperatorKind.NotEqual && row.Left.Name != row.Right!.Name
        && (!NumericOperands.Contains(row.Left.Name) || !NumericOperands.Contains(row.Right.Name))
        && row.Left.Name is not ("Decimal" or "Float" or "Float32") && row.Right.Name is not ("Decimal" or "Float" or "Float32");

    private static bool IsMasterUndefinedFold(FoldGridRow row) {
        if (ConstantFoldGrid.IsLogical(row.Op) || ConstantFoldGrid.Resolution(row) is not null)
            return false;
        var (l, r) = (row.Left.Name, row.Right?.Name);
        return (IsComparison(row.Op) && ((l, r) is ("Decimal", "Float" or "Float32") or ("Float" or "Float32", "Decimal")
                || (l, r) is ("Int8" or "Int16" or "Int32" or "Number", "UInt64") or ("UInt64", "Int8" or "Int16" or "Int32" or "Number")
                || (row.Op is not (OperatorKind.Equal or OperatorKind.NotEqual) && (l, r) is ("Text", "Text"))))
            || (row.Op is OperatorKind.ShiftLeft or OperatorKind.ShiftRight && (l, r) is ("Number", "Number"))
            || IsMismatchedEquality(row);
    }

    private static string[] MasterUndefinedRowsAsOnMaster() =>
        [.. ConstantFoldGrid.Rows.Where(IsMasterUndefinedFold).Select(static row => IsMismatchedEquality(row)
            ? $"{row.Key} = Boolean:{(row.Op is OperatorKind.NotEqual ? "True" : "False")}"
            : $"{row.Key} = {ConstantFoldGrid.UnfoldedVm(row)}")];

    private static FoldGridRow[] ShortCircuitRows() =>
        [.. ConstantFoldGrid.Rows.Where(static row => row.Op is OperatorKind.And or OperatorKind.Or
            && row.Left.Name == "Boolean" && row.Right!.Name != "Boolean"
            && row.LeftValue is bool left && left == (row.Op is OperatorKind.Or))];

    /// <summary>
    /// Round-1 amendment A10 said every stopped fold is value-neutral with the VM. It is not for 664 rows: an equality
    /// across mismatched types (a non-numeric operand, no Decimal or Float one) that master folded to false/true is now
    /// rejected at compile with <c>VmTypeCompatibility</c> ("comparison between incompatible types"), as C# rejects it
    /// (CS0019). The 8 Char against Text rows, which the VM still computes, are excluded.
    /// </summary>
    [Test]
    public async Task MismatchedTypeEqualities_MasterFolded_AreRejectedAtCompile() {
        var rows = ConstantFoldGrid.Rows.Where(static row => IsMismatchedEquality(row) && ConstantFoldGrid.Resolution(row) is null
            && ConstantFoldGrid.UnfoldedVm(row).StartsWith("throws", StringComparison.Ordinal)).ToArray();
        await Assert.That(ConstantFoldGrid.Pin([.. rows.Select(static row => row.Key)])).IsEqualTo(RejectedMismatchedEqualities);
        var notRejected = new List<string>();
        foreach (var row in rows) {
            if (ConstantFoldGrid.FoldOf(row) is { } fold) {
                notRejected.Add($"{row.Key}: folds {fold}");
                continue;
            }
            try {
                Interpreter.Compile(ConstantFoldGrid.ConstantTree(row));
                notRejected.Add($"{row.Key}: compiles");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("incompatible types", StringComparison.Ordinal)) {
            }
            catch (Exception ex) {
                notRejected.Add($"{row.Key}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        await Assert.That(notRejected).IsEmpty();
    }

    /// <summary>
    /// The delta rows: no fold where the provider states no operator (outside And, Or and Not). On master 828 of these
    /// rows folded (Decimal against Float comparison and equality, signed integers against UInt64, Int64 shifted by
    /// Int64, Text ordering, and <c>==</c> across mismatched non-numeric types). The undefined cells are pinned
    /// independently of folding, so a provider change cannot hide here.
    /// </summary>
    [Test]
    public async Task ProviderUndefinedCells_DoNotFold() {
        var undefined = ConstantFoldGrid.Rows
            .Where(static row => !ConstantFoldGrid.IsLogical(row.Op) && ConstantFoldGrid.Resolution(row) is null).ToArray();
        await Assert.That(ConstantFoldGrid.Pin([.. undefined.Select(static row => row.Key)])).IsEqualTo(UndefinedCells);
        var folding = undefined.Where(static row => ConstantFoldGrid.FoldOf(row) is not null)
            .Select(static row => $"{row.Key} = {ConstantFoldGrid.FoldOf(row)}").ToArray();
        await Assert.That(folding).IsEmpty();
    }

    /// <summary>
    /// The master folds T-20 keeps: every fold of a provider-defined cell outside the named new folds, to the same
    /// value and type. The delta rows are the provider-undefined folds, including the 36 short-circuit rows below.
    /// </summary>
    [Test]
    public async Task KeptFolds_FoldToTheMasterValueAndType() {
        var lines = ConstantFoldGrid.Folded()
            .Where(static f => ConstantFoldGrid.Resolution(f.Row) is not null && !ConstantFoldGrid.IsNamedNewFold(f.Row))
            .Select(static f => $"{f.Row.Key} = {f.Fold}").ToArray();
        await Assert.That(ConstantFoldGrid.Pin(lines)).IsEqualTo(KeptFolds);
    }

    /// <summary>
    /// The named new folds (the lock): every named cell folds. Their values are checked by the oracle below, which
    /// covers every fold; none sits in a ledger value-wrong cell.
    /// </summary>
    [Test]
    public async Task NamedNewFoldCells_Fold() {
        var named = ConstantFoldGrid.Rows.Where(ConstantFoldGrid.IsNamedNewFold).ToArray();
        await Assert.That(named.Length).IsEqualTo(66);
        var declining = named.Where(static row => ConstantFoldGrid.FoldOf(row) is null).Select(static row => row.Key).ToArray();
        await Assert.That(declining).IsEmpty();
    }

    // ---- (b) the oracle: AGREE, C# rejects, allowed DISAGREE ----

    /// <summary>Every fold is the value and the type the binder computes for the operator the provider states (none: no fold).</summary>
    [Test]
    public async Task EveryFold_IsTheBinderValueAndType() {
        var wrong = ConstantFoldGrid.Folded()
            .Where(static f => f.Fold != ConstantFoldGrid.Binder(f.Row))
            .Select(static f => $"{f.Row.Key}: fold {f.Fold}, binder {ConstantFoldGrid.Binder(f.Row)}").ToArray();
        await Assert.That(wrong).IsEmpty();
    }

    /// <summary>
    /// Every fold agrees with Roslyn on the authored tree (AGREE). A cell Roslyn rejects for its operator (CS0019:
    /// <c>ulong &gt; int</c> beyond a constant conversion, <c>decimal == double</c>, string ordering) must not fold (C#
    /// REJECTS); a constant the writer cannot print, or CS0220 alone (the writer's missing <c>unchecked</c>), is compared
    /// with the binder above instead.
    /// </summary>
    [Test]
    public async Task EveryFold_AgreesWithRoslyn_AndNoCellRoslynRejectsFolds() {
        var defined = ConstantFoldGrid.Folded();
        var disagree = new List<string>();
        var rejected = new List<string>();
        var checkedConstant = new List<string>();
        var unprintable = new List<string>();
        foreach (var (row, fold) in defined) {
            var parity = ConstantFoldGrid.Parity(row);
            if (parity.Printed == ParityDisposal.Executed) {
                if (!parity.Agrees)
                    disagree.Add($"{row.Key}: fold {fold}; {parity.Describe()}");
            }
            else if (ConstantFoldGrid.RoslynRejectsOnlyTheCheckedConstant(parity))
                checkedConstant.Add(row.Key);
            else if (ConstantFoldGrid.RoslynRejectsTheOperator(parity))
                rejected.Add($"{row.Key}: {parity.PrintedOutcome}");
            else
                unprintable.Add(row.Key);
        }
        await Assert.That(disagree).IsEmpty();
        await Assert.That(rejected).IsEmpty();
        // int.MaxValue + 'b' and 'b' + int.MaxValue: the writer prints no unchecked around a char operand.
        await Assert.That(checkedConstant).IsEquivalentTo(["Int32 Add Char S1", "Char Add Int32 S2"]);
        // Temporal and Guid constants have no C# literal, so the writer refuses them.
        await Assert.That(ConstantFoldGrid.Pin(unprintable)).IsEqualTo(UnprintableDefinedFolds);
    }

    /// <summary>
    /// The allowed-DISAGREE set: folds in a cell whose VM value the operator ledger records as wrong (issue 832, owner
    /// T-21). Folding is right there and the VM is wrong; T-21 inverts these pins. Derived from the ledger.
    /// </summary>
    [Test]
    public async Task FoldsInLedgerValueWrongCells_AreTheMasterSet_Pinned() {
        var lines = ConstantFoldGrid.Folded()
            .Where(static f => ConstantFoldGrid.LedgerValueWrongCells.Contains(f.Row.Cell))
            .Select(static f => $"{f.Row.Key} = {f.Fold}").ToArray();
        await Assert.That(ConstantFoldGrid.Pin(lines)).IsEqualTo(AllowedDisagree);
        // No fold outside the master fold list sits in a value-wrong cell: a named new fold there is not added.
        var named = ConstantFoldGrid.Rows.Where(static row => ConstantFoldGrid.IsNamedNewFold(row)
            && ConstantFoldGrid.LedgerValueWrongCells.Contains(row.Cell)).Select(static row => row.Key).ToArray();
        await Assert.That(named).IsEmpty();
    }

    /// <summary>Every fold is the value the unfolded VM computes, except in a ledger value-wrong cell (allowed DISAGREE).</summary>
    [Test]
    public async Task EveryFold_IsTheUnfoldedVmValue_OutsideTheLedgerValueWrongCells() {
        var wrong = ConstantFoldGrid.Folded()
            .Where(static f => !ConstantFoldGrid.LedgerValueWrongCells.Contains(f.Row.Cell))
            .Select(static f => (f.Row, f.Fold, Vm: ConstantFoldGrid.UnfoldedVm(f.Row)))
            .Where(static f => !ConstantFoldGrid.SameValue(f.Fold, f.Vm))
            .Select(static f => $"{f.Row.Key}: fold {f.Fold}, unfolded VM {f.Vm}").ToArray();
        await Assert.That(wrong).IsEmpty();
    }

    /// <summary>
    /// The allowed-DISAGREE folds really differ from the unfolded VM today; when T-21 fixes the VM these start to agree
    /// and the pin inverts.
    /// </summary>
    [Test]
    public async Task FoldsInLedgerValueWrongCells_DisagreeWithTheUnfoldedVm_T21InvertsThem() {
        var lines = ConstantFoldGrid.Folded()
            .Where(static f => ConstantFoldGrid.LedgerValueWrongCells.Contains(f.Row.Cell))
            .Select(static f => $"{f.Row.Key}: fold {f.Fold}, VM {ConstantFoldGrid.UnfoldedVm(f.Row)}")
            .Order(StringComparer.Ordinal).ToArray();
        await Assert.That(lines).IsEquivalentTo([
            "Decimal UnaryMinus - S1: fold Decimal:-0.1, VM Int64:-1",
            "Decimal UnaryMinus - S2: fold Decimal:-0.2, VM Int64:-1",
            "Float32 Divide Float32 S1: fold Single:11184811, VM Double:11184811",
            "Float32 Divide Float32 S2: fold Single:8.940697E-08, VM Double:8.940696716308594E-08"
        ]);
    }

    /// <summary>
    /// The logical folds: And, Or and Not fold only over Boolean operands. On master <c>false and X</c> and
    /// <c>true or X</c> folded whatever X was (36 grid rows), although C# rejects them (CS0019) and the unfolded VM
    /// throws; the owner's ruling of 2026-11-13 (#677) fixes that here instead of pinning it.
    /// </summary>
    [Test]
    public async Task LogicalFolds_AreBooleanOnly() {
        var lines = ConstantFoldGrid.Folded().Where(static f => ConstantFoldGrid.IsLogical(f.Row.Op))
            .Select(static f => $"{f.Row.Key} = {f.Fold}").ToArray();
        await Assert.That(ConstantFoldGrid.Pin(lines)).IsEqualTo(LogicalFolds);
    }

    /// <summary>
    /// The 36 rows master folded: a short-circuiting Boolean left operand (<c>false and</c>, <c>true or</c>) beside a
    /// non-Boolean right operand. None folds, and the constant tree is rejected at compile, as C# rejects it.
    /// </summary>
    [Test]
    public async Task ShortCircuitRowsOverANonBooleanRight_DoNotFold_AndCompileRejects() {
        var rows = ShortCircuitRows();
        await Assert.That(ConstantFoldGrid.Pin([.. rows.Select(static row => row.Key)])).IsEqualTo("36 c3b0062e01363e1c");
        var folding = rows.Where(static row => ConstantFoldGrid.FoldOf(row) is not null).Select(static row => row.Key).ToArray();
        await Assert.That(folding).IsEmpty();
        var accepted = new List<string>();
        foreach (var row in rows) {
            try {
                Interpreter.Compile(ConstantFoldGrid.ConstantTree(row));
                accepted.Add(row.Key);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("'and'/'or' requires Boolean operands", StringComparison.Ordinal)) {
            }
        }
        await Assert.That(accepted).IsEmpty();
    }

    /// <summary>
    /// <c>false and 5</c> and <c>true or "x"</c> report <c>VmTypeCompatibility</c> and do not compile; so does a
    /// short-circuit beside a non-Boolean parameter, which the simplifier must not reduce. The Boolean twins fold.
    /// </summary>
    [Test]
    public async Task ShortCircuitOverANonBooleanRightOperand_IsRejectedLikeCSharp_AndBooleanTwinsFold() {
        Node[] rejected = [
            new And(new Constant(false), new Constant(5)),
            new Or(new Constant(true), new Constant("x")),
            new And(new Constant(true), new Parameter("n", TypeReference.To<long>())),
            new Or(new Constant(false), new Parameter("s", TypeReference.To<string>())),
            new And(new Constant(false), new Parameter("m", TypeReference.To<long>()))
        ];
        foreach (var node in rejected) {
            var analysis = Interpreter.Analyze(node);
            await Assert.That(analysis.IsConstant(node)).IsFalse();
            await Assert.That(analysis.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error
                && d.Code == SyntaxTypeCompatibilityAnalyzer.DiagnosticCode)).IsTrue();
            await Assert.That(() => Interpreter.Compile(node)).Throws<InvalidOperationException>()
                .WithMessageContaining("'and'/'or' requires Boolean operands");
        }
        await AssertFolds(new And(new Constant(false), new Constant(true)), false);
        await AssertFolds(new Or(new Constant(true), new Constant(false)), true);
        await AssertFolds(new And(new Constant(false), new Parameter("p", TypeReference.To<bool>())), false);
        await AssertFolds(new Or(new Constant(true), new Parameter("q", TypeReference.To<bool>())), true);
        var flag = new Parameter("r", TypeReference.To<bool>());
        var andTrue = new And(new Constant(true), flag);
        await Assert.That(ReferenceEquals(Interpreter.Analyze(andTrue).GetNodeReplacement(andTrue), flag)).IsTrue();
    }

    // ---- (c) keep-folding pins ----

    private static INodeMetadataProvider Fold(Node ast) => Interpreter.Analyze(ast);

    private static async Task AssertFolds(Node ast, object expected) {
        var value = Fold(ast).GetConstantValue(ast);
        await Assert.That(ConstantFoldGrid.Typed(value)).IsEqualTo(ConstantFoldGrid.Typed(expected));
    }

    private static async Task AssertDoesNotFold(Node ast) =>
        await Assert.That(Fold(ast).IsConstant(ast)).IsFalse();

    [Test]
    public async Task LongGreaterThanFloat_Folds() => await AssertFolds(new GreaterThan(new Constant(3L), new Constant(2.5f)), true);

    [Test]
    public async Task UnsignedAndSmallIntegerAndCharComparisons_Fold() {
        await AssertFolds(new LessThan(new Constant(7u), new Constant(uint.MaxValue)), true);
        await AssertFolds(new Equal(new Constant(7u), new Constant(7)), true);
        await AssertFolds(new GreaterThan(new Constant(ulong.MaxValue), new Constant(7UL)), true);
        await AssertFolds(new Equal(new Constant(ulong.MaxValue), new Constant(ulong.MaxValue)), true);
        await AssertFolds(new LessThan(new Constant((byte)7), new Constant((byte)255)), true);
        await AssertFolds(new Equal(new Constant((byte)7), new Constant(7)), true);
        await AssertFolds(new GreaterThanOrEqual(new Constant((short)-7), new Constant((short)7)), false);
        await AssertFolds(new NotEqual(new Constant((short)7), new Constant(7L)), false);
        await AssertFolds(new LessThan(new Constant('a'), new Constant('b')), true);
        await AssertFolds(new Equal(new Constant('a'), new Constant(97)), true);
    }

    [Test]
    public async Task CharAdd_FoldsToInt() {
        await AssertFolds(new Add(new Constant('a'), new Constant('b')), 195);
        await AssertFolds(new Add(new Constant('a'), new Constant(1)), 98);
        await AssertFolds(new Add(new Constant(1L), new Constant('a')), 98L);
        await AssertFolds(new Add(new Constant('a'), new Constant(0.5)), 97.5);
    }

    [Test]
    public async Task LongBitwiseNot_Folds() => await AssertFolds(new BitwiseNot(new Constant(5L)), -6L);

    [Test]
    public async Task TextConcat_AndTheNameofChain_Fold() {
        await AssertFolds(new Add(new Constant("a"), new Constant("b")), "ab");
        var p = new Parameter("p", TypeReference.To<int>());
        var chain = new Add(new Add(new Constant("a"), new Invoke(new TypeReference("nameof"), p)), new Constant("b"));
        await AssertFolds(new Lambda([p], chain).Body, "apb");
    }

    [Test]
    public async Task BooleanEquality_Folds() {
        await AssertFolds(new Equal(new Constant(true), new Constant(true)), true);
        await AssertFolds(new NotEqual(new Constant(true), new Constant(false)), true);
    }

    // ---- (d) value pins ----

    /// <summary>Decimal equality ignores scale, so the pin compares the text: 1.25 * 1.2 is 1.500, 10.0 / 4 is 2.5.</summary>
    [Test]
    public async Task DecimalFolds_KeepTheScale() {
        var product = new Multiply(new Constant(1.25m), new Constant(1.2m));
        await Assert.That(((decimal)Fold(product).GetConstantValue(product)!).ToString(CultureInfo.InvariantCulture)).IsEqualTo((1.25m * 1.2m).ToString(CultureInfo.InvariantCulture));
        var quotient = new Divide(new Constant(10.0m), new Constant(4m));
        await Assert.That(((decimal)Fold(quotient).GetConstantValue(quotient)!).ToString(CultureInfo.InvariantCulture)).IsEqualTo((10.0m / 4m).ToString(CultureInfo.InvariantCulture));
        var sum = new Add(new Constant(2.50m), new Constant(0.00m));
        await Assert.That(((decimal)Fold(sum).GetConstantValue(sum)!).ToString(CultureInfo.InvariantCulture)).IsEqualTo("2.50");
    }

    [Test]
    public async Task FloatingEquality_IsIeee() {
        await AssertFolds(new Equal(new Constant(double.NaN), new Constant(double.NaN)), false);
        await AssertFolds(new NotEqual(new Constant(double.NaN), new Constant(double.NaN)), true);
        await AssertFolds(new Equal(new Constant(-0.0), new Constant(0.0)), true);
        await AssertFolds(new LessThan(new Constant(7L), new Constant(double.NaN)), false);
    }

    [Test]
    public async Task IntegerDivisionAndRemainder_TruncateTowardZero() {
        await AssertFolds(new Divide(new Constant(-7), new Constant(2)), -3);
        await AssertFolds(new Modulo(new Constant(-7), new Constant(2)), -1);
        await AssertFolds(new Modulo(new Constant(7), new Constant(-1)), 0);
    }

    /// <summary>int.MinValue / -1 and int.MinValue % -1 throw in C#: they decline. The twins beside them fold.</summary>
    [Test]
    public async Task MinValueByMinusOne_Declines_AndItsTwinsFold() {
        await AssertDoesNotFold(new Divide(new Constant(int.MinValue), new Constant(-1)));
        await AssertDoesNotFold(new Modulo(new Constant(int.MinValue), new Constant(-1)));
        await AssertFolds(new Divide(new Constant(int.MinValue), new Constant(2)), int.MinValue / 2);
        await AssertFolds(new Modulo(new Constant(int.MinValue), new Constant(3)), int.MinValue % 3);
    }

    [Test]
    public async Task DivisionByZero_Declines_AndItsTwinsFold() {
        foreach (var (zero, nonZero) in new (object, object)[] { (0, 2), (0L, 2L), (0.0, 2.0), (0m, 2m) }) {
            var ten = Convert.ChangeType(10, zero.GetType(), CultureInfo.InvariantCulture)!;
            await AssertDoesNotFold(new Divide(new Constant(ten), new Constant(zero)));
            await AssertDoesNotFold(new Modulo(new Constant(ten), new Constant(zero)));
            var divide = new Divide(new Constant(ten), new Constant(nonZero));
            await Assert.That(Fold(divide).IsConstant(divide)).IsTrue();
        }
    }

    [Test]
    public async Task DecimalOverflow_Declines_AndItsTwinFolds() {
        await AssertDoesNotFold(new Multiply(new Constant(decimal.MaxValue), new Constant(2m)));
        await AssertFolds(new Multiply(new Constant(decimal.MaxValue), new Constant(1m)), decimal.MaxValue);
    }

    /// <summary>Temporal arithmetic and unary minus stay unfolded (T-10 owns them); temporal equality beside them folds.</summary>
    [Test]
    public async Task TemporalArithmetic_DoesNotFold_AndTemporalEqualityDoes() {
        var hour = TimeSpan.FromHours(1);
        var day = new DateTime(2024, 2, 28, 10, 0, 0, DateTimeKind.Utc);
        await AssertDoesNotFold(new UnaryMinus(new Constant(hour)));
        await AssertDoesNotFold(new Add(new Constant(hour), new Constant(hour)));
        await AssertDoesNotFold(new Subtract(new Constant(hour), new Constant(hour)));
        await AssertDoesNotFold(new Add(new Constant(day), new Constant(hour)));
        await AssertDoesNotFold(new Subtract(new Constant(day), new Constant(day)));
        await AssertFolds(new Equal(new Constant(hour), new Constant(TimeSpan.FromMinutes(60))), true);
        await AssertFolds(new LessThan(new Constant(day), new Constant(day.AddDays(1))), true);
    }

    // ---- the delta: folds that stop (provider-undefined) and the named new folds ----

    private static OperatorResolution? Operator<TLeft, TRight>(OperatorKind kind) =>
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(TLeft)).GetOperator(kind, ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(TRight)));

    [Test]
    public async Task DecimalWithInt32OrInt64_Folds() {
        await AssertFolds(new Add(new Constant(1.5m), new Constant(2L)), 3.5m);
        await AssertFolds(new Subtract(new Constant(2), new Constant(0.5m)), 1.5m);
        await AssertFolds(new Multiply(new Constant(2.50m), new Constant(2)), 5.00m);
        await AssertFolds(new Divide(new Constant(7L), new Constant(2m)), 3.5m);
        await AssertFolds(new Modulo(new Constant(7.5m), new Constant(2)), 1.5m);
    }

    [Test]
    public async Task Int32BitwiseAndShift_Fold() {
        await AssertFolds(new BitwiseAnd(new Constant(6), new Constant(3)), 2);
        await AssertFolds(new BitwiseOr(new Constant(6), new Constant(3)), 7);
        await AssertFolds(new BitwiseXor(new Constant(6), new Constant(3)), 5);
        await AssertFolds(new ShiftLeft(new Constant(1), new Constant(3)), 8);
        await AssertFolds(new ShiftRight(new Constant(-16), new Constant(2)), -4);
        // C# masks an Int32 shift count to five bits.
        await AssertFolds(new ShiftLeft(new Constant(1), new Constant(33)), 2);
        await AssertFolds(new BitwiseNot(new Constant(5)), -6);
    }

    [Test]
    public async Task BooleanLogicalOperators_Fold() {
        await AssertFolds(new BitwiseAnd(new Constant(true), new Constant(false)), false);
        await AssertFolds(new BitwiseOr(new Constant(true), new Constant(false)), true);
        await AssertFolds(new BitwiseXor(new Constant(true), new Constant(true)), false);
    }

    [Test]
    public async Task MixedModulo_Folds() {
        await AssertFolds(new Modulo(new Constant(7), new Constant(4L)), 3L);
        await AssertFolds(new Modulo(new Constant(7L), new Constant(4)), 3L);
        await AssertFolds(new Modulo(new Constant(7.5), new Constant(2)), 1.5);
        await AssertFolds(new Modulo(new Constant(7), new Constant(2.5)), 2.0);
    }

    /// <summary>
    /// Companion to <c>LoweredTreeOperandCoverageTests</c>' <c>ConstantPairFolding</c> row (amendment 9). <c>1.5m + 2L</c>
    /// now folds to the decimal 3.5. The replacement that row reads is not the fold's constant: the compatibility pass
    /// runs after folding and rewrites decimal arithmetic to <c>decimal.Add</c> (as it does for a folded
    /// <c>decimal + decimal</c> on master), so the row's <c>converted</c> verdict still observes the conversion. The
    /// Parameter twin converts the same way and does not fold.
    /// </summary>
    [Test]
    public async Task ConstantPairFolding_FoldsToTheDecimal_AndTheReplacementIsStillTheCompatibilityRewrite() {
        var pair = new Add(new Constant(1.5m), new Constant(2L));
        var analysis = Interpreter.Analyze(pair);
        await Assert.That(ConstantFoldGrid.Typed(analysis.GetConstantValue(pair))).IsEqualTo("Decimal:3.5");
        await Assert.That(analysis.GetNodeReplacement(pair) is Invoke { Delegate: Member { MemberName: "Add" } }).IsTrue();

        var twin = new Add(new Parameter("d", TypeReference.To<decimal>()), new Parameter("n", TypeReference.To<long>()));
        var twinAnalysis = Interpreter.Analyze(twin);
        await Assert.That(twinAnalysis.IsConstant(twin)).IsFalse();
        await Assert.That(twinAnalysis.GetNodeReplacement(twin) is Invoke { Delegate: Member { MemberName: "Add" } }).IsTrue();
    }

    [Test]
    public async Task DecimalAgainstFloat_Declines_ProviderUndefined_AndDecimalAgainstInt32Folds() {
        await Assert.That(Operator<decimal, double>(OperatorKind.Equal)).IsNull();
        await AssertDoesNotFold(new Equal(new Constant(1.5m), new Constant(1.5)));
        await AssertDoesNotFold(new LessThan(new Constant(1.5f), new Constant(2m)));
        await AssertFolds(new Equal(new Constant(2m), new Constant(2)), true);
    }

    [Test]
    public async Task SignedAgainstUInt64_Declines_ProviderUndefined_AndUnsignedAgainstUInt64Folds() {
        await Assert.That(Operator<int, ulong>(OperatorKind.LessThan)).IsNull();
        await AssertDoesNotFold(new LessThan(new Constant(1), new Constant(ulong.MaxValue)));
        await AssertDoesNotFold(new Equal(new Constant(-1L), new Constant(ulong.MaxValue)));
        await AssertFolds(new LessThan(new Constant(1u), new Constant(ulong.MaxValue)), true);
    }

    [Test]
    public async Task Int64ShiftedByInt64_Declines_ProviderUndefined_AndInt32ShiftFolds() {
        await Assert.That(Operator<long, long>(OperatorKind.ShiftLeft)).IsNull();
        await AssertDoesNotFold(new ShiftLeft(new Constant(1L), new Constant(3L)));
        await AssertDoesNotFold(new ShiftRight(new Constant(16L), new Constant(2L)));
        await AssertFolds(new ShiftLeft(new Constant(1), new Constant(3)), 8);
    }

    [Test]
    public async Task TextOrdering_Declines_ProviderUndefined_AndTextEqualityFolds() {
        await Assert.That(Operator<string, string>(OperatorKind.LessThan)).IsNull();
        await AssertDoesNotFold(new LessThan(new Constant("a"), new Constant("b")));
        await AssertFolds(new Equal(new Constant("a"), new Constant("a")), true);
    }

    private enum Shade { Light, Dark }

    [Test]
    public async Task EnumOrdering_Declines_ProviderUndefined_AndEnumEqualityFolds() {
        await Assert.That(Operator<Shade, Shade>(OperatorKind.LessThan)).IsNull();
        await AssertDoesNotFold(new LessThan(new Constant(Shade.Light), new Constant(Shade.Dark)));
        await AssertFolds(new Equal(new Constant(Shade.Light), new Constant(Shade.Light)), true);
        await AssertFolds(new NotEqual(new Constant(Shade.Light), new Constant(Shade.Dark)), true);
    }

    [Test]
    public async Task EqualityAcrossMismatchedTypes_Declines_ProviderUndefined_AndSameTypeEqualityFolds() {
        await Assert.That(Operator<string, long>(OperatorKind.Equal)).IsNull();
        await AssertDoesNotFold(new Equal(new Constant("7"), new Constant(7L)));
        await AssertDoesNotFold(new NotEqual(new Constant(true), new Constant(1)));
        await AssertFolds(new Equal(new Constant(Guid.Empty), new Constant(Guid.Empty)), true);
        await AssertFolds(new Equal(new Constant(null), new Constant("a")), false);
    }

    private sealed record Box(int Value) {
        public static int Invocations;
        public static Box operator +(Box a, Box b) {
            Invocations++;
            return new Box(a.Value + b.Value);
        }
    }

    /// <summary>
    /// Amendment 6: a user-declared operator never runs at analysis time. On master <c>Box == Box</c> folded through
    /// <c>object.Equals</c> and the user operator <c>+</c> did not fold; both decline now. A Guid comparison (an
    /// allowlisted declaring type) folds.
    /// </summary>
    [Test]
    public async Task UserDeclaredOperators_DoNotRunAtAnalysisTime_AndAllowlistedDeclaredOperatorsFold() {
        await Assert.That(Operator<Box, Box>(OperatorKind.Equal)).IsNotNull();
        await AssertDoesNotFold(new Equal(new Constant(new Box(1)), new Constant(new Box(1))));
        var before = Box.Invocations;
        await AssertDoesNotFold(new Add(new Constant(new Box(1)), new Constant(new Box(2))));
        await Assert.That(Box.Invocations).IsEqualTo(before);
        await AssertFolds(new LessThan(new Constant(Guid.Empty), new Constant(Guid.Parse("00000000-0000-0000-0000-000000000001"))), true);
    }

    /// <summary>
    /// Amendment 10: a Parameter is typed by its stamp. A lambda parameter stamped Int32 bound to an Int64 argument
    /// disagrees with its stamp and declines (master folded it as Int64 + Int32); bound to an Int32 argument it folds.
    /// </summary>
    [Test]
    public async Task ParameterWhoseValueDisagreesWithItsStamp_Declines_AndAnAgreeingOneFolds() {
        var x = new Parameter("x", TypeReference.To<int>());
        var disagreeing = new Invoke(new Lambda([x], new Add(x, new Constant(1))), new Constant(5L));
        await AssertDoesNotFold(disagreeing);
        var y = new Parameter("y", TypeReference.To<int>());
        await AssertFolds(new Invoke(new Lambda([y], new Add(y, new Constant(1))), new Constant(5)), 6);
    }

    /// <summary>
    /// Amendment 10: a lambda parameter with no type has no stamp, so its operand has no type and the operator declines
    /// (master folded it to 6). The stamped twin folds.
    /// </summary>
    [Test]
    public async Task ParameterWithoutAStamp_Declines_AndAStampedOneFolds() {
        var x = new Parameter("x");
        var unstamped = new Invoke(new Lambda([x], new Add(x, new Constant(1))), new Constant(5));
        await Assert.That(Interpreter.Analyze(unstamped).GetResolvedType(x)).IsNull();
        await AssertDoesNotFold(unstamped);
        var y = new Parameter("y", TypeReference.To<int>());
        await AssertFolds(new Invoke(new Lambda([y], new Add(y, new Constant(1))), new Constant(5)), 6);
    }

    // The persisted counts and checksums.
    private const string MasterFolds = "2640 68cba717a778d527";
    private const string MasterUndefinedFolds = "828 0d020bf27c2afbb7";
    private const string KeptFolds = "1776 06fba54a76763aeb";
    private const string UnprintableDefinedFolds = "48 ace2db05f3c87b7b";
    private const string AllowedDisagree = "4 97551d7e2ef40a1f";
    private const string LogicalFolds = "6 d49ba859ff062537";
    private const string UndefinedCells = "7800 1786e1cb3f6eaa59";
    private const string RejectedMismatchedEqualities = "664 e544c78cf28685b8";
}