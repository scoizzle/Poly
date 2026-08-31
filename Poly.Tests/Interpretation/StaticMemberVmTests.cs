using Poly.Interpretation;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Type-name receivers are types, not values. Static members (DateTime.UtcNow,
/// DateOnly.FromDateTime, Guid.NewGuid, Int32.MaxValue) compile and execute.
/// </summary>
public class StaticMemberVmTests {
    [Test]
    public async Task GetTypeDefinition_PrimitiveShortName_ResolvesBclType() {
        var dateTime = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("DateTime");
        await Assert.That(dateTime).IsNotNull();
        await Assert.That(dateTime!.GetRuntimeType()).IsEqualTo(typeof(DateTime));

        var dateOnly = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("DateOnly");
        await Assert.That(dateOnly!.GetRuntimeType()).IsEqualTo(typeof(DateOnly));
    }

    [Test]
    public async Task Compile_TypeNameAsValue_FailsClosed() {
        await Assert.That(() => Interpreter.Compile(new NamedTypeReference("DateTime")))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: Type name is not a VM value; use it as a Member/Invoke/New receiver.");
    }

    [Test]
    public async Task Member_DateTimeUtcNow_ReturnsUtcClock() {
        var before = DateTime.UtcNow.AddSeconds(-2);
        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Member(new NamedTypeReference("DateTime"), "UtcNow")));
        var now = exec.GetValue<DateTime>();
        var after = DateTime.UtcNow.AddSeconds(2);
        await Assert.That(now.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(now >= before && now <= after).IsTrue();
    }

    [Test]
    public async Task Member_ClrTypeReferenceUtcNow_ReturnsUtcClock() {
        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Member(new ClrTypeReference(typeof(DateTime)), "UtcNow")));
        await Assert.That(exec.GetValue<DateTime>().Kind).IsEqualTo(DateTimeKind.Utc);
    }

    [Test]
    public async Task Invoke_DateOnlyFromDateTime_ReturnsDate() {
        var utcNow = new Member(new NamedTypeReference("DateTime"), "UtcNow");
        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"), utcNow)));
        await Assert.That(exec.GetValue<DateOnly>()).IsEqualTo(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Test]
    public async Task Invoke_GuidNewGuid_ReturnsGuid() {
        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Invoke(new Member(new NamedTypeReference("Guid"), "NewGuid"))));
        await Assert.That(exec.GetValue<Guid>()).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Member_Int32MaxValue_ReturnsField() {
        using var exec = Interpreter.Execute(Interpreter.Compile(
            new Member(new NamedTypeReference("Int32"), "MaxValue")));
        await Assert.That(exec.GetValue<int>()).IsEqualTo(int.MaxValue);
    }
}