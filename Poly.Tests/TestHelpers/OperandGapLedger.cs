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
        new(812, "T-22", "Assignment", "IndexTextIntoNumberArray", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "Await", "NumberOperand", "analysis=unchanged;compile=backstop"),
        new(813, "T-09b", "Coalesce", "EntityDecimal", "analysis=unchanged;compile=ok"),
        new(605, "T-09b", "Coalesce", "OptionalDecimalNumber", "analysis=unchanged;compile=backstop"),
        new(605, "T-09b", "Coalesce", "OptionalNumberDecimal", "analysis=unchanged;compile=backstop"),
        new(813, "T-09b", "Coalesce", "TextNumber", "analysis=unchanged;compile=backstop"),
        new(605, "T-09b", "Conditional", "DecimalNumber", "analysis=unchanged;compile=backstop"),
        new(813, "T-09b", "Conditional", "DecimalText", "analysis=unchanged;compile=ok"),
        new(813, "T-09b", "Conditional", "EntityDecimal", "analysis=unchanged;compile=ok"),
        new(803, "T-06", "Conditional", "NullableThenNumberNullTaken", "analysis=unchanged;compile=run-threw:InvalidOperationException"),
        new(812, "T-22", "Conditional", "NumberCondition", "analysis=unchanged;compile=ok"),
        new(605, "T-09b", "Conditional", "NumberDecimal", "analysis=unchanged;compile=backstop"),
        new(803, "T-06", "Conditional", "NumberThenNullableNullTaken", "analysis=unchanged;compile=run-threw:InvalidOperationException"),
        new(813, "T-09b", "Conditional", "TextNumber", "analysis=unchanged;compile=backstop"),
        new(605, "T-09b", "Conditional", "ThreeArmsDecimalNumberDecimal", "analysis=unchanged;compile=backstop"),
        new(813, "T-09b", "Conditional", "ThreeArmsTextInside", "analysis=unchanged;compile=backstop"),
        new(812, "T-22", "DoWhileLoop", "NumberCondition", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "ForEachLoop", "DeclaredDecimalElementNumber", "analysis=unchanged;compile=backstop"),
        new(812, "T-22", "ForLoop", "NumberCondition", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "IfStatement", "NumberCondition", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "IndexAccess", "IndexDecimal", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "IndexAccess", "IndexOptional", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "IndexAccess", "IndexText", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "Lambda", "ArgumentNumberForDecimalParameter", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "Lambda", "ArgumentTextForNumberParameter", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "New", "ArgumentNumberForDecimalParameter", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "NewArray", "LengthDecimal", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "NewArray", "LengthText", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "Return", "DecimalThenNumber", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "Return", "DecimalThenText", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "StridedSetBits", "StepText", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "SwitchStatement", "DecimalCaseNumberDefault", "analysis=unchanged;compile=ok"),
        new(805, "T-21", "SwitchStatement", "Issue805RawReadKey0", "analysis=unchanged;compile=wrong-value"),
        new(814, "T-21", "SwitchStatement", "Issue805RawReadKey1", "analysis=unchanged;compile=ok"),
        new(814, "T-21", "SwitchStatement", "NumberCaseDecimalDefault", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "SwitchStatement", "PatternTextAgainstNumberSubject", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "SwitchStatement", "TextCaseNumberDefault", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "ThrowExpression", "NumberOperand", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "ThrowStatement", "NumberOperand", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "TypeAs", "NumberToText", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "TypeCast", "EntityToNumber", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "UsingStatement", "NumberResource", "analysis=unchanged;compile=ok"),
        new(812, "T-22", "WhileLoop", "NumberCondition", "analysis=unchanged;compile=ok"),
    ];
}

/// <summary>
/// One cell of an interim rule of the lowered-tree analysis: the rule, the row that removes it, the interim row
/// (<see cref="Kind"/>, <see cref="Case"/>) of <c>LoweredTreeOperandCoverageTests.InterimRows</c>, and what the cell does
/// today: the row's observation, the node's stamp, and whether that agrees with C# (the row's expectation). Removing a rule
/// changes its cells' observation and fails <c>InterimRules_EveryCellIsAsListed</c>; the removing row edits or deletes
/// its entries.
/// </summary>
public sealed record KnownInterimCell(string Owner, string Rule, string Kind, string Case, string Observed);

/// <summary>The interim rules of the lowered-tree analysis and every cell they decide, sorted by <c>(Kind, Case)</c>.</summary>
public static class KnownInterimRules {
    public static IReadOnlyList<KnownInterimCell> All { get; } = [
        new("T-22b", "null and unresolved operand skip", "Add", "NullWithNumber", "analysis=unchanged;compile=backstop;type=none;csharp=differs"),
        new("T-22b", "null and unresolved operand skip", "Add", "TextWithUnresolved", "analysis=unchanged;compile=backstop;type=none;csharp=differs"),
        new("T-29", "EnumBitwiseRule", "BitwiseAnd", "EnumEnum", "analysis=unchanged;compile=ok;type=AttributeTargets;csharp=agrees"),
        new("T-29", "EnumBitwiseRule", "BitwiseAnd", "EnumNumber", "analysis=rejected;compile=rejected-by-analysis;type=none;csharp=agrees"),
        new("T-29", "EnumBitwiseRule", "BitwiseAnd", "OptionalEnumEnum", "analysis=unchanged;compile=backstop;type=Nullable`1;csharp=differs"),
        new("T-26", "ConstantOperandRule (the provider has no constant conversion)", "BitwiseAnd", "UInt32WithIntConstant", "analysis=unchanged;compile=ok;type=Int64;csharp=agrees"),
        new("T-29", "EnumBitwiseRule", "BitwiseNot", "Enum", "analysis=unchanged;compile=ok;type=AttributeTargets;csharp=agrees"),
        new("T-29", "EnumBitwiseRule", "BitwiseOr", "EnumEnum", "analysis=unchanged;compile=ok;type=AttributeTargets;csharp=agrees"),
        new("T-29", "EnumBitwiseRule", "BitwiseXor", "EnumEnum", "analysis=unchanged;compile=ok;type=AttributeTargets;csharp=agrees"),
        new("T-07", "UndeclaredComparisonRule", "Equal", "ClrStructWithoutEquality", "analysis=unchanged;compile=ok;type=Boolean;csharp=differs"),
        new("T-21b", "NullableDateRule narrowed: a nullable temporal beside a different type (C# compiles DateTime? == DateTimeOffset; the VM's nullable compare does not convert)", "Equal", "DateTimeOffsetBesideOptionalDateTime", "analysis=rejected;compile=rejected-by-analysis;type=Boolean;csharp=differs"),
        new("T-21b", "the VM runs Object.Equals where the tree binds (object, object) reference equality", "Equal", "DelegateBesideDelegateSameMethod", "analysis=unchanged;compile=wrong-value;type=Boolean;csharp=differs"),
        new("T-21b", "NullableDateRule narrowed: a nullable temporal beside a different type (C# compiles DateTime? == DateTimeOffset; the VM's nullable compare does not convert)", "Equal", "OptionalDateTimeBesideDateTimeOffset", "analysis=rejected;compile=rejected-by-analysis;type=Boolean;csharp=differs"),
        new("T-22b", "void operand skip", "Equal", "VoidCallWithNumber", "analysis=unchanged;compile=ok;type=Boolean;csharp=differs"),
        new("T-07", "UndeclaredComparisonRule", "LessThan", "ModelledStruct", "analysis=unchanged;compile=ok;type=Boolean;csharp=differs"),
        new("T-21b", "the VM runs Object.Equals where the tree binds (object, object) reference equality", "NotEqual", "DelegateBesideDelegateSameMethod", "analysis=unchanged;compile=wrong-value;type=Boolean;csharp=differs"),
        new("T-29", "EnumBitwiseRule", "ShiftLeft", "EnumNumber", "analysis=rejected;compile=rejected-by-analysis;type=none;csharp=agrees"),
        new("T-29", "EnumBitwiseRule", "UnaryMinus", "Enum", "analysis=rejected;compile=rejected-by-analysis;type=none;csharp=agrees"),
        new("T-22b", "null and unresolved operand skip", "UnaryMinus", "Null", "analysis=unchanged;compile=backstop;type=none;csharp=differs"),
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