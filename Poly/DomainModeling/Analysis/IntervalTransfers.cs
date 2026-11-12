using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The interval of an operator applied to its operand intervals — one immutable row per node kind, so a
/// new node adds a row beside it and never a second evaluator (A4, B6). A node with no row is
/// <c>null</c> (unknown): the transfer never guesses an interval.
/// </summary>
internal static class IntervalTransfers {
    /// <summary>The interval of <paramref name="node"/> over <paramref name="operands"/>, or null when the
    /// node's transfer is not modelled.</summary>
    internal static ValueRange? Transfer(DomainExpression node, IReadOnlyList<ValueRange?> operands) => null;
}
