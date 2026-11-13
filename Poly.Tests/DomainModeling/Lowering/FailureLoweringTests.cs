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

    // L-04a replaced the two tests of FailureLowering.Throw and FailureLowering.ThrowFrom (a failure thrown from a void body)
    // with this one: no lowered body is void any more, so the seam has no throwing form, and a failure is only ever built
    // as a returned value. The members left are the three that build one.
    [Test]
    public async Task FailureLowering_HasNoThrowingForm() {
        var members = typeof(FailureLowering)
            .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(string.Join(",", members)).IsEqualTo("Call,Propagate,Return");
    }
}