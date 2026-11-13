using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-04a (from L-03c's review, nit 3): a peer binder or an action parameter named after a name the generated handler or
/// action body declares or uses must not break the generated C#. The handler declares <c>linkedTarget</c> and
/// <c>linkedMatched</c> (the <c>when any</c> / <c>when all</c> gates), takes a <c>previousStage</c> parameter, and reads
/// the stage enums <c>{Entity}Stage</c> from expression position. Either the analysis refuses the name through the
/// reserved-name rule (DMSTR009), or the printed C# compiles with warnings as errors; nothing in between (CS0136,
/// CS0100, a shadowed stage enum).
/// </summary>
public class GeneratedHandlerNameCollisionTests {
    private static string BinderDsl(string binder) => $$"""
        domain Names
        Shelf: entity {
          Label: Text default("")
          Phase: stage {
            when any books Out as {{binder}} { assign Label to ({{binder}} Code) }
          }
          Idle: stage { }
          books: many Book
        }
        Book: entity {
          Code: Text default("c")
          shelf: Shelf via books
          In: stage { Go: action { transition to Out } }
          Out: stage { }
        }
        """;

    private static string AllBinderDsl(string binder) => $$"""
        domain Names
        Shelf: entity {
          Label: Text default("")
          books: many Book
          when all books Out as {{binder}} { assign Label to ({{binder}} Code) }
        }
        Book: entity {
          Code: Text default("c")
          shelf: Shelf via books
          In: stage { Go: action { transition to Out } }
          Out: stage { }
        }
        """;

    private static string ParameterDsl(string parameter) => $$"""
        domain Names
        Shelf: entity {
          Label: Text default("")
          books: many Book
          Tag: action ({{parameter}}: Text) { assign Label to {{parameter}} }
          Move: stage { }
          Send: action ({{parameter}}: Text) { transition to Move assign Label to {{parameter}} }
        }
        Book: entity {
          Code: Text default("c")
          shelf: Shelf via books
        }
        """;

    private static readonly string[] Names = ["linkedTarget", "linkedMatched", "previousStage", "ShelfStage", "BookStage"];

    /// <summary>Null when the analysis refuses the document; else the compile diagnostics of the printed C#.</summary>
    private static string? Outcome(string dsl, string name) {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        string cs;
        try {
            var changes = new PolyDslParser(dsl, session).Parse();
            var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                .ApplyAndAnalyze(changes, session: session);
            if (!evolved.Succeeded)
                return evolved.Analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.ReservedMemberName
                    || d.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase))
                    ? null
                    : "analysis failed: " + string.Join("; ", evolved.Analysis.Diagnostics.Select(d => d.Code + " " + d.Message));
            cs = new Poly.Interpretation.CSharp.CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) {
            return ex.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase) ? null : "threw: " + ex.Message;
        }
        var diagnostics = ExportedCSharp.CompileWithWarningsAsErrors("Names" + name, [("N.cs", cs)]);
        return string.Join("\n", diagnostics.Select(d => d.ToString()));
    }

    [Test]
    public async Task PeerBinder_NamedAfterAGeneratedHandlerName_IsRefusedOrCompiles() {
        var problems = new List<string>();
        foreach (var name in Names)
            foreach (var (kind, dsl) in new[] { ("any", BinderDsl(name)), ("all", AllBinderDsl(name)) }) {
                var outcome = Outcome(dsl, $"{kind}{name}");
                if (!string.IsNullOrEmpty(outcome))
                    problems.Add($"when {kind} … as {name}: {outcome}");
            }

        await Assert.That(string.Join("\n", problems)).IsEqualTo("");
    }

    [Test]
    public async Task ActionParameter_NamedAfterAGeneratedHandlerName_IsRefusedOrCompiles() {
        var problems = new List<string>();
        foreach (var name in Names) {
            var outcome = Outcome(ParameterDsl(name), "Param" + name);
            if (!string.IsNullOrEmpty(outcome))
                problems.Add($"parameter {name}: {outcome}");
        }

        await Assert.That(string.Join("\n", problems)).IsEqualTo("");
    }
}
