namespace Poly.Tests.TestHelpers;

/// <summary>
/// One known operand-agreement gap: the lowered-tree row (<see cref="Kind"/>, <see cref="Case"/>) whose
/// analysis does not do what the row expects, the exact outcome it produces today
/// (<see cref="Observed"/>), and the issue and slice (<see cref="Owner"/>, a slice id) that fix it.
/// The fixing slice deletes the entry; a stale entry is a failure, never a narrowing.
/// </summary>
public sealed record KnownOperandGap(int Issue, string Owner, string Kind, string Case, string Observed);

/// <summary>Every known operand-agreement gap, sorted by <c>(Kind, Case)</c>.</summary>
public static class KnownOperandGaps {
    public static IReadOnlyList<KnownOperandGap> All { get; } = [
        new(812, "T-22", "Add", "EntityNumber", "analysis=unchanged;compile=ok"),
        new(815, "T-18", "Add", "Float64Decimal", "analysis=converted;compile=ok"),
        new(812, "T-22", "Assignment", "IndexTextIntoNumberArray", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "BitwiseAnd", "NumberDecimal", "analysis=unchanged;compile=backstop"),
        new(812, "T-22", "BitwiseAnd", "NumberText", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "BitwiseOr", "NumberDecimal", "analysis=unchanged;compile=backstop"),
        new(812, "T-22", "BitwiseXor", "NumberText", "analysis=unchanged;compile=ok"),
        new(813, "T-09", "Coalesce", "EntityDecimal", "analysis=unchanged;compile=ok"),
        new(605, "T-09", "Coalesce", "OptionalDecimalNumber", "analysis=unchanged;compile=backstop"),
        new(605, "T-09", "Coalesce", "OptionalNumberDecimal", "analysis=unchanged;compile=backstop"),
        new(813, "T-09", "Coalesce", "TextNumber", "analysis=unchanged;compile=backstop"),
        new(605, "T-09", "Conditional", "DecimalNumber", "analysis=unchanged;compile=backstop"),
        new(813, "T-09", "Conditional", "DecimalText", "analysis=unchanged;compile=ok"),
        new(813, "T-09", "Conditional", "EntityDecimal", "analysis=unchanged;compile=ok"),
        new(803, "T-06", "Conditional", "NullableThenNumberNullTaken", "analysis=unchanged;compile=run-threw:InvalidOperationException"),
        new(812, "T-22", "Conditional", "NumberCondition", "analysis=unchanged;compile=ok"),
        new(605, "T-09", "Conditional", "NumberDecimal", "analysis=unchanged;compile=backstop"),
        new(803, "T-06", "Conditional", "NumberThenNullableNullTaken", "analysis=unchanged;compile=run-threw:InvalidOperationException"),
        new(813, "T-09", "Conditional", "TextNumber", "analysis=unchanged;compile=backstop"),
        new(605, "T-09", "Conditional", "ThreeArmsDecimalNumberDecimal", "analysis=unchanged;compile=backstop"),
        new(813, "T-09", "Conditional", "ThreeArmsTextInside", "analysis=unchanged;compile=backstop"),
        new(812, "T-22", "DoWhileLoop", "NumberCondition", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "Equal", "EntityNumber", "analysis=unchanged;compile=ok"),
        new(815, "T-18", "Equal", "Float64Decimal", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "ForEachLoop", "DeclaredDecimalElementNumber", "analysis=unchanged;compile=backstop"),
        new(812, "T-22", "ForLoop", "NumberCondition", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "IfStatement", "NumberCondition", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "IndexAccess", "IndexDecimal", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "IndexAccess", "IndexOptional", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "IndexAccess", "IndexText", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "Lambda", "ArgumentNumberForDecimalParameter", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "Lambda", "ArgumentTextForNumberParameter", "analysis=unchanged;compile=ok"),
        new(815, "T-18", "LessThan", "Float64Decimal", "analysis=unchanged;compile=ok"),
        new(815, "T-18", "Multiply", "Float64Decimal", "analysis=converted;compile=ok"),
        new(814, "T-21", "New", "ArgumentNumberForDecimalParameter", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "NewArray", "LengthDecimal", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "NewArray", "LengthText", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "Return", "DecimalThenNumber", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "Return", "DecimalThenText", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "ShiftLeft", "DecimalNumber", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "ShiftLeft", "NumberText", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "ShiftRight", "TextNumber", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "SwitchStatement", "DecimalCaseNumberDefault", "analysis=unchanged;compile=ok"),
        new(805, "T-21", "SwitchStatement", "Issue805RawReadKey0", "analysis=unchanged;compile=wrong-value"),
        new(814, "T-21", "SwitchStatement", "Issue805RawReadKey1", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "SwitchStatement", "NumberCaseDecimalDefault", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "SwitchStatement", "PatternTextAgainstNumberSubject", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "SwitchStatement", "TextCaseNumberDefault", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "TypeAs", "NumberToText", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "TypeCast", "EntityToNumber", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "WhileLoop", "NumberCondition", "analysis=unchanged;compile=ok"),
    ];
}

/// <summary>
/// Adapts operand-gap rows to <see cref="ParityDifference"/> so <see cref="ParityDifferenceSet.Compare"/>
/// is the one comparison: <c>Step</c> is the kind, <c>Field</c> is <c>Kind.Case</c> (a case name is only
/// unique within its kind), <c>Simulate</c> is the observed outcome.
/// </summary>
public static class OperandGapRatchet {
    public static IReadOnlyList<ParityDrift> Drift(
        IEnumerable<KnownOperandGap> ledger, IEnumerable<(string Kind, string Case, string Observed)> live) =>
        ParityDifferenceSet.Compare(
            [.. ledger.Select(gap => Row(gap.Kind, gap.Case, gap.Observed))],
            [.. live.Select(gap => Row(gap.Kind, gap.Case, gap.Observed))]);

    static ParityDifference Row(string kind, string caseName, string observed) =>
        new(0, kind, $"{kind}.{caseName}", observed, "");
}