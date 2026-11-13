using System.Reflection;

using Poly.Interpretation;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// R-03b: printed C# binds the shared <c>Poly.Runtime</c> result family. A failed
/// result's <c>Value</c> throws while its <c>ToString</c> does not (#538/#554), and the
/// result and the constraint failure a printed body throws are the runtime types, not
/// printed twins compiled into the emitted assembly.
/// </summary>
public sealed class DomainResultValueTests {
    const string Dsl = """
        domain Spot
        Item: entity { Name: Text Qty: Number range(1, 10) }
        """;

    [Test]
    public async Task ItemCreate_FailedResult_IsTheRuntimeType_AndValueThrows() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Dsl);
        var files = session.Emit(domain, analysis);
        var asm = ExportedCSharp.CompileAndLoad(files, "DomainResultValue");
        var item = asm.GetType("Spot.Item")!;

        var failed = ExportedCSharp.InvokeCreate(item, ("Name", "n"), ("Qty", 99L));

        // (a) The result is Poly.Runtime's, not a twin compiled into the emitted assembly.
        await Assert.That(failed.GetType().Assembly.GetName().Name).IsEqualTo("Poly.Runtime");
        await Assert.That(IsSuccess(failed)).IsFalse();

        // (b) Reading Value on a failure throws the runtime message.
        var thrown = await Assert.That(() => Value(failed)).Throws<TargetInvocationException>();
        await Assert.That(thrown!.InnerException).IsAssignableTo<InvalidOperationException>();
        await Assert.That(thrown.InnerException!.Message).IsEqualTo("A failed DomainResult has no Value.");

        // (c) #538/#554: a failed result's text must not read Value.
        await Assert.That(() => failed.ToString()).ThrowsNothing();
        await Assert.That(failed.ToString()).Contains("Error");

        // (d) A successful result still carries its value.
        var succeeded = ExportedCSharp.InvokeCreate(item, ("Name", "n"), ("Qty", 5L));
        await Assert.That(IsSuccess(succeeded)).IsTrue();
        await Assert.That(Value(succeeded)).IsNotNull();
    }

    [Test]
    public async Task ConstraintFailure_ThrownByPrintedCode_IsTheRuntimeType() {
        // A void OnEntry body cannot return a failure, so it throws. The throw must be
        // the runtime type the executor catches, not a printed twin.
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve("""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Bump: Number default(100)
              Draft: stage {
                entry { assign Score to Bump }
              }
            }
            """);
        var files = session.Emit(domain, analysis);
        var asm = ExportedCSharp.CompileAndLoad(files, "ConstraintFailure");
        var widget = asm.GetType("Lab.Widget")!;
        var ctor = widget
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var args = ctor.GetParameters()
            .Select(p => p.Name switch {
                "score" => (object?)50L,
                "bump" => 100L,
                _ => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null,
            })
            .ToArray();

        var thrown = await Assert.That(() => ctor.Invoke(args)).Throws<TargetInvocationException>();

        await Assert.That(ReferenceEquals(
                thrown!.InnerException!.GetType(),
                typeof(Poly.DomainModeling.Runtime.ConstraintFailureException)))
            .IsTrue()
            .Because(thrown.InnerException.GetType().FullName ?? thrown.InnerException.GetType().Name);
    }

    static bool IsSuccess(object result) =>
        (bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!;

    static object? Value(object result) =>
        result.GetType().GetProperty("Value", PrintedMembers.Any)!.GetValue(result);
}