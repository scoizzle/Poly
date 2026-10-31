using System.Reflection;

using Poly.Interpretation;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// A failed <c>DomainResult&lt;T&gt;</c> must not expose <c>default!</c> as <c>Value</c>.
/// Reading <c>Value</c> throws. Emit's module analyzer must accept the private field
/// and the private method that performs the check.
/// </summary>
public sealed class DomainResultValueTests {
    const string Dsl = """
        domain Spot
        Item: entity { Name: Text }
        """;

    [Test]
    public async Task Failure_Value_Throws() {
        var (domain, analysis, session) = SliceCProducerLoopCatalogTests.Evolve(Dsl);
        var files = session.Emit(domain, analysis);
        var module = session.Lower(domain, analysis);
        var generic = module.Single(t => t.Name == "DomainResult" && (t.GenericParameters?.Count ?? 0) == 1);
        var owned = Descendants(generic);
        var vm = Interpreter.Analyzer.Analyze(new CompilationUnitNode([], null, module, null));
        var errors = vm.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && owned.Contains(d.Node))
            .Select(d => $"{d.Node.GetType().Name}: {d.Message}")
            .ToArray();
        await Assert.That(errors).IsEmpty();

        var asm = ExportedCSharp.CompileAndLoad(Join(files), "DomainResultValue");
        var closed = asm.GetType("DomainResult`1")!.MakeGenericType(typeof(string));
        var failure = closed.GetMethod("Failure")!.Invoke(null, ["nope"])!;
        var thrown = await Assert.That(() => failure.GetType().GetProperty("Value")!.GetValue(failure))
            .Throws<TargetInvocationException>();
        await Assert.That(thrown!.InnerException).IsAssignableTo<InvalidOperationException>();
        await Assert.That(thrown.InnerException!.Message).IsEqualTo("A failed DomainResult has no Value.");

        var success = closed.GetMethod("Success")!.Invoke(null, ["ok"])!;
        await Assert.That(success.GetType().GetProperty("Value")!.GetValue(success)).IsEqualTo("ok");
    }

    static string Join(IReadOnlyList<(string FileName, string Source)> files) {
        var sources = files.Select(f => f.Source.Replace("\r\n", "\n").Split('\n')).ToList();
        var usings = sources.SelectMany(lines => lines)
            .Where(line => line.StartsWith("using ", StringComparison.Ordinal))
            .Distinct();
        var bodies = sources.Select(lines => string.Join('\n', lines.Where(line =>
            !line.StartsWith("using ", StringComparison.Ordinal) && line.Trim() != "#nullable enable")));
        return string.Join('\n', usings.Concat(bodies));
    }

    static HashSet<Node> Descendants(Node root) {
        var owned = new HashSet<Node>();
        Walk(root);
        return owned;

        void Walk(Node node) {
            owned.Add(node);
            foreach (var child in node.Children)
                if (child is not null)
                    Walk(child);
        }
    }
}