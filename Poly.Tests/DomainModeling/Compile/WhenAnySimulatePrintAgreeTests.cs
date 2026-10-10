using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// One .poly model with <c>when any</c>: simulate runs the module handler
/// session.Lower produced, and printed C# is that same body.
/// </summary>
public class WhenAnySimulatePrintAgreeTests {
    [Test]
    public async Task WhenAny_SimulateAndPrintedCsharp_ShareTheModuleHandler() {
        var poly = """
            domain Watch
            Loan: entity {
              Code: Text
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage { }
            }
            Patron: entity {
              Flag: Text
              loans: many Loan
              when any loans Overdue {
                assign Flag to "FIRED"
              }
            }
            """;
        var (domain, analysis, session) = Evolve(poly);
        var module = session.Lower(domain, analysis);
        var patronType = module.First(t => t.Name == "Patron");
        var handler = patronType.Methods?.FirstOrDefault(m => m.Name == "WhenAnyLoanOverdue");
        await Assert.That(handler).IsNotNull();
        await Assert.That(handler!.Body).IsNotNull();
        await Assert.That(ContainsNode<ThisReference>(handler.Body!)).IsTrue();

        var pending = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var plan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending)
            ?? throw new InvalidOperationException("missing entity subscription plan");
        var entry = plan.ByRelationshipName.Values.SelectMany(e => e).First();
        await Assert.That(RuntimeAnalysisCache.TryGetSubscriptionBody(domain, entry, "Overdue", out var cached))
            .IsTrue();
        await Assert.That(ReferenceEquals(handler.Body, cached)).IsTrue();

        var printed = new CSharpGenerator().Generate(handler.Body!);
        await Assert.That(printed.Contains("linkedMatched", StringComparison.Ordinal)).IsTrue();
        var files = session.Emit(domain, analysis);
        var patronCs = files.First(f => f.FileName == "Patron.cs").Source;
        await Assert.That(patronCs.Contains("WhenAnyLoanOverdue", StringComparison.Ordinal)).IsTrue();
        await Assert.That(patronCs.Contains("if (!linkedMatched)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(patronCs.Contains("linkedMatched != 1", StringComparison.Ordinal)).IsFalse();

        var loanE = domain.Types.OfType<Entity>().First(e => e.Name == "Loan");
        var patronE = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var store = new DomainInstanceStore();
        var loan1 = DomainEntityInstance.Create(loanE,
            new Dictionary<string, object?> { ["Code"] = "L1" }, domain);
        var loan2 = DomainEntityInstance.Create(loanE,
            new Dictionary<string, object?> { ["Code"] = "L2" }, domain);
        var patron = DomainEntityInstance.Create(patronE,
            new Dictionary<string, object?> { ["Flag"] = "NONE" }, domain);
        store.Add(loan1);
        store.Add(loan2);
        store.Add(patron);
        store.Link("loans", patron, loan1);
        store.Link("loans", patron, loan2);

        await Assert.That(loan1.InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("FIRED");

        IDictionary<string, object?> bag = loan1;
        await Assert.That(bag.ContainsKey("_patronLoansOverdueSubscribers")).IsTrue();
        await Assert.That(loan1.Snapshot().ContainsKey("_patronLoansOverdueSubscribers")).IsFalse();
        var registry = bag["_patronLoansOverdueSubscribers"] as System.Collections.IList;
        await Assert.That(registry).IsNotNull();
        await Assert.That(registry!.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(registry[0], patron)).IsTrue();

        patron.SetProperty("Flag", "NONE");
        await Assert.That(loan2.InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("FIRED");

        patron.SetProperty("Flag", "NONE");
        patron.InvokeNamed("WhenAnyLoanOverdue", []);
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("FIRED");
    }

    [Test]
    public async Task WhenAny_MultiStage_SimulateAndPrintedCsharp_SameFireCounts() {
        var poly = """
            domain Watch
            Loan: entity {
              Code: Text
              Draft: stage {
                Overdue: action { transition to Overdue }
                Lose: action { transition to Lost }
              }
              Overdue: stage { }
              Lost: stage { }
            }
            Patron: entity {
              Fires: Number default(0)
              loans: many Loan
              when any loans Overdue, Lost {
                assign Fires to Fires + 1
              }
            }
            """;
        var (domain, analysis, session) = Evolve(poly);
        var loanE = domain.Types.OfType<Entity>().First(e => e.Name == "Loan");
        var patronE = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var store = new DomainInstanceStore();
        var loan1 = DomainEntityInstance.Create(loanE,
            new Dictionary<string, object?> { ["Code"] = "L1" }, domain);
        var loan2 = DomainEntityInstance.Create(loanE,
            new Dictionary<string, object?> { ["Code"] = "L2" }, domain);
        var loan3 = DomainEntityInstance.Create(loanE,
            new Dictionary<string, object?> { ["Code"] = "L3" }, domain);
        var patron = DomainEntityInstance.Create(patronE,
            new Dictionary<string, object?> { ["Fires"] = 0L }, domain);
        store.Add(loan1);
        store.Add(loan2);
        store.Add(loan3);
        store.Add(patron);
        store.Link("loans", patron, loan1);
        store.Link("loans", patron, loan2);
        store.Link("loans", patron, loan3);

        await Assert.That(loan1.InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<object>("Fires")).IsEqualTo(1L);
        await Assert.That(loan2.InvokeAction("Lose").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<object>("Fires")).IsEqualTo(2L);
        await Assert.That(loan3.InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<object>("Fires")).IsEqualTo(3L);

        var types = session.Lower(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);
        var asm = CompileGenerated(cs, "WhenAnyPrintAgree");
        var printedFires = RunPrintedAnyScenario(asm);
        await Assert.That(printedFires[0]).IsEqualTo(1L);
        await Assert.That(printedFires[1]).IsEqualTo(2L);
        await Assert.That(printedFires[2]).IsEqualTo(3L);
    }

    private static long[] RunPrintedAnyScenario(Assembly asm) {
        var patron = CreateEntity(asm, "Patron");
        var loan1 = CreateEntity(asm, "Loan", ("code", "L1"));
        var loan2 = CreateEntity(asm, "Loan", ("code", "L2"));
        var loan3 = CreateEntity(asm, "Loan", ("code", "L3"));
        Attach(patron, "Loans", loan1);
        Attach(patron, "Loans", loan2);
        Attach(patron, "Loans", loan3);

        var fires = new long[3];
        InvokeAction(loan1, "Overdue");
        fires[0] = GetLong(patron, "Fires");
        InvokeAction(loan2, "Lose");
        fires[1] = GetLong(patron, "Fires");
        InvokeAction(loan3, "Overdue");
        fires[2] = GetLong(patron, "Fires");
        return fires;
    }

    private static bool ContainsNode<T>(Node node) where T : Node {
        if (node is T)
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ContainsNode<T>(child))
                return true;
        }
        return false;
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }

    internal static Assembly CompileGenerated(string cs, string assemblyName) {
        var tree = CSharpSyntaxTree.ParseText(cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray() ?? [];
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        var emitErrors = emit.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        if (emitErrors.Length > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, emitErrors));
        pe.Position = 0;
        var alc = new AssemblyLoadContext(assemblyName, isCollectible: true);
        return alc.LoadFromStream(pe);
    }

    internal static object CreateEntity(Assembly asm, string typeName, params (string Name, object? Value)[] named) {
        var type = asm.GetType(typeName)
            ?? throw new InvalidOperationException($"missing type {typeName}");
        var create = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Create")
            .OrderByDescending(m => m.GetParameters().Length)
            .First();
        var parameters = create.GetParameters();
        var callArgs = new object?[parameters.Length];
        var byName = named.ToDictionary(n => n.Name, n => n.Value, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parameters.Length; i++) {
            if (byName.TryGetValue(parameters[i].Name!, out var value))
                callArgs[i] = value;
            else if (parameters[i].HasDefaultValue)
                callArgs[i] = parameters[i].DefaultValue;
            else if (parameters[i].ParameterType == typeof(string))
                callArgs[i] = "";
            else if (parameters[i].ParameterType == typeof(long))
                callArgs[i] = 0L;
            else
                callArgs[i] = null;
        }
        var result = create.Invoke(null, callArgs)!;
        var ok = (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;
        if (!ok)
            throw new InvalidOperationException(
                result.GetType().GetProperty("ErrorMessage")!.GetValue(result) as string ?? "Create failed");
        return result.GetType().GetProperty("Value")!.GetValue(result)!;
    }

    internal static void Attach(object parent, string navPascal, object child) {
        var method = parent.GetType().GetMethod(
            $"Attach{navPascal}",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"missing Attach{navPascal}");
        method.Invoke(parent, [child]);
    }

    internal static void InvokeAction(object instance, string name) {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"missing action {name}");
        var result = method.Invoke(instance, null)!;
        var ok = (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;
        if (!ok)
            throw new InvalidOperationException(
                result.GetType().GetProperty("ErrorMessage")!.GetValue(result) as string
                ?? $"{name} failed");
    }

    internal static long GetLong(object instance, string property) {
        var value = instance.GetType().GetProperty(property)!.GetValue(instance);
        return Convert.ToInt64(value);
    }
}
