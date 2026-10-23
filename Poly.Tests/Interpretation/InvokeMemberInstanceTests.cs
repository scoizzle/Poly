using System.Collections;

using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.Vm;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

public class InvokeMemberInstanceTests {
    public sealed class RecordingInstance : IDictionary<string, object?> {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        public string? LastNotified { get; private set; }
        public int Calls { get; private set; }

        public void Notify(string stageName) {
            LastNotified = stageName;
            Calls++;
        }

        public object? this[string key] {
            get => _values[key];
            set => _values[key] = value;
        }
        public ICollection<string> Keys => _values.Keys;
        public ICollection<object?> Values => _values.Values;
        public int Count => _values.Count;
        public bool IsReadOnly => false;
        public void Add(string key, object? value) => _values.Add(key, value);
        public void Add(KeyValuePair<string, object?> item) =>
            ((ICollection<KeyValuePair<string, object?>>)_values).Add(item);
        public void Clear() => _values.Clear();
        public bool Contains(KeyValuePair<string, object?> item) =>
            ((ICollection<KeyValuePair<string, object?>>)_values).Contains(item);
        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) =>
            ((ICollection<KeyValuePair<string, object?>>)_values).CopyTo(array, arrayIndex);
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();
        public bool Remove(string key) => _values.Remove(key);
        public bool Remove(KeyValuePair<string, object?> item) =>
            ((ICollection<KeyValuePair<string, object?>>)_values).Remove(item);
        public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);
        IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
    }

    private static ITypeDefinitionProvider BuildNotifyTypeDef(string typeName = "Person") {
        var typeDef = new TypeDefinitionNode(
            Name: typeName,
            Properties: [
                new PropertyDefinitionNode(
                    "CurrentStage",
                    new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.String),
                    Getter: new PropertyGetterDefinitionNode())
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Notify",
                    new TypeReference("void"),
                    Parameters: [new Parameter("stageName",
                        new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.String))],
                    Body: new Block([])),
                new MethodDefinitionNode(
                    "Bounce",
                    new TypeReference("void"),
                    Body: new Block([]))
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeDef);
        return ctx.TypeDefinitions;
    }

    private static Invoke NotifyInvoke(string typeName = "Person") =>
        new(new Member(new Parameter("entity", new TypeReference(typeName)), "Notify"),
            new Constant("Active"));

    [Test]
    public async Task Generate_InvokeMemberThisNotify_PrintsThisNotify() {
        var node = new Invoke(
            new Member(new ThisReference(), "Notify"),
            new Constant("Active"));
        var result = new CSharpGenerator().Generate(node);
        await Assert.That(result).IsEqualTo("this.Notify(\"Active\");");
    }

    [Test]
    public async Task Execute_HeapInstanceWithNotify_InvokesMethod() {
        var instance = new RecordingInstance();
        var program = Interpreter.Compile(NotifyInvoke(), BuildNotifyTypeDef());
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { instance }));
        await Assert.That(instance.Calls).IsEqualTo(1);
        await Assert.That(instance.LastNotified).IsEqualTo("Active");
    }

    [Test]
    public async Task Execute_InstanceMissingMethod_Throws() {
        var bag = new Dictionary<string, object?>(StringComparer.Ordinal);
        var program = Interpreter.Compile(NotifyInvoke(), BuildNotifyTypeDef());
        var ex = Assert.Throws<InvalidOperationException>(() => {
            using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        });
        await Assert.That(ex!.Message).Contains("does not define method 'Notify'");
    }

    public sealed class NamedDispatchInstance {
        public List<string> Names { get; } = [];
        internal object? InvokeNamed(string name, object?[] args) {
            Names.Add(name);
            if (string.Equals(name, "Bounce", StringComparison.Ordinal) && Names.Count > 2)
                throw new InvalidOperationException("Action invoke depth exceeded (max 2) while calling 'Bounce'.");
            return null;
        }
    }

    [Test]
    public async Task Execute_UnresolvedMember_FallsBackToInvokeNamed() {
        var instance = new NamedDispatchInstance();
        var invoke = new Invoke(
            new Member(new Parameter("entity", new TypeReference("Person")), "Bounce"));
        var program = Interpreter.Compile(invoke, BuildNotifyTypeDef());
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { instance }));
        await Assert.That(instance.Names).IsEquivalentTo(["Bounce"]);
    }

    [Test]
    public async Task Execute_InvokeNamedThrow_SurfacesInnerMessage() {
        var instance = new NamedDispatchInstance();
        var bounce = new Invoke(
            new Member(new Parameter("entity", new TypeReference("Person")), "Bounce"));
        var program = Interpreter.Compile(
            new Block([bounce, bounce, bounce]), BuildNotifyTypeDef());
        await Assert.That(() => {
            using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { instance }));
        }).Throws<InvalidOperationException>()
            .WithMessageContaining("depth exceeded");
    }

    [Test]
    public async Task Execute_AstMethodBody_RunsOnDictionary() {
        var typeDef = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Ping",
                    new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.Int64),
                    Body: new Constant(7L))
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeDef);
        var bag = new Dictionary<string, object?>();
        var invoke = new Invoke(
            new Member(new Parameter("entity", new TypeReference("Sample.Widget")), "Ping"));
        var program = Interpreter.Compile(invoke, ctx.TypeDefinitions);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { bag }));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task AstInvoke_ResultIsInTheRing() {
        var typeDef = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Ping",
                    new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.Int64),
                    Body: new Constant(7L))
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeDef);
        var invoke = new Invoke(
            new Member(new Parameter("entity", new TypeReference("Sample.Widget")), "Ping"));
        var program = Interpreter.Compile(invoke, ctx.TypeDefinitions);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { new Dictionary<string, object?>() }));
        // The callee writes its result word at slots[FramePos]; the caller moves it.
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task VoidInvoke_LeavesZero() {
        var list = new List<string> { "a" };
        var invoke = new Invoke(
            new Member(new Parameter("list", TypeReference.To<List<string>>()), "Clear"));
        using var exec = Interpreter.Execute(
            Interpreter.Compile(invoke), s => s.SetArgs(new object?[] { list }));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
        await Assert.That(list).IsEmpty();
    }

    [Test]
    public async Task VoidAstInvoke_LeavesZero() {
        // A body that yields no value still leaves a result word, so the caller's slot
        // is never unwritten.
        var typeDef = new TypeDefinitionNode(
            "Widget",
            "Sample",
            Methods: [
                new MethodDefinitionNode(
                    "Reset",
                    new TypeReference("void"),
                    Body: new Block([new Comment("nothing to do")]))
            ]);
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var ctx = AnalysisContext.CreateDefault();
        analyzer.Analyze(ctx, typeDef);
        var invoke = new Invoke(
            new Member(new Parameter("entity", new TypeReference("Sample.Widget")), "Reset"));
        var program = Interpreter.Compile(invoke, ctx.TypeDefinitions);
        using var exec = Interpreter.Execute(program, s => s.SetArgs(new object?[] { new Dictionary<string, object?>() }));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(0L);
    }
}