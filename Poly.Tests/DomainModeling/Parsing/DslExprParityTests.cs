using Poly.DomainModeling;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;
// Disambiguate from Poly.Ast.Nodes records imported via global usings.
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.Tests.DomainModeling.Parsing;

// ─── Expression regression corpus ───
// Same frozen canonical IR oracles:
// DslExpressionFragment (standalone expr) + DslGrammar span checks.
// Never delete cases; every case parses to the exact frozen IR or rejects loud.

public sealed class DslExprParityTests {
    private static DomainExpression V2Parse(string expr) =>
        DslExpressionFragment.ParseExpressionFragment(expr);

    private static async Task AssertCanonical(string expr, string expected) {
        await Assert.That(Canonical(V2Parse(expr))).IsEqualTo(expected);
    }

    private static async Task AssertRejects(string expr) {
        await Assert.That(() => V2Parse(expr)).Throws<FormatException>();
    }

    /// <summary>Normalized structural text for a DomainExpression (Id-agnostic).</summary>
    private static string Canonical(DomainExpression e) => e switch {
        PropertyAccess p => $"Prop({p.Name})",
        ParameterAccess p => $"Param({p.Name})",
        Literal l => $"Lit({l.Value ?? "null"})",
        OwnedAccess o => $"Owned({o.OwnedName},{Canonical(o.Inner)})",
        Exists x => $"Exists({Canonical(x.Target)})",
        NotExists x => $"NotExists({Canonical(x.Target)})",
        Add a => $"Add({Canonical(a.Left)},{Canonical(a.Right)})",
        Subtract s => $"Sub({Canonical(s.Left)},{Canonical(s.Right)})",
        Multiply m => $"Mul({Canonical(m.Left)},{Canonical(m.Right)})",
        Divide d => $"Div({Canonical(d.Left)},{Canonical(d.Right)})",
        DateOperation d => $"DateOp({Canonical(d.Date)},{Canonical(d.Offset)},{d.Kind})",
        RelationshipNavigation n => $"Nav({n.RelationshipName},{Canonical(n.TargetProperty)})",
        AnyExpr a => $"Any({a.RelationshipName},{Canonical(a.Body)})",
        AllExpr a => $"All({a.RelationshipName},{Canonical(a.Body)})",
        NoneExpr n => $"None({n.RelationshipName},{Canonical(n.Body)})",
        CountExpr c => $"Count({c.RelationshipName},{(c.Body is null ? "-" : Canonical(c.Body))})",
        Comparison c => $"Cmp({Canonical(c.Left)},{c.Kind},{Canonical(c.Right)})",
        And a => $"And({Canonical(a.Left)},{Canonical(a.Right)})",
        Or o => $"Or({Canonical(o.Left)},{Canonical(o.Right)})",
        Not n => $"Not({Canonical(n.Operand)})",
        _ => throw new InvalidOperationException(
            $"Unmapped DomainExpression subtype '{e.GetType().Name}' in canonical oracle"),
    };

    // Span side: TryMatch("expr") accepts only when the whole stream is consumed.
    private static (bool Accepts, int Consumed) GrammarMatch(string expr) {
        var reader = new DslTokenReader(expr);
        var matcher = new Matcher<DslToken, DslTokenKind>(DslGrammar.Build(), reader);
        var result = matcher.TryMatch("expr");
        if (result is null) return (false, 0);
        var total = 0;
        while (true) {
            total++;
            if (reader.Peek(total - 1).Kind == DslTokenKind.EndOfFile) break;
        }
        return (result.Consumed == total - 1, result.Consumed);
    }

    // Product side: full .poly parse whose policy body is the expression.
    private static bool ProductAccepts(string expr) {
        var poly = $"domain D\nE: entity {{ P: policy {{ {expr} }} }}";
        try {
            new PolyDslParser(poly).Parse();
            return true;
        }
        catch (FormatException) {
            return false;
        }
    }

    [Test]
    public async Task Parity_AddMul_Precedence_AcceptBoth() {
        await Assert.That(ProductAccepts("1 + 2 * 3")).IsTrue();
        var g = GrammarMatch("1 + 2 * 3");
        await Assert.That(g.Accepts).IsTrue();
        await Assert.That(g.Consumed).IsEqualTo(5);
    }

    [Test]
    public async Task Parity_AndOr_Consumes_AcceptBoth() {
        await Assert.That(ProductAccepts("a and b or c")).IsTrue();
        var g = GrammarMatch("a and b or c");
        await Assert.That(g.Accepts).IsTrue();
        await Assert.That(g.Consumed).IsEqualTo(5);
    }

    [Test]
    public async Task Parity_Compare_AcceptBoth() {
        await Assert.That(ProductAccepts("Age >= 18")).IsTrue();
        var g = GrammarMatch("Age >= 18");
        await Assert.That(g.Accepts).IsTrue();
        await Assert.That(g.Consumed).IsEqualTo(3);
    }

    [Test]
    public async Task Parity_Not_AcceptBoth() {
        await Assert.That(ProductAccepts("not x")).IsTrue();
        var g = GrammarMatch("not x");
        await Assert.That(g.Accepts).IsTrue();
        await Assert.That(g.Consumed).IsEqualTo(2);
    }

    [Test]
    public async Task Parity_NotOverCompare_RejectsBoth() {
        await Assert.That(ProductAccepts("not a > b")).IsFalse();
        await Assert.That(GrammarMatch("not a > b").Accepts).IsFalse();
    }

    [Test]
    public async Task Parity_TrailingOp_RejectsBoth() {
        await Assert.That(ProductAccepts("1 +")).IsFalse();
        await Assert.That(GrammarMatch("1 +").Accepts).IsFalse();
    }

    [Test]
    public async Task Parity_Group_AcceptBoth() {
        await Assert.That(ProductAccepts("(1 + 2)")).IsTrue();
        var g = GrammarMatch("(1 + 2)");
        await Assert.That(g.Accepts).IsTrue();
        await Assert.That(g.Consumed).IsEqualTo(5);
    }

    [Test]
    public async Task Parity_NestedGroup_InComparison_AcceptBoth() {
        await Assert.That(ProductAccepts("(Age + 1) >= 18")).IsTrue();
        var g = GrammarMatch("(Age + 1) >= 18");
        await Assert.That(g.Accepts).IsTrue();
        await Assert.That(g.Consumed).IsEqualTo(7);
    }

    [Test]
    public async Task Parity_NotNot_AcceptBoth() {
        await Assert.That(ProductAccepts("not not x")).IsTrue();
        var g = GrammarMatch("not not x");
        await Assert.That(g.Accepts).IsTrue();
        await Assert.That(g.Consumed).IsEqualTo(3);
    }

    [Test]
    public async Task Parity_UnclosedGroup_RejectsBoth() {
        await Assert.That(ProductAccepts("(1 + 2")).IsFalse();
        await Assert.That(GrammarMatch("(1 + 2").Accepts).IsFalse();
    }

    // ─── Frozen-IR regression corpus ─────────────────────────────

    [Test]
    public async Task Ir_Arithmetic() {
        await AssertCanonical("1 + 2 * 3", "Add(Lit(1),Mul(Lit(2),Lit(3)))");
        await AssertCanonical("(1 + 2) * 3", "Mul(Add(Lit(1),Lit(2)),Lit(3))");
        await AssertCanonical("1 + 2 - 3", "Sub(Add(Lit(1),Lit(2)),Lit(3))");
        await AssertCanonical("x * y / z", "Div(Mul(Prop(x),Prop(y)),Prop(z))");
        await AssertCanonical("a + not b", "Add(Prop(a),Not(Prop(b)))");
    }

    [Test]
    public async Task SpanVsFold_NotInChain_TableRejectsFoldAccepts() {
        await Assert.That(GrammarMatch("a + not b").Accepts).IsFalse();
        await Assert.That(GrammarMatch("a + not b > c").Accepts).IsFalse();
        await AssertCanonical("a + not b", "Add(Prop(a),Not(Prop(b)))");
        await AssertCanonical("a + not b > c", "Cmp(Add(Prop(a),Not(Prop(b))),GreaterThan,Prop(c))");
    }

    [Test]
    public async Task Ir_AndOr() {
        await AssertCanonical("a and b or c", "Or(And(Prop(a),Prop(b)),Prop(c))");
        await AssertCanonical("a and b", "And(Prop(a),Prop(b))");
        await AssertCanonical("a or b", "Or(Prop(a),Prop(b))");
        await AssertCanonical("not (a and b)", "Not(And(Prop(a),Prop(b)))");
    }

    [Test]
    public async Task Ir_Compare() {
        await AssertCanonical("Age >= 18", "Cmp(Prop(Age),GreaterThanOrEqual,Lit(18))");
        await AssertCanonical("Age == 18", "Cmp(Prop(Age),Equal,Lit(18))");
        await AssertCanonical("Age != 18", "Cmp(Prop(Age),NotEqual,Lit(18))");
        await AssertCanonical("Age < 18", "Cmp(Prop(Age),LessThan,Lit(18))");
        await AssertCanonical("Age <= 18", "Cmp(Prop(Age),LessThanOrEqual,Lit(18))");
        await AssertCanonical("Age is 18", "Cmp(Prop(Age),Equal,Lit(18))");
        await AssertCanonical("Age is not 18", "Cmp(Prop(Age),NotEqual,Lit(18))");
    }

    [Test]
    public async Task Ir_Not() {
        await AssertCanonical("not x", "Not(Prop(x))");
        await AssertCanonical("not a + b", "Not(Add(Prop(a),Prop(b)))");
        await AssertCanonical("not (a > b)", "Not(Cmp(Prop(a),GreaterThan,Prop(b)))");
        await AssertCanonical("not not x", "Not(Not(Prop(x)))");
    }

    [Test]
    public async Task Ir_Group() {
        await AssertCanonical("(1 + 2)", "Add(Lit(1),Lit(2))");
        await AssertCanonical("(Age + 1) >= 18", "Cmp(Add(Prop(Age),Lit(1)),GreaterThanOrEqual,Lit(18))");
    }

    [Test]
    public async Task Ir_PathPrefixAndExists() {
        await AssertCanonical("loan book Title is \"Classic\"",
            "Nav(loan,Nav(book,Cmp(Prop(Title),Equal,Lit(Classic))))");
        await AssertCanonical("orders exists", "Exists(Prop(orders))");
        await AssertCanonical("patron loan Code is \"L1\"",
            "Nav(patron,Nav(loan,Cmp(Prop(Code),Equal,Lit(L1))))");
    }

    [Test]
    public async Task Ir_Quantifiers() {
        await AssertCanonical("any Line where Total > 0", "Any(Line,Cmp(Prop(Total),GreaterThan,Lit(0)))");
        await AssertCanonical("all Line where Total > 0", "All(Line,Cmp(Prop(Total),GreaterThan,Lit(0)))");
        await AssertCanonical("count Line where Total > 0", "Count(Line,Cmp(Prop(Total),GreaterThan,Lit(0)))");
    }

    [Test]
    public async Task Ir_FailClosed_Negatives() {
        await AssertRejects("1 +");
        await AssertRejects("(1 + 2");
        await AssertRejects("1 + + 2");
        await Assert.That(ProductAccepts("not a > b")).IsFalse();
        await Assert.That(ProductAccepts("a > b > c")).IsFalse();
    }
}