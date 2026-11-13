using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-04a (from L-03c's review, nit 3): a peer binder or an action parameter named after a name the generated handler or
/// action body declares or uses must not break the generated C#. The handler declares <c>__linkedTarget</c> and
/// <c>__linkedMatched</c> (the <c>when any</c> / <c>when all</c> gates) and takes a <c>__previousStage</c> parameter: the
/// <c>__</c> prefix is reserved, so no user name collides with them. It reads the stage enums <c>{Entity}Stage</c> from
/// expression position, so exactly those names are reserved. Either the analysis refuses the name through the
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

    // ── fix round 1: no shape rule; the generated locals are `__`-prefixed; the stage enums are reserved exactly ──

    private static string StagelessParameterDsl(string parameter) => $$"""
        domain Names
        Shelf: entity {
          Label: Text default("")
          Tag: action ({{parameter}}: Text) { assign Label to {{parameter}} }
        }
        """;

    /// <summary>The verdict of one document: the analysis errors, or the compile diagnostics of the printed C#. Empty is accepted and compiles.</summary>
    private static (IReadOnlyList<Poly.Analysis.Diagnostic> Errors, string Compile) Verdict(string dsl, string name) {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        var errors = evolved.Analysis.Diagnostics.Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            return (errors, "");
        var cs = new Poly.Interpretation.CSharp.CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
        return ([], string.Join("\n", ExportedCSharp.CompileWithWarningsAsErrors("Verdict" + name, [("N.cs", cs)]).Select(d => d.ToString())));
    }

    [Test]
    [Arguments("NextStage")]
    [Arguments("FromStage")]
    [Arguments("CurrentStage")]
    [Arguments("linkedTarget")]
    [Arguments("linkedMatched")]
    [Arguments("previousStage")]
    public async Task ActionParameter_WithANameThatIsNoGeneratedEnum_IsAcceptedAndCompiles(string name) {
        var (errors, compile) = Verdict(ParameterDsl(name), "Accept" + name);

        await Assert.That(string.Join("\n", errors.Select(e => e.Code + " " + e.Message))).IsEqualTo("");
        await Assert.That(compile).IsEqualTo("");
    }

    [Test]
    [Arguments("NextStage")]
    [Arguments("CurrentStage")]
    [Arguments("linkedTarget")]
    [Arguments("linkedMatched")]
    [Arguments("previousStage")]
    public async Task PeerBinder_WithANameThatIsNoGeneratedEnum_IsAcceptedAndCompiles(string name) {
        foreach (var dsl in new[] { BinderDsl(name), AllBinderDsl(name) }) {
            var (errors, compile) = Verdict(dsl, "BAccept" + name);

            await Assert.That(string.Join("\n", errors.Select(e => e.Code + " " + e.Message))).IsEqualTo("");
            await Assert.That(compile).IsEqualTo("");
        }
    }

    [Test]
    public async Task ActionParameter_NamedLikeAGeneratedStageEnum_IsRefusedAtTheParameterNamingTheEntity() {
        var (errors, _) = Verdict(ParameterDsl("ShelfStage"), "EnumParam");

        await Assert.That(errors.Count).IsGreaterThan(0);
        var error = errors.First(e => e.Code == DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(error.Message).Contains("'ShelfStage'");
        await Assert.That(error.Message).Contains("generated stage enum of entity 'Shelf'");
        await Assert.That(error.Node.GetType().GetProperty("Name")!.GetValue(error.Node)).IsEqualTo("ShelfStage");
    }

    [Test]
    public async Task PeerBinder_NamedLikeAnotherEntitysStageEnum_IsRefusedNamingTheEntity() {
        var (errors, _) = Verdict(BinderDsl("BookStage"), "EnumBinder");

        var error = errors.First(e => e.Code == DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(error.Message).Contains("generated stage enum of entity 'Book'");
    }

    [Test]
    public async Task ActionParameter_NamedLikeAStageEnumThatIsNotGenerated_IsAcceptedAndCompiles() {
        var (errors, compile) = Verdict(StagelessParameterDsl("ShelfStage"), "NoEnum");

        await Assert.That(string.Join("\n", errors.Select(e => e.Code + " " + e.Message))).IsEqualTo("");
        await Assert.That(compile).IsEqualTo("");
    }

    // Closing-round probe (b): an action parameter named like a numbered generated local. `result0` holds (refused or
    // compiles; it compiles) for a fan-out `for … invoke`, on master and here. The other numbered locals (assignValue0,
    // create0, invoke0, target0) and a member named like the bare Create's `created` or another entity's stage enum
    // are accepted by analysis and do not compile, on master too: reported, not asserted.
    [Test]
    public async Task ActionParameter_NamedResult0_BesideAFanOutForInvoke_IsRefusedOrCompiles() {
        const string dsl = """
            domain P
            Holder: entity {
              kids: many Kid
              Run: action (result0: Number) { for kids as k invoke k.Go }
            }
            Kid: entity {
              N: Number default(0)
              holder: Holder via kids
              Go: action { assign N to 1 }
            }
            """;

        var (errors, compile) = Verdict(dsl, "Result0");

        await Assert.That(errors.Count == 0 ? compile : "").IsEqualTo("");
    }
}
