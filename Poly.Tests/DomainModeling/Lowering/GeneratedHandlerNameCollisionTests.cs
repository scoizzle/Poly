using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

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

    /// <summary>#890: the same domain-wide set at member scope. A property named like another entity's generated
    /// stage enum shadows it where the subscription handler reads it (<c>BookStage.Out</c>), which is CS1061 in the
    /// emitted C#.</summary>
    private static string MemberDsl(string member) => $$"""
        domain P
        Shelf: entity {
          {{member}}: Text default("")
          Label: Text default("")
          books: many Book
          when any books Out { assign Label to "x" }
        }
        Book: entity {
          shelf: Shelf via books
          In: stage { Go: action { transition to Out } }
          Out: stage { }
        }
        """;

    [Test]
    public async Task Property_NamedLikeAnotherEntitysStageEnum_IsRefusedNamingTheEntity() {
        var (errors, compile) = Verdict(MemberDsl("BookStage"), "MemberEnum");

        var error = errors.First(e => e.Code == DomainModelDiagnosticCodes.ReservedMemberName);
        await Assert.That(error.Message).Contains("Member 'BookStage' on entity 'Shelf'");
        await Assert.That(error.Message).Contains("would shadow the generated C# type 'BookStage'");
        await Assert.That(error.Node.GetType().GetProperty("Name")!.GetValue(error.Node)).IsEqualTo("BookStage");
        await Assert.That(compile).IsEqualTo("");
    }

    [Test]
    public async Task ActionParameter_NamedLikeAStageEnumThatIsNotGenerated_IsAcceptedAndCompiles() {
        var (errors, compile) = Verdict(StagelessParameterDsl("ShelfStage"), "NoEnum");

        await Assert.That(string.Join("\n", errors.Select(e => e.Code + " " + e.Message))).IsEqualTo("");
        await Assert.That(compile).IsEqualTo("");
    }

    // Closing-round probe (b): an action parameter named like a numbered generated local. `result0` holds (refused or
    // compiles; it compiles) for a fan-out `for … invoke`, on master and here.
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

    // ── #889 / #927 / #516: every lowered local is `__`-prefixed, so no author name collides with one ──

    /// <summary>The four documents #889 records, one per numbered <c>LocalNames.Next</c> local: analysis accepted each
    /// and the printed C# did not compile (CS0136), because the parameter and the lowered local shared a name.</summary>
    private static readonly (string Name, string Dsl, string Entity, string Action)[] NumberedLocalDocuments = [
        ("assignValue0", """
            domain P
            Doc: entity {
              Score: Number range(0, 10) default(0)
              Bump: action (assignValue0: Number) { assign Score to Score + 1 }
            }
            """, "Doc", "Bump"),
        ("create0", """
            domain P
            Holder: entity {
              kids: many Kid
              Make: action (create0: Number) { create in kids { } }
            }
            Kid: entity {
              holder: Holder via kids
            }
            """, "Holder", "Make"),
        ("invoke0", """
            domain P
            Doc: entity {
              Score: Number default(0)
              Inner: action { assign Score to 1 }
              Outer: action (invoke0: Number) { invoke Inner }
            }
            """, "Doc", "Outer"),
        ("target0", """
            domain P
            Holder: entity {
              kids: many Kid
              Run: action (target0: Number) { for k in kids { invoke k.Go } }
            }
            Kid: entity {
              N: Number default(0)
              holder: Holder via kids
              Go: action { assign N to 1 }
            }
            """, "Holder", "Run"),
    ];

    /// <summary>A property named <c>Created</c> is an author name, not a reserved one: the bare factory's local for the
    /// new instance must not meet the slot's <c>__created</c> effective value, and the constructor must not meet the
    /// <c>created</c> parameter. Both hosts create, with the authored value.</summary>
    [Test]
    public async Task Property_NamedCreated_CreatesOnBothHosts() {
        const string dsl = """
            domain P
            Doc: entity { Created: Text }
            """;

        var (vm, printed) = ParityScenario.FromDsl(dsl, "CreatedProperty")
            .Run(side => side.Create("Doc", ("Created", "t")));

        await Assert.That(vm[0].Success).IsTrue();
        await Assert.That(printed[0].Success).IsTrue();
        foreach (var outcome in new[] { vm[0], printed[0] })
            await Assert.That(outcome.State["Created"]).IsEqualTo("t");
    }

    /// <summary>An action parameter named like a numbered lowered local compiles and runs on both hosts.</summary>
    [Test]
    public async Task ActionParameter_NamedANumberedGeneratedLocal_CompilesAndRunsOnBothHosts() {
        var problems = new List<string>();
        foreach (var (name, dsl, entity, action) in NumberedLocalDocuments) {
            try {
                var (vm, printed) = ParityScenario.FromDsl(dsl, "Numbered" + name).Run(side => {
                    side.Create(entity);
                    side.Invoke(action, (name, 1L));
                });
                foreach (var outcome in new[] { vm[1], printed[1] })
                    if (!outcome.Success)
                        problems.Add($"{name}: {outcome.Step} failed: {outcome.Message ?? outcome.ExceptionType}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) {
                problems.Add($"{name}: {ex.Message.Split('\n')[0]}");
            }
        }

        await Assert.That(string.Join("\n", problems)).IsEqualTo("");
    }
}