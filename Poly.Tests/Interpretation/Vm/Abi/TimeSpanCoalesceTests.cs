using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>An optional struct parameter coalesced with a call arm answers the struct, not a word.</summary>
public class TimeSpanCoalesceTests {
    private static Node Body() {
        var budget = new Parameter("budget", new OptionalTypeReference(TypeReference.To<TimeSpan>()));
        var fallback = new Invoke(
            new Member(new NamedTypeReference("TimeSpan"), "FromMinutes"),
            new Constant(30.0));
        return new Coalesce(budget, fallback);
    }

    [Test]
    public async Task OptionalTimeSpan_Absent_CoalescesToFallback() {
        var body = Body();
        using var exec = Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs([null]));
        await Assert.That(exec.GetValue<TimeSpan>()).IsEqualTo(TimeSpan.FromMinutes(30));
    }

    [Test]
    public async Task OptionalTimeSpan_Present_KeepsLeft() {
        var body = Body();
        using var exec = Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs([TimeSpan.FromHours(2)]));
        await Assert.That(exec.GetValue<TimeSpan>()).IsEqualTo(TimeSpan.FromHours(2));
    }
}