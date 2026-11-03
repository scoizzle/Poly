using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// S13.2b: the lowered module reaches unique state only through the
/// <see cref="IStoragePort"/> it is handed. It never names the DEI runtime, so
/// a host can run it without <c>DomainInstanceStore</c>.
/// </summary>
public class UniqueModuleNamesOnlyStoragePortTests {
    private const string Dsl = """
        domain Lab
        Widget: entity {
          Code: Text unique
          Relabel: action (code: Text) {
            assign Code to code
          }
        }
        """;

    [Test]
    public async Task EmittedModule_NamesNoDeiRuntime() {
        var source = Lower(Dsl);

        await Assert.That(source).DoesNotContain("DomainInstanceStore");
        await Assert.That(source).DoesNotContain("PrintedPort");
        await Assert.That(source).DoesNotContain("PrintedCollision");
        await Assert.That(source).DoesNotContain("PrintedRecord");
        await Assert.That(source).Contains("db.Exists(\"Widget\", \"Code\"");
    }

    [Test]
    public async Task Create_WhenPortHasTheKey_FailsThroughPortExists() {
        var assembly = WhenAnySimulatePrintAgreeTests.CompileGenerated(Lower(Dsl), "UniqueOnlyPortCreate");
        var port = new RecordingPort();
        port.Seed("Widget", "Code", "A");

        var type = assembly.GetType("Widget")!;
        var result = ExportedCSharp.InvokeCreate(type, ("code", "A"), ("db", port));

        await Assert.That(Success(result)).IsFalse();
        await Assert.That(Message(result)).Contains("Unique constraint violated");
        await Assert.That(port.ExistsCalls).Contains(("Widget", "Code", "A"));
        await Assert.That(port.RecordCalls).IsEmpty();
    }

    [Test]
    public async Task Create_WhenKeyIsFree_RecordsOnceOnThePort() {
        var assembly = WhenAnySimulatePrintAgreeTests.CompileGenerated(Lower(Dsl), "UniqueOnlyPortFree");
        var port = new RecordingPort();

        var type = assembly.GetType("Widget")!;
        var result = ExportedCSharp.InvokeCreate(type, ("code", "A"), ("db", port));

        await Assert.That(Success(result)).IsTrue();
        await Assert.That(port.RecordCalls.Count).IsEqualTo(1);
        await Assert.That(port.RecordCalls[0]).IsEqualTo(("Widget", "Code", "A"));
    }

    [Test]
    public async Task Assign_WhenPortHasTheKey_FailsThroughPortExists() {
        var assembly = WhenAnySimulatePrintAgreeTests.CompileGenerated(Lower(Dsl), "UniqueOnlyPortAssign");
        var port = new RecordingPort();
        var type = assembly.GetType("Widget")!;
        var widget = ExportedCSharp.CreateEntity(type, ("code", "A"), ("db", port));
        port.Seed("Widget", "Code", "B");

        var relabel = type.GetMethod("Relabel")!;
        var result = relabel.Invoke(widget, ["B", port])!;

        await Assert.That(Success(result)).IsFalse();
        await Assert.That(Message(result)).Contains("Unique constraint violated");
        await Assert.That(type.GetProperty("Code")!.GetValue(widget)).IsEqualTo("A");
    }

    private static string Lower(string poly) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        foreach (var id in ExtensionCatalog.ProductAuthoring)
            builder.Load(ExtensionCatalog.Core.Resolve(id));
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return new CSharpGenerator().Generate(session.Lower(result.Root!, result.Analysis));
    }

    private static bool Success(object result) =>
        (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;

    private static string? Message(object result) =>
        result.GetType().GetProperty("ErrorMessage")!.GetValue(result) as string;

    private sealed class RecordingPort : IStoragePort {
        private readonly HashSet<(string, string, string)> _keys = [];
        public List<(string, string, string)> ExistsCalls { get; } = [];
        public List<(string, string, string)> RecordCalls { get; } = [];

        public void Seed(string entity, string property, string value) => _keys.Add((entity, property, value));

        public bool Exists(string entity, string property, string value) {
            ExistsCalls.Add((entity, property, value));
            return _keys.Contains((entity, property, value));
        }

        public void Record(string entity, string property, string value) {
            RecordCalls.Add((entity, property, value));
            _keys.Add((entity, property, value));
        }

        public void Release(string entity, string property, string value) => _keys.Remove((entity, property, value));
    }
}