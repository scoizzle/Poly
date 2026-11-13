using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Meaning;
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

    /// <summary>A row registered on a copy of the table parses with no grammar change: <c>in</c> binds tighter than a sum and looser than a product.</summary>
    [Test]
    public async Task With_ExtraRow_ParsesWithNoGrammarEdit() {
        var table = DslOperatorTable.Core().With(new BinaryRow(
            DslTokenKind.In, 55, Assoc.Left, (left, right) => DomainExpression.Equal(left, right)));

        var reader = new DslTokenReader("a in b * c");
        var cursor = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(DslGrammar.Core, reader));
        var parsed = new DslExpressionParser(cursor, operators: table).ParseExpression();

        await Assert.That(cursor.Current.Kind).IsEqualTo(DslTokenKind.EndOfFile);
        var comparison = (Comparison)parsed;
        await Assert.That(comparison.Kind).IsEqualTo(ComparisonKind.Equal);
        await Assert.That(((PropertyAccess)comparison.Left).Name).IsEqualTo("a");
        await Assert.That(comparison.Right).IsTypeOf<Poly.DomainModeling.Ontology.Multiply>();
    }

    /// <summary>
    /// F-04 / F-05 hazard, pinned: a binary row between the comparison level and the sum level (here 45) would move the
    /// level at whose head <c>not</c> applies and silently turn <c>not a &gt; b</c> into <c>(not a) &gt; b</c>, so <c>With</c> refuses it.
    /// A row outside that gap (55) leaves <c>not a &gt; b</c> a parse error exactly as in the core table.
    /// </summary>
    [Test]
    public async Task With_RowBetweenNotHeadAndOperand_IsRefused_AndOutsideRowKeepsNotClosedToCompare() {
        var refused = Assert.Throws<InvalidOperationException>(() => DslOperatorTable.Core().With(new BinaryRow(
            DslTokenKind.In, 45, Assoc.Left, DomainExpression.Equal)));
        // The message names the refused precedence band's row (precedence 45, the 41 to 49 gap) and why: it would change how `not` binds.
        await Assert.That(refused.Message).Contains("precedence 45");
        await Assert.That(refused.Message).Contains("would change how prefix 'Not' binds");

        var table = DslOperatorTable.Core().With(new BinaryRow(DslTokenKind.In, 55, Assoc.Left, DomainExpression.Equal));
        var reader = new DslTokenReader("not a > b");
        var cursor = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(DslGrammar.Core, reader));
        var parsed = new DslExpressionParser(cursor, operators: table).ParseExpression();

        await Assert.That(parsed).IsTypeOf<Poly.DomainModeling.Ontology.Not>();
        await Assert.That(cursor.Current.Kind).IsEqualTo(DslTokenKind.Gt);
    }

    private static ExpressionFormRegistry TemporalClaims() {
        var forms = new ExpressionFormRegistry();
        forms.RegisterBinaryClaim(DslTokenKind.Plus, DateOperationFold.Plus);
        forms.RegisterBinaryClaim(DslTokenKind.Minus, DateOperationFold.Minus);
        return forms;
    }

    private static readonly Duration TwoHours = new(2, DurationUnit.Hours);

    /// <summary>G-20: a library claims <c>+</c>/<c>-</c> as a claim on the operator table's own row, and the row fold returns the claim.</summary>
    [Test]
    public async Task TemporalClaim_IsARowOfTheOperatorTable() {
        var table = DslOperatorTable.Core(TemporalClaims());

        var claimed = table.Claim(DslTokenKind.Plus, new Now(), TwoHours);
        await Assert.That(claimed).IsTypeOf<DateOperation>();
        await Assert.That(((DateOperation)claimed!).Kind).IsEqualTo(DateOperationKind.AddHours);
        await Assert.That(((Literal)((DateOperation)claimed).Offset).Value).IsEqualTo(2L);

        var viaRow = Binary(table, DslTokenKind.Plus).Fold(new Now(), TwoHours);
        await Assert.That(viaRow).IsEqualTo(claimed);
        var minus = (DateOperation)Binary(table, DslTokenKind.Minus).Fold(new Now(), TwoHours);
        await Assert.That(((Literal)minus.Offset).Value).IsEqualTo(-2L);
        await Assert.That(table.Fold(DslTokenKind.Plus, new Now(), TwoHours)).IsEqualTo(claimed);
    }

    /// <summary>An unclaimed pair is the row's own fallback, in one place: <c>Claim</c> is null and the row folds to the plain node.</summary>
    [Test]
    public async Task UnclaimedPair_ClaimIsNull_AndTheRowFoldsToPlainAdd() {
        var table = DslOperatorTable.Core(TemporalClaims());
        var age = DomainExpression.Property("Age");
        var one = DomainExpression.Literal(1L, new DomainTypeReference("Number"));

        await Assert.That(table.Claim(DslTokenKind.Plus, age, one)).IsNull();
        await Assert.That(Binary(table, DslTokenKind.Plus).Fold(age, one)).IsEqualTo(DomainExpression.Add(age, one));
        await Assert.That(table.Fold(DslTokenKind.Minus, age, one)).IsEqualTo(DomainExpression.Subtract(age, one));
    }

    [Test]
    public async Task Core_WithNoClaims_PlusIsPlainAdd() {
        var table = DslOperatorTable.Core();

        await Assert.That(table.Claim(DslTokenKind.Plus, new Now(), TwoHours)).IsNull();
        await Assert.That(Binary(table, DslTokenKind.Plus).Fold(new Now(), TwoHours))
            .IsEqualTo(DomainExpression.Add(new Now(), TwoHours));
    }

    /// <summary>Claims survive <c>With</c> (F-04 and F-05 add rows through it): the copy still claims and its <c>+</c> row still folds the claim.</summary>
    [Test]
    public async Task CoreWithRow_StillClaims() {
        var table = DslOperatorTable.Core(TemporalClaims())
            .With(new BinaryRow(DslTokenKind.In, 55, Assoc.Left, DomainExpression.Equal))
            .With(new PrefixRow(DslTokenKind.Minus, 50, DomainExpression.Not));

        await Assert.That(table.Claim(DslTokenKind.Plus, new Now(), TwoHours)).IsTypeOf<DateOperation>();
        await Assert.That(Binary(table, DslTokenKind.Plus).Fold(new Now(), TwoHours)).IsTypeOf<DateOperation>();
    }

    /// <summary>A claim may sit on any binary row of the core table, not just <c>+</c>/<c>-</c>: <c>Core</c> wraps every row.</summary>
    [Test]
    public async Task ClaimOnAnotherRow_WrapsThatRow() {
        var forms = new ExpressionFormRegistry();
        forms.RegisterBinaryClaim(DslTokenKind.Star, (l, r) => l is Duration ? DomainExpression.Literal(0L) : null);
        var table = DslOperatorTable.Core(forms);

        await Assert.That(Binary(table, DslTokenKind.Star).Fold(TwoHours, new Now())).IsTypeOf<Literal>();
        await Assert.That(Binary(table, DslTokenKind.Star).Fold(new Now(), TwoHours)).IsTypeOf<Poly.DomainModeling.Ontology.Multiply>();
    }

    [Test]
    public async Task Claim_ForOpWithoutRow_Throws() {
        var table = DslOperatorTable.Core(TemporalClaims());

        var ex = Assert.Throws<InvalidOperationException>(() => table.Claim(DslTokenKind.In, new Now(), TwoHours));
        await Assert.That(ex.Message).Contains("In");
    }

    [Test]
    public async Task RegisterBinaryClaim_ForOpWithoutRow_Throws() {
        var forms = new ExpressionFormRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            forms.RegisterBinaryClaim(DslTokenKind.In, (l, r) => null));
        await Assert.That(ex.Message).Contains("In");
    }

    /// <summary>
    /// G-20: the binary-fold bridge is gone; a library claims <c>+</c>/<c>-</c> through the operator table.
    /// Reflection by full name, so this compiles whether or not the symbols exist; the guard names the replacements.
    /// </summary>
    [Test]
    public async Task BinaryFoldBridge_IsRetired() {
        var poly = typeof(DslOperatorTable).Assembly;
        var registry = poly.GetType("Poly.DomainModeling.Meaning.ExpressionFormRegistry")!;

        await Assert.That(poly.GetType("Poly.DomainModeling.Meaning.IBinaryExpressionFold")).IsNull();
        await Assert.That(registry.GetMethod("RegisterBinaryFold")).IsNull();
        await Assert.That(registry.GetMethod("TryFoldBinary")).IsNull();

        await Assert.That(registry.GetMethod("RegisterBinaryClaim")).IsNotNull();
        await Assert.That(poly.GetType("Poly.DomainModeling.Libraries.Temporal.DateOperationFold")).IsNotNull();
    }

    /// <summary>The same token with no row is not an operator: the default table does not know <c>in</c>.</summary>
    [Test]
    public async Task Core_DoesNotKnowAnUnregisteredOperator() {
        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment("a in b")).Throws<FormatException>();
    }
}