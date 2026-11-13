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
        Folded().Where(static f => !IsLogical(f.Row.Op))
            .ToDictionary(static f => f.Row, static f => AstParity.Compare(f.Row.Key, ConstantTree(f.Row))));

    /// <summary>The printed-C# row for a folded, non-logical row: Roslyn compiles the authored constant tree.</summary>
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
    /// The fold list of master (0f79ba45): every grid row the pass folds, with its value and type. A change detector,
    /// not a proof: the proof is the oracle below.
    /// </summary>
    [Test]
    public async Task MasterFoldList_IsPinned() {
        var lines = ConstantFoldGrid.Folded().Select(static f => $"{f.Row.Key} = {f.Fold}").ToArray();
        await Assert.That(ConstantFoldGrid.Pin(lines)).IsEqualTo(MasterFolds);
    }

    /// <summary>The master folds whose operator the provider does not state (outside And, Or and Not): T-20's delta rows.</summary>
    [Test]
    public async Task MasterFoldList_ProviderUndefinedRows_ArePinned() {
        var lines = ConstantFoldGrid.Folded()
            .Where(static f => !ConstantFoldGrid.IsLogical(f.Row.Op) && ConstantFoldGrid.Resolution(f.Row) is null)
            .Select(static f => $"{f.Row.Key} = {f.Fold}").ToArray();
        await Assert.That(ConstantFoldGrid.Pin(lines)).IsEqualTo(MasterUndefinedFolds);
    }

    /// <summary>The master folds T-20 keeps: every fold outside the delta rows and outside the named new folds, to the same value and type.</summary>
    [Test]
    public async Task KeptFolds_FoldToTheMasterValueAndType() {
        var lines = ConstantFoldGrid.Folded()
            .Where(static f => (ConstantFoldGrid.IsLogical(f.Row.Op) || ConstantFoldGrid.Resolution(f.Row) is not null)
                && !ConstantFoldGrid.IsNamedNewFold(f.Row))
            .Select(static f => $"{f.Row.Key} = {f.Fold}").ToArray();
        await Assert.That(ConstantFoldGrid.Pin(lines)).IsEqualTo(KeptFolds);
    }

    [Test]
    public async Task NamedNewFoldCells_DoNotFoldOnMaster() {
        var named = ConstantFoldGrid.Rows.Where(ConstantFoldGrid.IsNamedNewFold).ToArray();
        await Assert.That(named.Length).IsEqualTo(66);
        // Boolean == and != are named and already fold; the named cells here are the ones that do not.
        var folding = named.Where(static row => ConstantFoldGrid.FoldOf(row) is not null).Select(static row => row.Key).ToArray();
        await Assert.That(folding).IsEmpty();
    }

    // ---- (b) the oracle: AGREE, C# rejects, allowed DISAGREE ----

    /// <summary>Every fold of a provider-defined cell is the value and the type the binder computes for that operator.</summary>
    [Test]
    public async Task EveryDefinedFold_IsTheBinderValueAndType() {
        var wrong = ConstantFoldGrid.Folded()
            .Where(static f => !ConstantFoldGrid.IsLogical(f.Row.Op) && ConstantFoldGrid.Resolution(f.Row) is not null)
            .Where(static f => f.Fold != ConstantFoldGrid.Binder(f.Row))
            .Select(static f => $"{f.Row.Key}: fold {f.Fold}, binder {ConstantFoldGrid.Binder(f.Row)}").ToArray();
        await Assert.That(wrong).IsEmpty();
    }

    /// <summary>
    /// Every fold of a provider-defined cell agrees with Roslyn on the authored tree. A cell Roslyn rejects for its
    /// operator (CS0019, CS0020) must not fold; a constant the writer cannot print, or CS0220 alone (the writer's missing
    /// <c>unchecked</c>), is compared with the binder above instead.
    /// </summary>
    [Test]
    public async Task EveryDefinedFold_AgreesWithRoslyn_AndNoCellRoslynRejectsFolds() {
        var defined = ConstantFoldGrid.Folded()
            .Where(static f => !ConstantFoldGrid.IsLogical(f.Row.Op) && ConstantFoldGrid.Resolution(f.Row) is not null).ToArray();
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

    /// <summary>Every fold of a provider-defined cell is the value the unfolded VM computes, except in a ledger value-wrong cell.</summary>
    [Test]
    public async Task EveryDefinedFold_IsTheUnfoldedVmValue_OutsideTheLedgerValueWrongCells() {
        var wrong = ConstantFoldGrid.Folded()
            .Where(static f => !ConstantFoldGrid.IsLogical(f.Row.Op) && ConstantFoldGrid.Resolution(f.Row) is not null
                && !ConstantFoldGrid.LedgerValueWrongCells.Contains(f.Row.Cell))
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
    /// Found, not fixed (outside T-20, which leaves <c>FoldAnd</c> and <c>FoldOr</c> alone): <c>false and X</c> and
    /// <c>true or X</c> fold whatever X is, so a non-Boolean right operand that C# rejects (CS0019) and the unfolded VM
    /// throws on still folds. Pinned as it is today.
    /// </summary>
    [Test]
    public async Task LogicalFolds_AreUntouched_IncludingANonBooleanRightOperand() {
        var lines = ConstantFoldGrid.Folded().Where(static f => ConstantFoldGrid.IsLogical(f.Row.Op))
            .Select(static f => $"{f.Row.Key} = {f.Fold}").ToArray();
        await Assert.That(ConstantFoldGrid.Pin(lines)).IsEqualTo(LogicalFolds);
        var nonBoolean = ConstantFoldGrid.Folded()
            .Where(static f => f.Row.Op is OperatorKind.And or OperatorKind.Or && f.Row.Right!.Name != "Boolean").ToArray();
        await Assert.That(nonBoolean.Length).IsEqualTo(36);
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

    // The persisted counts and checksums.
    private const string MasterFolds = "2640 68cba717a778d527";
    private const string MasterUndefinedFolds = "828 0d020bf27c2afbb7";
    private const string KeptFolds = "1812 2520a051640c393f";
    private const string UnprintableDefinedFolds = "48 ace2db05f3c87b7b";
    private const string AllowedDisagree = "4 97551d7e2ef40a1f";
    private const string LogicalFolds = "42 ea3948634df0953b";
}