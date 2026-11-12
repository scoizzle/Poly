using System.Reflection;

using RuntimeAsyncDomainResult = Poly.Runtime.AsyncDomainResult;
using RuntimeDomainFailure = Poly.Runtime.DomainFailure;
using RuntimeDomainFailureKind = Poly.Runtime.DomainFailureKind;
using RuntimeDomainResult = Poly.Runtime.DomainResult;

namespace Poly.Tests.Runtime;

/// <summary>
/// R-10: the completed-task contract a lowered <c>async</c> action returns. These tests
/// prove the factories usable in-process, not evaluator behaviour (R-09 A6 framing):
/// completion is asserted before any <c>await</c>, and a failure travels as a value in a
/// successful task, never as a fault.
/// <para>
/// The file never adds <c>using Poly.Runtime;</c>: the namespace <c>Poly.Tests.Runtime</c>
/// shadows the <c>Runtime</c> segment, so the runtime types are aliased or fully qualified
/// (<c>DomainResult&lt;T&gt;</c> has no alias because C# has no open-generic alias).
/// </para>
/// </summary>
public class AsyncDomainResultContractTests {
    /// <summary>The contract lives on the type itself, not inherited from <see cref="object"/>.</summary>
    const BindingFlags DeclaredStatics =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Test]
    public async Task Success_IsCompletedAndSuccess() {
        var task = RuntimeAsyncDomainResult.Success();

        await Assert.That(task.IsCompletedSuccessfully).IsTrue()
            .Because("T1: completed before any await; the VM Await blocks on the operand");
        await Assert.That(task.Status).IsEqualTo(TaskStatus.RanToCompletion);

        var result = await task;
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task AsyncFailure_PreservesKindAndPath() {
        var error = RuntimeDomainFailure.Arithmetic("overflow", "Order.Lines", "QtyInRange");
        var task = RuntimeAsyncDomainResult.Failure(error);

        await Assert.That(task.IsCompletedSuccessfully).IsTrue()
            .Because("T1: completed before any await; a failure is a value, not a fault");
        await Assert.That(task.Status).IsEqualTo(TaskStatus.RanToCompletion);

        var result = await task;
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error!.Kind).IsEqualTo(RuntimeDomainFailureKind.Arithmetic);
        await Assert.That(result.Error.ElementPath).IsEqualTo("Order.Lines");
        await Assert.That(result.Error.Guard).IsEqualTo("QtyInRange");
        await Assert.That(result.Error.Render()).IsEqualTo("overflow");
        await Assert.That(ReferenceEquals(result.Error, error)).IsTrue()
            .Because("D3: the caller's failure instance travels unchanged, so no kind is downgraded");
    }

    [Test]
    public async Task AsyncFailure_TypedArm_PreservesKindAndPath_AndValueThrows() {
        var error = RuntimeDomainFailure.Range("out of range", "Order.Total", "TotalInRange");
        var task = RuntimeAsyncDomainResult.Failure<int>(error);

        await Assert.That(task.IsCompletedSuccessfully).IsTrue()
            .Because("T1: completed before any await, typed arm");
        await Assert.That(task.Status).IsEqualTo(TaskStatus.RanToCompletion);

        var result = await task;
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error!.Kind).IsEqualTo(RuntimeDomainFailureKind.Range);
        await Assert.That(result.Error.ElementPath).IsEqualTo("Order.Total");
        await Assert.That(result.Error.Guard).IsEqualTo("TotalInRange");
        await Assert.That(() => result.Value).Throws<InvalidOperationException>()
            .Because("a failed DomainResult<T> has no Value");
    }

    [Test]
    public async Task Failure_Null_Throws() {
        await Assert.That(() => { _ = RuntimeAsyncDomainResult.Failure(null!); })
            .Throws<ArgumentNullException>()
            .Because("D5: null throws at the call site, never as a faulted task");
        await Assert.That(() => { _ = RuntimeAsyncDomainResult.Failure<int>(null!); })
            .Throws<ArgumentNullException>()
            .Because("D5: null throws at the call site, never as a faulted task");
    }

    [Test]
    public async Task NoFactoryTakesAMessageString() {
        var methods = typeof(RuntimeAsyncDomainResult).GetMethods(DeclaredStatics);

        await Assert.That(methods.Length).IsEqualTo(4)
            .Because("D1: exactly four factories, no other member");
        foreach (var method in methods) {
            await Assert.That(method.GetParameters().Any(p => p.ParameterType == typeof(string)))
                .IsFalse()
                .Because($"g.3: no async message-string shim; {method.Name} must not take a string");
        }
    }

    [Test]
    public async Task Namespace_AndAssembly_AreTheRuntimePackage() {
        var type = typeof(RuntimeAsyncDomainResult);

        await Assert.That(type.Namespace).IsEqualTo("Poly.Runtime")
            .Because("R-09 lock A1: namespace Poly.Runtime, written by hand");
        await Assert.That(type.IsAbstract && type.IsSealed).IsTrue()
            .Because("D1: AsyncDomainResult is a static factory class");
        await Assert.That(type.Assembly.GetName().Name).IsEqualTo("Poly.Runtime");
    }

    [Test]
    public async Task Success_TypedArm_CarriesTheValue_AndNoError() {
        var task = RuntimeAsyncDomainResult.Success(7);

        await Assert.That(task.IsCompletedSuccessfully).IsTrue()
            .Because("T1: completed before any await, typed success arm");
        await Assert.That(task.Status).IsEqualTo(TaskStatus.RanToCompletion);

        var result = await task;
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(7);
        await Assert.That(result.Error).IsNull();
    }
}