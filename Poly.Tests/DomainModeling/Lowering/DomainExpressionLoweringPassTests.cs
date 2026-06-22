using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;

using Parameter = Poly.Syntax.Nodes.Parameter;
using SN = Poly.Syntax.Nodes;

namespace Poly.Tests.DomainModeling.Lowering;

public class DomainExpressionLoweringPassTests {
    private static readonly DomainExpressionLoweringPass Pass = new();

    private static readonly SN.ParameterReference Subject = new();

    [Test]
    public async Task PropertyAccess_LowersToMember() {
        var expr = DomainExpression.Property("Name");
        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<SN.Member>();
        var member = (SN.Member)result;
        await Assert.That(member.MemberName).IsEqualTo("Name");
        await Assert.That(member.Value).IsSameReferenceAs(Subject);
    }

    [Test]
    public async Task ParameterAccess_LowersToParameter_WhenFoundInDictionary() {
        var param = new Parameter("p1");
        var pass = new DomainExpressionLoweringPass(new Dictionary<string, Node> { ["p1"] = param });

        var expr = DomainExpression.Parameter("p1");
        var result = pass.Lower(expr, Subject);

        await Assert.That(result).IsSameReferenceAs(param);
    }

    [Test]
    public async Task ParameterAccess_CreatesFreshParameter_WhenNotInDictionary() {
        var expr = DomainExpression.Parameter("fresh");
        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Parameter>();
        var param = (Parameter)result;
        await Assert.That(param.Name).IsEqualTo("fresh");
    }

    [Test]
    public async Task Literal_LowersToConstant() {
        var expr = DomainExpression.Literal(42);
        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<SN.Constant>();
        await Assert.That(((SN.Constant)result).Value).IsEqualTo(42);
    }

    [Test]
    public async Task Literal_Null_LowersToConstantNull() {
        var expr = DomainExpression.Literal(null);
        var result = Pass.Lower(expr, Subject);

        await Assert.That(((SN.Constant)result).Value).IsNull();
    }

    [Test]
    public async Task OwnedAccess_LowersToNestedMember() {
        var inner = DomainExpression.Property("Time");
        var expr = DomainExpression.Owned("BirthCert", inner);

        var result = Pass.Lower(expr, Subject);

        // Expected: Member(Member(subject, "BirthCert"), "Time")
        await Assert.That(result).IsTypeOf<SN.Member>();
        var outer = (SN.Member)result;
        await Assert.That(outer.MemberName).IsEqualTo("Time");

        await Assert.That(outer.Value).IsTypeOf<SN.Member>();
        var innerMember = (SN.Member)outer.Value;
        await Assert.That(innerMember.MemberName).IsEqualTo("BirthCert");
        await Assert.That(innerMember.Value).IsSameReferenceAs(Subject);
    }

    [Test]
    public async Task RelationshipNavigation_LowersToNestedMember() {
        var inner = DomainExpression.Property("AvailableCopies");
        var expr = DomainExpression.RelationshipNav("Book", inner);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<SN.Member>();
        var outer = (SN.Member)result;
        await Assert.That(outer.MemberName).IsEqualTo("AvailableCopies");

        await Assert.That(outer.Value).IsTypeOf<SN.Member>();
        var nav = (SN.Member)outer.Value;
        await Assert.That(nav.MemberName).IsEqualTo("Book");
        await Assert.That(nav.Value).IsSameReferenceAs(Subject);
    }

    [Test]
    public async Task Exists_LowersToNotEqualNull() {
        var inner = DomainExpression.Property("Name");
        var expr = DomainExpression.Exists(inner);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<SN.NotEqual>();
        var neq = (SN.NotEqual)result;

        await Assert.That(neq.LeftHandValue).IsTypeOf<SN.Member>();
        await Assert.That(((SN.Member)neq.LeftHandValue).MemberName).IsEqualTo("Name");
        await Assert.That(neq.RightHandValue).IsTypeOf<SN.Constant>();
        await Assert.That(((SN.Constant)neq.RightHandValue).Value).IsNull();
    }

    [Test]
    public async Task NotExists_LowersToEqualNull() {
        var expr = DomainExpression.NotExists(DomainExpression.Property("Name"));

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<SN.Equal>();
        var eq = (SN.Equal)result;
        await Assert.That(eq.RightHandValue).IsTypeOf<SN.Constant>();
        await Assert.That(((SN.Constant)eq.RightHandValue).Value).IsNull();
    }

    [Test]
    public async Task Add_LowersToSyntaxAdd() {
        var result = Pass.Lower(
            DomainExpression.Add(DomainExpression.Literal(1), DomainExpression.Literal(2)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Add>();
        var add = (SN.Add)result;
        await Assert.That(((SN.Constant)add.LeftHandValue).Value).IsEqualTo(1);
        await Assert.That(((SN.Constant)add.RightHandValue).Value).IsEqualTo(2);
    }

    [Test]
    public async Task Subtract_LowersToSyntaxSubtract() {
        var result = Pass.Lower(
            DomainExpression.Subtract(DomainExpression.Literal(5), DomainExpression.Literal(3)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Subtract>();
    }

    [Test]
    public async Task Multiply_LowersToSyntaxMultiply() {
        var result = Pass.Lower(
            DomainExpression.Multiply(DomainExpression.Literal(2), DomainExpression.Literal(3)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Multiply>();
    }

    [Test]
    public async Task Divide_LowersToSyntaxDivide() {
        var result = Pass.Lower(
            DomainExpression.Divide(DomainExpression.Literal(10), DomainExpression.Literal(2)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Divide>();
    }

    [Test]
    public async Task And_LowersToSyntaxAnd() {
        var result = Pass.Lower(
            DomainExpression.And(
                DomainExpression.Exists(DomainExpression.Property("A")),
                DomainExpression.Exists(DomainExpression.Property("B"))),
            Subject);

        await Assert.That(result).IsTypeOf<SN.And>();
    }

    [Test]
    public async Task Or_LowersToSyntaxOr() {
        var result = Pass.Lower(
            DomainExpression.Or(
                DomainExpression.Exists(DomainExpression.Property("X")),
                DomainExpression.Exists(DomainExpression.Property("Y"))),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Or>();
    }

    [Test]
    public async Task Not_LowersToSyntaxNot() {
        var result = Pass.Lower(
            DomainExpression.Not(DomainExpression.Exists(DomainExpression.Property("Flag"))),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Not>();
    }

    [Test]
    public async Task Comparison_Equal_LowersToSyntaxEqual() {
        var result = Pass.Lower(
            DomainExpression.Equal(DomainExpression.Property("Count"), DomainExpression.Literal(5)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Equal>();
    }

    [Test]
    public async Task Comparison_NotEqual_LowersToSyntaxNotEqual() {
        var result = Pass.Lower(
            DomainExpression.NotEqual(DomainExpression.Property("Status"), DomainExpression.Literal(0)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.NotEqual>();
    }

    [Test]
    public async Task Comparison_LessThan_LowersToSyntaxLessThan() {
        var result = Pass.Lower(
            DomainExpression.LessThan(DomainExpression.Property("Qty"), DomainExpression.Property("Min")),
            Subject);

        await Assert.That(result).IsTypeOf<SN.LessThan>();
    }

    [Test]
    public async Task Comparison_LessThanOrEqual_LowersToSyntaxLessThanOrEqual() {
        var result = Pass.Lower(
            DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Property("Min")),
            Subject);

        await Assert.That(result).IsTypeOf<SN.LessThanOrEqual>();
    }

    [Test]
    public async Task Comparison_GreaterThan_LowersToSyntaxGreaterThan() {
        var result = Pass.Lower(
            DomainExpression.GreaterThan(DomainExpression.Property("Qty"), DomainExpression.Property("Max")),
            Subject);

        await Assert.That(result).IsTypeOf<SN.GreaterThan>();
    }

    [Test]
    public async Task Comparison_GreaterThanOrEqual_LowersToSyntaxGreaterThanOrEqual() {
        var result = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Property("Max")),
            Subject);

        await Assert.That(result).IsTypeOf<SN.GreaterThanOrEqual>();
    }

    [Test]
    public async Task DateOperation_AddDays_LowersToInvoke() {
        var expr = DomainExpression.DateOp(
            DomainExpression.Property("DueDate"),
            DomainExpression.Literal(14),
            DateOperationKind.AddDays);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<SN.Invoke>();
        var invoke = (SN.Invoke)result;
        await Assert.That(invoke.Delegate).IsTypeOf<SN.Member>();
        await Assert.That(((SN.Member)invoke.Delegate).MemberName).IsEqualTo("AddDays");
        await Assert.That(invoke.Arguments.Length).IsEqualTo(1);
    }

    [Test]
    public async Task DateOperation_AddMonths_LowersToInvoke() {
        var expr = DomainExpression.DateOp(
            DomainExpression.Property("StartDate"),
            DomainExpression.Literal(3),
            DateOperationKind.AddMonths);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<SN.Invoke>();
        await Assert.That(((SN.Member)((SN.Invoke)result).Delegate).MemberName).IsEqualTo("AddMonths");
    }

    [Test]
    public async Task DateOperation_DiffDays_LowersToInvoke() {
        var expr = DomainExpression.DateOp(
            DomainExpression.Property("EndDate"),
            DomainExpression.Property("StartDate"),
            DateOperationKind.DiffDays);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<SN.Invoke>();
        await Assert.That(((SN.Member)((SN.Invoke)result).Delegate).MemberName).IsEqualTo("Subtract");
    }

    [Test]
    public async Task ComplexExpression_CombinesMultipleNodeTypes() {
        // Lower: And(Exists(Owned("Cert", Property("Time"))), GreaterThan(Property("Age"), Literal(18)))
        var expr = DomainExpression.And(
            DomainExpression.Exists(
                DomainExpression.Owned("Cert", DomainExpression.Property("Time"))),
            DomainExpression.GreaterThan(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        var result = Pass.Lower(expr, Subject);

        // Top-level: And
        await Assert.That(result).IsTypeOf<SN.And>();
        var and = (SN.And)result;

        // Left: NotEqual(Member(Member(subject, "Cert"), "Time"), null)
        await Assert.That(and.LeftHandValue).IsTypeOf<SN.NotEqual>();
        var neq = (SN.NotEqual)and.LeftHandValue;
        await Assert.That(neq.RightHandValue).IsTypeOf<SN.Constant>();
        await Assert.That(((SN.Constant)neq.RightHandValue).Value).IsNull();
        await Assert.That(neq.LeftHandValue).IsTypeOf<SN.Member>();
        await Assert.That(((SN.Member)neq.LeftHandValue).MemberName).IsEqualTo("Time");

        // Right: GreaterThan(Member(subject, "Age"), Constant(18))
        await Assert.That(and.RightHandValue).IsTypeOf<SN.GreaterThan>();
        var gt = (SN.GreaterThan)and.RightHandValue;
        await Assert.That(gt.LeftHandValue).IsTypeOf<SN.Member>();
        await Assert.That(((SN.Member)gt.LeftHandValue).MemberName).IsEqualTo("Age");
        await Assert.That(gt.RightHandValue).IsTypeOf<SN.Constant>();
        await Assert.That(((SN.Constant)gt.RightHandValue).Value).IsEqualTo(18);
    }

    [Test]
    public async Task ParameterAccess_ResolvesInNestedContext() {
        var param = new Parameter("status");
        var pass = new DomainExpressionLoweringPass(
            new Dictionary<string, Node> { ["status"] = param });

        // Owned("Details", Parameter("status"))
        var expr = DomainExpression.Owned("Details", DomainExpression.Parameter("status"));
        var result = pass.Lower(expr, Subject);

        // Expected: Member(subject, "Details") — but inner is parameter, not property
        // ParameterAccess resolves directly to the parameter node, bypassing the owned context
        await Assert.That(result).IsSameReferenceAs(param);
    }

    [Test]
    public async Task Guard_ThrowsOnNullExpression() {
        var pass = new DomainExpressionLoweringPass();
        var ex = Assert.Throws<ArgumentNullException>(() => pass.Lower(null!, Subject));
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Guard_ThrowsOnNullSubject() {
        var pass = new DomainExpressionLoweringPass();
        var ex = Assert.Throws<ArgumentNullException>(() => pass.Lower(DomainExpression.Literal(1), null!));
        await Assert.That(ex).IsNotNull();
    }
}