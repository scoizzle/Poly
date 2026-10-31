using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Slice 5: a duplicate create through the printed module and through the
/// simulator fails with the same message. Both sides run the same lowered
/// module; the port is the only boundary value.
/// </summary>
public class UniqueSimulatePrintAgreeTests {
    private const string Dsl = """
        domain Lab
        Widget: entity {
          Code: Text unique
        }
        Lot: entity {
          widgets: many Widget
          Issue: action (code: Text) {
            create in widgets { Code: code }
          }
          Double: action (code: Text) {
            create in widgets { Code: code }
            create in widgets { Code: code }
          }
        }
        """;

    [Test]
    public async Task DuplicateCreate_FailsWithTheSameMessageInPrintAndSimulate() {
        var (domain, analysis, session) = Evolve(Dsl);

        // Simulate: the module guard runs inside the action; the store gate is
        // still live until 3b, but both produce the same domain message.
        var lotEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotEntity, domain: domain);
        store.Add(lot);
        var first = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["code"] = "A" });
        var second = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["code"] = "A" });
        var third = lot.InvokeAction("Issue", new Dictionary<string, object?> { ["code"] = "B" });

        // Print: compile the same lowered module and invoke the action with a
        // real IStoragePort implementation.
        var module = session.Lower(domain, analysis);
        var source = new CSharpGenerator().Generate(module);
        var assembly = WhenAnySimulatePrintAgreeTests.CompileGenerated(source, "UniqueParity");
        var port = new TestPort();
        var printedLot = WhenAnySimulatePrintAgreeTests.CreateEntity(assembly, "Lot", ("db", port));
        var printedFirst = Invoke(printedLot, "Issue", ("code", "A"), ("db", port));
        var printedSecond = Invoke(printedLot, "Issue", ("code", "A"), ("db", port));
        var printedThird = Invoke(printedLot, "Issue", ("code", "B"), ("db", port));

        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(second.Succeeded).IsFalse();
        await Assert.That(third.Succeeded).IsTrue();
        await Assert.That(Success(printedFirst)).IsTrue();
        await Assert.That(Success(printedSecond)).IsFalse();
        await Assert.That(Success(printedThird)).IsTrue();
        await Assert.That(Message(printedSecond)).IsEqualTo(second.ErrorMessage);
        await Assert.That(second.ErrorMessage).Contains("Unique constraint violated");
    }

    [Test]
    public async Task NestedSecondCreate_FailsBeforeSaveInPrintAndSimulate() {
        var (domain, analysis, session) = Evolve(Dsl);

        var lotEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotEntity, domain: domain);
        store.Add(lot);
        var simulate = lot.InvokeAction("Double", new Dictionary<string, object?> { ["code"] = "A" });

        var module = session.Lower(domain, analysis);
        var source = new CSharpGenerator().Generate(module);
        var assembly = WhenAnySimulatePrintAgreeTests.CompileGenerated(source, "UniqueParityNested");
        var port = new TestPort();
        var printedLot = WhenAnySimulatePrintAgreeTests.CreateEntity(assembly, "Lot", ("db", port));
        var printed = Invoke(printedLot, "Double", ("code", "A"), ("db", port));

        // The first create recorded the pending key; the second create in the
        // same action fails before any host save.
        await Assert.That(simulate.Succeeded).IsFalse();
        await Assert.That(Success(printed)).IsFalse();
        await Assert.That(Message(printed)).IsEqualTo(simulate.ErrorMessage);
        await Assert.That(simulate.ErrorMessage).Contains("Unique constraint violated");
    }

    private static (Domain Domain, Poly.Analysis.AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
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
        return (result.Root!, result.Analysis, session);
    }

    private static object Invoke(object instance, string name, params (string Name, object? Value)[] arguments) {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"missing action {name}");
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        var byName = arguments.ToDictionary(a => a.Name, a => a.Value, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parameters.Length; i++) {
            if (byName.TryGetValue(parameters[i].Name!, out var value))
                args[i] = value;
            else if (parameters[i].HasDefaultValue)
                args[i] = parameters[i].DefaultValue;
            else
                args[i] = parameters[i].ParameterType == typeof(string) ? "" : null;
        }
        return method.Invoke(instance, args)!;
    }

    private static bool Success(object result) =>
        (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;

    private static string? Message(object result) =>
        result.GetType().GetProperty("ErrorMessage")!.GetValue(result) as string;

    private sealed class TestPort : IStoragePort {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public bool Exists(string entity, string property, string value) =>
            _keys.Contains(entity + "\u001f" + property + "\u001f" + value);

        public void Record(string entity, string property, string value) =>
            _keys.Add(entity + "\u001f" + property + "\u001f" + value);
    }
}