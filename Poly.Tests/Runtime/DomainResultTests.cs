using System.Reflection;

using RuntimeDomainFailure = Poly.Runtime.DomainFailure;
using RuntimeDomainFailureKind = Poly.Runtime.DomainFailureKind;
using RuntimeDomainResult = Poly.Runtime.DomainResult;

namespace Poly.Tests.Runtime;

/// <summary>
/// R-02: the typed failure vocabulary in <c>Poly.Runtime</c>. These types are landed
/// before any consumer binds them (R-03 binds, R-04 lowers), so this suite pins the
/// contract shape and the render/read invariants alone.
/// <para>
/// The file never adds <c>using Poly.Runtime;</c>: the global
/// <c>using Poly.DomainModeling.Runtime;</c> exports the VM <c>DomainResult</c> twin,
/// and the namespace <c>Poly.Tests.Runtime</c> shadows the <c>Runtime</c> segment, so
/// the runtime types are aliased or fully qualified.
/// </para>
/// </summary>
public class DomainResultTests {
    const BindingFlags EveryMember =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.FlattenHierarchy;

    /// <summary>The #677 contract registry order; closed, append-only, serialised by name.</summary>
    static readonly string[] ContractKinds = [
        "Required", "Range", "Length", "Pattern", "Enum", "Unique", "Stage",
        "Policy", "Link", "Arithmetic", "Argument", "Cascade", "Conflict", "Constraint",
    ];

    [Test]
    public async Task Kinds_AreTheContractSetInOrder() {
        await Assert.That(string.Join(",", Enum.GetNames<RuntimeDomainFailureKind>()))
            .IsEqualTo(string.Join(",", ContractKinds));
        await Assert.That(Enum.GetValues<RuntimeDomainFailureKind>().Length).IsEqualTo(14);
    }

    [Test]
    [Arguments("Required")]
    [Arguments("Range")]
    [Arguments("Length")]
    [Arguments("Pattern")]
    [Arguments("Enum")]
    [Arguments("Unique")]
    [Arguments("Stage")]
    [Arguments("Policy")]
    [Arguments("Link")]
    [Arguments("Arithmetic")]
    [Arguments("Argument")]
    [Arguments("Cascade")]
    [Arguments("Conflict")]
    [Arguments("Constraint")]
    public async Task Factory_CarriesKindElementPathAndGuard(string kindName) {
        var factory = typeof(RuntimeDomainFailure)
            .GetMethod(kindName, BindingFlags.Public | BindingFlags.Static);
        await Assert.That(factory).IsNotNull().Because($"DomainFailure.{kindName} is part of the contract");
        await Assert.That(factory!.ReturnType).IsEqualTo(typeof(RuntimeDomainFailure));

        var parameters = factory.GetParameters();
        await Assert.That(parameters.Length).IsEqualTo(3);
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(string));
        await Assert.That(parameters[1].ParameterType).IsEqualTo(typeof(string));
        await Assert.That(parameters[2].ParameterType).IsEqualTo(typeof(string));
        await Assert.That(parameters[1].IsOptional).IsTrue();
        await Assert.That(parameters[2].IsOptional).IsTrue();

        var failure = (RuntimeDomainFailure)factory.Invoke(null,
            ["bad value", "Reservation.Qty", "QtyInRange"])!;
        await Assert.That(failure.Kind.ToString()).IsEqualTo(kindName);
        await Assert.That(failure.Message).IsEqualTo("bad value");
        await Assert.That(failure.ElementPath).IsEqualTo("Reservation.Qty");
        await Assert.That(failure.Guard).IsEqualTo("QtyInRange");
    }

    [Test]
    public async Task Failure_Equality_IsStructural() {
        var left = RuntimeDomainFailure.Range("qty out of range", "Reservation.Qty", "QtyInRange");
        var same = RuntimeDomainFailure.Range("qty out of range", "Reservation.Qty", "QtyInRange");
        var otherPath = RuntimeDomainFailure.Range("qty out of range", "Reservation.Total", "QtyInRange");

        await Assert.That(left).IsEqualTo(same);
        await Assert.That(left.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(left).IsNotEqualTo(otherPath);
    }

    [Test]
    public async Task At_PrefixesElementPath_AndKeepsLeafKind() {
        var leaf = RuntimeDomainFailure.Range("qty out of range", "Reservation.Qty", "QtyInRange");

        var nested = leaf.At("Product.hold").At("Order.submit");

        await Assert.That(nested.Kind).IsEqualTo(RuntimeDomainFailureKind.Range);
        await Assert.That(nested.ElementPath)
            .IsEqualTo("Order.submit.Product.hold.Reservation.Qty");
        await Assert.That(nested.Message).IsEqualTo("qty out of range");
        await Assert.That(nested.Guard).IsEqualTo("QtyInRange");
    }

    [Test]
    public async Task At_OnPathlessFailure_DoesNotLeaveTrailingDot() {
        var fromNull = RuntimeDomainFailure.Required("m").At("Order.submit");
        var fromEmpty = RuntimeDomainFailure.Required("m", "").At("Order.submit");

        await Assert.That(fromNull.ElementPath).IsEqualTo("Order.submit");
        await Assert.That(fromEmpty.ElementPath).IsEqualTo("Order.submit");
    }

    [Test]
    public async Task At_NullOrEmptySegment_Throws() {
        await Assert.That(() => RuntimeDomainFailure.Required("m").At(null!))
            .Throws<ArgumentException>();
        await Assert.That(() => RuntimeDomainFailure.Required("m").At(""))
            .Throws<ArgumentException>();
        await Assert.That(() => RuntimeDomainFailure.Required("m", "Leaf").At(null!))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Generic_Failure_ToString_DoesNotThrow() {
        var failure = Poly.Runtime.DomainResult<string>.Failure(
            RuntimeDomainFailure.Required("missing name", "Item.Name"));

        var text = failure.ToString();

        await Assert.That(text).Contains("missing name");
        await Assert.That(text).DoesNotContain("Value");
    }

    [Test]
    public async Task Generic_Failure_Equality_And_HashCode_DoNotThrow() {
        var left = Poly.Runtime.DomainResult<string>.Failure(RuntimeDomainFailure.Required("missing name"));
        var same = Poly.Runtime.DomainResult<string>.Failure(RuntimeDomainFailure.Required("missing name"));
        var other = Poly.Runtime.DomainResult<string>.Failure(RuntimeDomainFailure.Required("other"));

        await Assert.That(left).IsEqualTo(same);
        await Assert.That(left.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(left).IsNotEqualTo(other);
    }

    [Test]
    public async Task TryGetValue_OnFailure_ReturnsFalse() {
        var failure = Poly.Runtime.DomainResult<string>.Failure(RuntimeDomainFailure.Required("missing name"));

        await Assert.That(failure.TryGetValue(out var value)).IsFalse();
        await Assert.That(value).IsNull();

        var thrown = await Assert.That(() => failure.Value).Throws<InvalidOperationException>();
        await Assert.That(thrown!.Message).IsEqualTo("A failed DomainResult has no Value.");
    }

    [Test]
    public async Task Failure_String_ProducesConstraintKindWithMessage() {
        var voidResult = RuntimeDomainResult.Failure("flat message");
        await Assert.That(voidResult.IsSuccess).IsFalse();
        await Assert.That(voidResult.Error).IsNotNull();
        await Assert.That(voidResult.Error!.Kind).IsEqualTo(RuntimeDomainFailureKind.Constraint);
        await Assert.That(voidResult.Error.Message).IsEqualTo("flat message");
        await Assert.That(voidResult.Error.Render()).IsEqualTo("flat message");
        await Assert.That(voidResult.ErrorMessage).IsEqualTo("flat message");

        var generic = Poly.Runtime.DomainResult<int>.Failure("flat message");
        await Assert.That(generic.IsSuccess).IsFalse();
        await Assert.That(generic.Error).IsNotNull();
        await Assert.That(generic.Error!.Kind).IsEqualTo(RuntimeDomainFailureKind.Constraint);
        await Assert.That(generic.Error.Message).IsEqualTo("flat message");
        await Assert.That(generic.ErrorMessage).IsEqualTo("flat message");
    }

    [Test]
    public async Task Failure_Null_Throws() {
        await Assert.That(() => RuntimeDomainResult.Failure((RuntimeDomainFailure)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => RuntimeDomainResult.Failure((string)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => Poly.Runtime.DomainResult<string>.Failure((RuntimeDomainFailure)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => Poly.Runtime.DomainResult<string>.Failure((string)null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Success_HasNoError() {
        var voidResult = RuntimeDomainResult.Success();
        await Assert.That(voidResult.IsSuccess).IsTrue();
        await Assert.That(voidResult.Error).IsNull();
        await Assert.That(voidResult.ErrorMessage).IsNull();
        await Assert.That(voidResult).IsAssignableTo<Poly.Runtime.IDomainResult>();

        var generic = Poly.Runtime.DomainResult<string>.Success("ok");
        await Assert.That(generic.IsSuccess).IsTrue();
        await Assert.That(generic.Error).IsNull();
        await Assert.That(generic.ErrorMessage).IsNull();
        await Assert.That(generic.Value).IsEqualTo("ok");
        await Assert.That(generic).IsAssignableTo<Poly.Runtime.IDomainResult>();
    }

    [Test]
    public async Task Void_DomainResult_HasNoValueMember() {
        await Assert.That(typeof(RuntimeDomainResult).GetMember("Value", EveryMember)).IsEmpty();
        await Assert.That(typeof(Poly.Runtime.IDomainResult).GetMember("Value", EveryMember)).IsEmpty();
    }

    [Test]
    public async Task Properties_HaveNoSetterOrInitAccessor() {
        Type[] types = [
            typeof(RuntimeDomainFailure),
            typeof(RuntimeDomainResult),
            typeof(Poly.Runtime.DomainResult<string>),
            typeof(Poly.Runtime.IDomainResult),
        ];

        var mutable = types
            .SelectMany(type => type.GetProperties(EveryMember))
            .Where(property => property.SetMethod is not null)
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")
            .ToArray();

        await Assert.That(mutable).IsEmpty();
    }
}