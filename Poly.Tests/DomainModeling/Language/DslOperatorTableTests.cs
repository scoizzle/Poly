using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// G-10: the operator table is where the expression driver reads precedence and associativity, so
/// adding an operator is a row and not a grammar edit (F-04's unary minus, F-05's <c>%</c>).
/// </summary>
public sealed class DslOperatorTableTests {
    private static BinaryRow Binary(DslOperatorTable table, DslTokenKind op, DslTokenKind? follow = null) =>
        table.BinaryRows.Single(r => r.Op == op && r.Follow == follow);

    [Test]
    public async Task Core_HasEveryProductBinaryRowAndTheNotPrefix() {
        var table = DslOperatorTable.Core();

        var binary = table.BinaryRows.Select(r => (r.Op, r.Follow)).ToHashSet();
        foreach (var op in new[] {
                     DslTokenKind.Or, DslTokenKind.And, DslTokenKind.Plus, DslTokenKind.Minus, DslTokenKind.Star,
                     DslTokenKind.Slash, DslTokenKind.Is, DslTokenKind.Eq, DslTokenKind.Neq, DslTokenKind.Gt,
                     DslTokenKind.Gte, DslTokenKind.Lt, DslTokenKind.Lte,
                 })
            await Assert.That(binary.Contains((op, null))).IsTrue();
        await Assert.That(binary.Contains((DslTokenKind.Is, DslTokenKind.Not))).IsTrue();
        await Assert.That(table.PrefixRows.Select(r => r.Op).Single()).IsEqualTo(DslTokenKind.Not);
    }

    [Test]
    public async Task Core_PrecedenceOrder_OrAndCompareAddMul() {
        var table = DslOperatorTable.Core();

        var or = Binary(table, DslTokenKind.Or).Precedence;
        var and = Binary(table, DslTokenKind.And).Precedence;
        var compare = Binary(table, DslTokenKind.Gt).Precedence;
        var add = Binary(table, DslTokenKind.Plus).Precedence;
        var mul = Binary(table, DslTokenKind.Star).Precedence;

        await Assert.That(or).IsLessThan(and);
        await Assert.That(and).IsLessThan(compare);
        await Assert.That(compare).IsLessThan(add);
        await Assert.That(add).IsLessThan(mul);
        await Assert.That(Binary(table, DslTokenKind.Minus).Precedence).IsEqualTo(add);
        await Assert.That(Binary(table, DslTokenKind.Slash).Precedence).IsEqualTo(mul);
        await Assert.That(table.Levels.Select(l => l.Precedence).SequenceEqual([or, and, compare, add, mul])).IsTrue();
    }

    [Test]
    public async Task Core_CompareIsNonAssociative_EveryOtherRowIsLeft() {
        var table = DslOperatorTable.Core();

        foreach (var op in new[] { DslTokenKind.Is, DslTokenKind.Eq, DslTokenKind.Neq, DslTokenKind.Gt, DslTokenKind.Gte, DslTokenKind.Lt, DslTokenKind.Lte })
            await Assert.That(Binary(table, op).Assoc).IsEqualTo(Assoc.None);
        foreach (var op in new[] { DslTokenKind.Or, DslTokenKind.And, DslTokenKind.Plus, DslTokenKind.Minus, DslTokenKind.Star, DslTokenKind.Slash })
            await Assert.That(Binary(table, op).Assoc).IsEqualTo(Assoc.Left);
        await Assert.That(Binary(table, DslTokenKind.Is, DslTokenKind.Not).Assoc).IsEqualTo(Assoc.None);
    }

    /// <summary><c>not</c> takes an add-level operand, and applies at the head of the compare level, so its result is closed to a comparison.</summary>
    [Test]
    public async Task Core_NotOperandIsTheAddLevel_AndItAppliesAtTheCompareHead() {
        var table = DslOperatorTable.Core();
        var not = table.PrefixRows.Single();

        await Assert.That(not.OperandPrecedence).IsEqualTo(Binary(table, DslTokenKind.Plus).Precedence);
        await Assert.That(table.HeadLevel(not)).IsEqualTo(table.LevelOf(DslTokenKind.Gt));
    }

    [Test]
    public async Task With_DuplicateRow_Throws() {
        var table = DslOperatorTable.Core();
        var again = new BinaryRow(DslTokenKind.Plus, 50, Assoc.Left, DomainExpression.Add);

        await Assert.That(() => table.With(again)).Throws<InvalidOperationException>();
        await Assert.That(() => table.With(new PrefixRow(DslTokenKind.Not, 50, DomainExpression.Not))).Throws<InvalidOperationException>();
    }

    /// <summary>A row registered on a copy of the table parses with no grammar change: <c>in</c> binds between a comparison and a sum.</summary>
    [Test]
    public async Task With_ExtraRow_ParsesWithNoGrammarEdit() {
        var table = DslOperatorTable.Core().With(new BinaryRow(
            DslTokenKind.In, 45, Assoc.Left, (left, right) => DomainExpression.Equal(left, right)));

        var reader = new DslTokenReader("a in b + c");
        var cursor = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(DslGrammar.Core, reader));
        var parsed = new DslExpressionParser(cursor, operators: table).ParseExpression();

        await Assert.That(cursor.Current.Kind).IsEqualTo(DslTokenKind.EndOfFile);
        var comparison = (Comparison)parsed;
        await Assert.That(comparison.Kind).IsEqualTo(ComparisonKind.Equal);
        await Assert.That(((PropertyAccess)comparison.Left).Name).IsEqualTo("a");
        await Assert.That(comparison.Right).IsTypeOf<Poly.DomainModeling.Ontology.Add>();
    }

    /// <summary>The same token with no row is not an operator: the default table does not know <c>in</c>.</summary>
    [Test]
    public async Task Core_DoesNotKnowAnUnregisteredOperator() {
        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment("a in b")).Throws<FormatException>();
    }
}