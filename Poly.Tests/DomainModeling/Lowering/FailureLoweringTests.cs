using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;
using Poly.Runtime;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// The failure-construction seam: <see cref="FailureLowering"/> is the only AST-facing
/// constructor of a typed <c>Poly.Runtime.DomainFailure</c>. Each test prints the node
/// it builds, so the factory call and the omitted-trailing-null rule are pinned.
/// </summary>
public class FailureLoweringTests {
    private static string Print(Node node) => new CSharpGenerator().Generate(node);

    [Test]
    public async Task Return_EmitsTheDomainFailureFactory_NotAStringLiteral() {
        var node = FailureLowering.Return(
            new NamedTypeReference("DomainResult"),
            new FailureLowering.Spec(DomainFailureKind.Range, new Constant("'Qty' must be >= 1."), "Reservation.Qty"));

        var text = Print(node);

        await Assert.That(text).Contains(
            "DomainResult.Failure(Poly.Runtime.DomainFailure.Range(\"'Qty' must be >= 1.\", \"Reservation.Qty\"))");
    }

    [Test]
    public async Task Call_WithoutPathOrGuard_OmitsTrailingNulls() {
        var call = FailureLowering.Call(
            new FailureLowering.Spec(DomainFailureKind.Constraint, new Constant("m")));

        await Assert.That(Print(call)).Contains("Poly.Runtime.DomainFailure.Constraint(\"m\")");
        await Assert.That(((Invoke)call).Arguments.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Call_WithGuardButNoPath_KeepsThePathSlotAsNull() {
        var call = FailureLowering.Call(
            new FailureLowering.Spec(DomainFailureKind.Policy, new Constant("m"), Guard: "IsActive"));

        await Assert.That(((Invoke)call).Arguments.Length).IsEqualTo(3);
        await Assert.That(Print(call)).Contains("Poly.Runtime.DomainFailure.Policy(\"m\", null, \"IsActive\")");
    }

    [Test]
    public async Task Propagate_PrintsErrorWithNullForgivingAndAt() {
        var node = FailureLowering.Propagate(
            new NamedTypeReference("DomainResult"), new Variable("result1"), "Product.Hold");

        await Assert.That(Print(node)).Contains("DomainResult.Failure(result1.Error!.At(\"Product.Hold\"))");
    }

    [Test]
    public async Task Propagate_WithoutCallerPath_KeepsKindMessageAndGuardByPassingTheErrorThrough() {
        var node = FailureLowering.Propagate(new NamedTypeReference("DomainResult"), new Variable("result1"));

        await Assert.That(Print(node)).Contains("DomainResult.Failure(result1.Error!)");
    }

    [Test]
    public async Task Throw_PrintsConstraintFailureExceptionWithTheMessage() {
        var node = FailureLowering.Throw(
            new FailureLowering.Spec(DomainFailureKind.Required, new Constant("'Name' is required."), "Widget.Name"));

        await Assert.That(Print(node)).Contains("throw new ConstraintFailureException(\"'Name' is required.\")");
    }

    [Test]
    public async Task ThrowFrom_PrintsTheCalleeErrorMessageWithACoalesce() {
        var node = FailureLowering.ThrowFrom(new Variable("result1"));

        await Assert.That(Print(node)).Contains("throw new ConstraintFailureException(result1.ErrorMessage ?? \"\")");
    }
}