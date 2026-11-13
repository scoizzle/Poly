using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-24: <see cref="DslOperatorTable"/> is the one precedence source. The pins hold the driver's verdicts and
/// trees for the strings the retired pattern chain used to recognise, and the shape tests show that the
/// table, not the grammar, decides how operators nest.
/// </summary>
public sealed class DslPrecedenceSourceTests {
    // The strings DslExprParityTests gave the pattern chain. The verdict is derived from the product parse
    // of the same string; only the accepted tree is a frozen oracle.
    private static readonly (string Expr, string? Tree)[] Corpus = [
        ("1 + 2 * 3", "Add(Lit(1),Mul(Lit(2),Lit(3)))"),
        ("a and b or c", "Or(And(Prop(a),Prop(b)),Prop(c))"),
        ("Age >= 18", "Cmp(Prop(Age),GreaterThanOrEqual,Lit(18))"),
        ("not x", "Not(Prop(x))"),
        ("not a > b", null),
        ("1 +", null),
        ("(1 + 2)", "Add(Lit(1),Lit(2))"),
        ("(Age + 1) >= 18", "Cmp(Add(Prop(Age),Lit(1)),GreaterThanOrEqual,Lit(18))"),
        ("not not x", "Not(Not(Prop(x)))"),
        ("(1 + 2", null),
        ("a + not b", "Add(Prop(a),Not(Prop(b)))"),
        ("a + not b > c", "Cmp(Add(Prop(a),Not(Prop(b))),GreaterThan,Prop(c))"),
    ];

    [Test]
    public async Task Driver_Verdict_FollowsProductParse_AndTreeIsFrozen() {
        foreach (var (expr, tree) in Corpus) {
            var productAccepts = DslExprParityTests.ProductAccepts(expr);

            DomainExpression? parsed = null;
            try {
                parsed = DslExpressionFragment.ParseExpressionFragment(expr);
            }
            catch (FormatException) {
            }

            await Assert.That(parsed is not null).IsEqualTo(productAccepts);
            await Assert.That(tree is not null).IsEqualTo(productAccepts);
            if (parsed is not null)
                await Assert.That(DslExprParityTests.Canonical(parsed)).IsEqualTo(tree);
        }
    }

    /// <summary>
    /// The pattern grammar defines no operator precedence: its one expression rule is the flat atom
    /// <c>expr-primary</c>, and the table decides how atoms nest.
    /// </summary>
    [Test]
    public async Task GrammarDefinesOneExpressionRule_ExprPrimaryOnly() {
        var grammar = DslGrammar.Core;
        await Assert.That(grammar.KnownRules.Contains("expr-primary")).IsTrue();
        await Assert.That(grammar.GetPatterns("expr-primary").Any(p => p.Name == "ident")).IsTrue();

        var names = grammar.KnownRules
            .Where(n => n == "expr" || n.StartsWith("expr-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var extras = names.Where(n => n != "expr-primary").ToArray();

        await Assert.That(extras.Length).IsEqualTo(0).Because($"extra expression rules: {string.Join(", ", extras)}");
        await Assert.That(names).IsEquivalentTo(["expr-primary"]);
    }

    private static DomainExpression ParseWith(string text, DslOperatorTable table) {
        var reader = new DslTokenReader(text);
        var cursor = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(DslGrammar.Core, reader));
        var parsed = new DslExpressionParser(cursor, operators: table).ParseExpression();
        if (cursor.Current.Kind != DslTokenKind.EndOfFile)
            throw new InvalidOperationException($"'{text}' left tokens after the expression.");
        return parsed;
    }

    private static string Spelling(DslTokenKind op) => op switch {
        DslTokenKind.Or => "or",
        DslTokenKind.And => "and",
        DslTokenKind.Is => "is",
        DslTokenKind.Eq => "==",
        DslTokenKind.Neq => "!=",
        DslTokenKind.Gt => ">",
        DslTokenKind.Gte => ">=",
        DslTokenKind.Lt => "<",
        DslTokenKind.Lte => "<=",
        DslTokenKind.Plus => "+",
        DslTokenKind.Minus => "-",
        DslTokenKind.Star => "*",
        DslTokenKind.Slash => "/",
        DslTokenKind.In => "in",
        _ => throw new InvalidOperationException($"No spelling for operator '{op}' in this test."),
    };

    private static readonly DomainExpression ProbeLeft = DomainExpression.Parameter("l");
    private static readonly DomainExpression ProbeRight = DomainExpression.Parameter("r");

    private static Type NodeOf(BinaryRow row) => row.Fold(ProbeLeft, ProbeRight).GetType();

    private static (DomainExpression Left, DomainExpression Right) Operands(DomainExpression e) => e switch {
        Add n => (n.Left, n.Right),
        Subtract n => (n.Left, n.Right),
        Multiply n => (n.Left, n.Right),
        Divide n => (n.Left, n.Right),
        And n => (n.Left, n.Right),
        Or n => (n.Left, n.Right),
        Comparison n => (n.Left, n.Right),
        _ => throw new InvalidOperationException($"'{e.GetType().Name}' is not a binary node."),
    };

    /// <summary>
    /// For each adjacent pair of levels in the table, the tighter operator is the inner node whichever side it
    /// sits on: <c>x loose y tight z</c> is <c>x loose (y tight z)</c> and <c>x tight y loose z</c> is
    /// <c>(x tight y) loose z</c>. The expectation is computed from the row precedences.
    /// </summary>
    [Test]
    public async Task Driver_TreeShape_FollowsTableLevels() {
        var table = DslOperatorTable.Core();
        var perLevel = table.Levels
            .Select(l => table.BinaryRows
                .Where(r => r.Precedence == l.Precedence && r.Follow is null)
                .OrderBy(r => r.Op)
                .First())
            .ToList();
        await Assert.That(perLevel.Count).IsGreaterThan(2);

        for (var i = 0; i + 1 < perLevel.Count; i++) {
            var loose = perLevel[i];
            var tight = perLevel[i + 1];
            await Assert.That(loose.Precedence).IsLessThan(tight.Precedence);

            var innerRight = ParseWith($"x {Spelling(loose.Op)} y {Spelling(tight.Op)} z", table);
            await Assert.That(innerRight.GetType()).IsEqualTo(NodeOf(loose));
            await Assert.That(Operands(innerRight).Right.GetType()).IsEqualTo(NodeOf(tight));
            await Assert.That(Operands(innerRight).Left).IsTypeOf<PropertyAccess>();

            var innerLeft = ParseWith($"x {Spelling(tight.Op)} y {Spelling(loose.Op)} z", table);
            await Assert.That(innerLeft.GetType()).IsEqualTo(NodeOf(loose));
            await Assert.That(Operands(innerLeft).Left.GetType()).IsEqualTo(NodeOf(tight));
            await Assert.That(Operands(innerLeft).Right).IsTypeOf<PropertyAccess>();
        }
    }

    /// <summary>One row, two precedences, one input: only the row's precedence changes the tree.</summary>
    [Test]
    public async Task Driver_TreeShape_MovesWithOneRow() {
        static BinaryRow In(int precedence) => new(DslTokenKind.In, precedence, Assoc.Left, DomainExpression.Equal);

        var belowMul = ParseWith("a * b in c", DslOperatorTable.Core().With(In(55)));
        await Assert.That(belowMul).IsTypeOf<Comparison>();
        await Assert.That(Operands(belowMul).Left).IsTypeOf<Multiply>();

        var aboveMul = ParseWith("a * b in c", DslOperatorTable.Core().With(In(65)));
        await Assert.That(aboveMul).IsTypeOf<Multiply>();
        await Assert.That(Operands(aboveMul).Right).IsTypeOf<Comparison>();
    }
}