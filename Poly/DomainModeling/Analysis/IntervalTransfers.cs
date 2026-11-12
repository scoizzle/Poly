using System.Collections.Frozen;

using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The interval of an operator applied to its operand intervals — one immutable row per node kind, so a
/// new node adds a row beside it and never a second evaluator (A4, B6). A node with no row is
/// <c>null</c> (unknown): the transfer never guesses an interval.
/// </summary>
internal static class IntervalTransfers {
    private static readonly FrozenDictionary<Type, Func<ValueRange?, ValueRange?, ValueRange?>> Rows =
        new Dictionary<Type, Func<ValueRange?, ValueRange?, ValueRange?>> {
            [typeof(Add)] = static (left, right) => Combine(left, right, static (l, r) => l.Add(r)),
            [typeof(Subtract)] = static (left, right) => Combine(left, right, static (l, r) => l.Sub(r)),
            [typeof(Multiply)] = static (left, right) => left is null || right is null
                // An unknown operand still multiplies by exactly [0, 0] to 0 (0 · ±∞ = 0).
                ? (IsZero(left) || IsZero(right) ? new ValueRange(0m, 0m) : null)
                : left.Mul(right),
        }.ToFrozenDictionary();

    /// <summary>The interval of <paramref name="node"/> over <paramref name="operands"/>, or null when the
    /// node's transfer is not modelled.</summary>
    internal static ValueRange? Transfer(DomainExpression node, IReadOnlyList<ValueRange?> operands) =>
        operands.Count == 2 && Rows.TryGetValue(node.GetType(), out var row) ? row(operands[0], operands[1]) : null;

    private static ValueRange? Combine(
        ValueRange? left, ValueRange? right, Func<ValueRange, ValueRange?, ValueRange?> op) =>
        left is null ? null : op(left, right);

    private static bool IsZero(ValueRange? range) => range is { Min: 0m, Max: 0m };
}