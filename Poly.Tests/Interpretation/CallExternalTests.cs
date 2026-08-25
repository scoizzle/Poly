using Poly.Interpretation;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;

namespace Poly.Tests.Interpretation;

public class CallExternalTests {
    public sealed class RecordingHost {
        public string? LastNotified { get; private set; }
        public int Calls { get; private set; }

        public void Notify(string stageName) {
            LastNotified = stageName;
            Calls++;
        }
    }

    [Test]
    public async Task Generate_CallExternal_ProducesBareCall() {
        var node = new CallExternal("Notify", new Constant("Active"));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("Notify(\"Active\");");
    }

    [Test]
    public async Task Execute_WithHost_InvokesHostMethod() {
        var host = new RecordingHost();
        var program = Interpreter.Compile(new CallExternal("Notify", new Constant("Active")));
        using var exec = Interpreter.Execute(program, s => s.Host = host);
        await Assert.That(host.Calls).IsEqualTo(1);
        await Assert.That(host.LastNotified).IsEqualTo("Active");
    }

    [Test]
    public async Task Execute_NullHost_Throws() {
        var program = Interpreter.Compile(new CallExternal("Notify", new Constant("Active")));
        var ex = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Interpreter.Execute(program);
        });
        await Assert.That(ex!.Message).Contains("requires VmState.Host");
    }

    [Test]
    public async Task Execute_MissingMethod_Throws() {
        var host = new RecordingHost();
        var program = Interpreter.Compile(new CallExternal("NoSuchMethod", new Constant("Active")));
        var ex = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Interpreter.Execute(program, s => s.Host = host);
        });
        await Assert.That(ex!.Message).Contains("does not define method 'NoSuchMethod'");
    }
}
