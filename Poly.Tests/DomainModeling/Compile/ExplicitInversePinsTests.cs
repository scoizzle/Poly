using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// S-13a pins, green before any document gains an explicit inverse (<c>via</c>) or a navigation back:
/// stating a pair with <c>via</c> changes no lowered output and no simulated result; what a second
/// owner <c>Link</c> does today; and what the printed and simulated <c>Create</c> do with a missing
/// singular navigation (commit 2 adds required to-one navigations, and reflective helpers pass null
/// for reference parameters). Enforcement of a second owner belongs to L-13 and OQ-14; these tests
/// only record what happens.
/// </summary>
public sealed class ExplicitInversePinsTests {
    private const string PairTemplate = """
        domain Watch
        Loan: entity {
          Code: Text
          __BORROWER__
          Draft: stage {
            Overdue: action { transition to Overdue }
          }
          Overdue: stage { }
        }
        Patron: entity {
          Flag: Text
          __LOANS__
          when any loans Overdue {
            assign Flag to "FIRED"
          }
        }
        """;

    private static string Pair(string borrower, string loans) =>
        PairTemplate.Replace("__BORROWER__", borrower).Replace("__LOANS__", loans);

    [Test]
    public async Task ExplicitViaPair_ChangesNoLoweredOutputOrSimResult() {
        var implicitPair = Pair("borrower: Patron optional", "loans: many Loan");
        var viaOnPatron = Pair("borrower: Patron optional", "loans: many Loan via borrower");
        var viaOnLoan = Pair("borrower: Patron via loans optional", "loans: many Loan");
        var viaBoth = Pair("borrower: Patron via loans optional", "loans: many Loan via borrower");

        var baseline = PrintedAndSimulated(implicitPair);
        await Assert.That(baseline.Printed).IsNotEmpty();
        await Assert.That(baseline.Simulated).IsEqualTo("Flag=FIRED;LoanStage=Overdue");
        foreach (var stated in new[] { viaOnPatron, viaOnLoan, viaBoth }) {
            var run = PrintedAndSimulated(stated);
            await Assert.That(run.Printed).IsEqualTo(baseline.Printed);
            await Assert.That(run.Simulated).IsEqualTo(baseline.Simulated);
        }
    }

    [Test]
    public async Task MigratedPair_SecondOwnerLink_OutcomeIsPinned() {
        // A loan linked to a second patron through the collection side. Pinned for the shape before
        // and after the back-navigation exists (both with and without via): the observed outcome is
        // the same, and nothing here enforces single ownership (L-13 / OQ-14).
        foreach (var dsl in new[] {
            Pair("borrower: Patron optional", "loans: many Loan"),
            Pair("borrower: Patron via loans optional", "loans: many Loan via borrower"),
        }) {
            var (domain, _, _) = Evolve(dsl);
            var exec = ModuleExec.For(domain);
            var loan = exec.Create("Loan", new Dictionary<string, object?> { ["Code"] = "L1" }).Instance!;
            var first = exec.Create("Patron", new Dictionary<string, object?> { ["Flag"] = "A" }).Instance!;
            var second = exec.Create("Patron", new Dictionary<string, object?> { ["Flag"] = "B" }).Instance!;
            await Assert.That(exec.Link("loans", first, loan).Succeeded).IsTrue();
            var again = exec.Link("loans", second, loan);
            await Assert.That(again.Succeeded).IsTrue();
            await Assert.That(again.Change).IsEqualTo(LinkChange.Linked);
        }
    }

    [Test]
    public async Task MigratedPair_GrepOwnedChild_SecondOwnerLink_OutcomeIsPinned() {
        // grep.poly's shape: Query.sources is `many owned Source`. Whether the owned child may gain a
        // second owner through Link is pinned for the document as shipped and with the plain singular
        // navigation back stated with via (the legal S-12 pair the guide allows).
        const string Shipped = """
            domain G
            Query: entity {
              Pat: Text
              Hits: Number default(0)
              sources: many owned Source
              when sources Scanned {
                assign Hits to Hits + 1
              }
            }
            Source: entity {
              Path: Text
              Unscanned: stage { Scan: action { transition to Scanned } }
              Scanned: stage { }
            }
            """;
        var withBack = Shipped
            .Replace("sources: many owned Source", "sources: many owned Source via query")
            .Replace("Path: Text", "Path: Text\n  query: Query via sources optional");
        var outcomes = new List<string>();
        foreach (var dsl in new[] { Shipped, withBack }) {
            var (domain, _, _) = Evolve(dsl);
            var exec = ModuleExec.For(domain);
            var source = exec.Create("Source", new Dictionary<string, object?> { ["Path"] = "a.txt" }).Instance!;
            var first = exec.Create("Query", new Dictionary<string, object?> { ["Pat"] = "x" }).Instance!;
            var second = exec.Create("Query", new Dictionary<string, object?> { ["Pat"] = "y" }).Instance!;
            var link1 = exec.Link("sources", first, source);
            var link2 = exec.Link("sources", second, source);
            outcomes.Add($"first={link1.Succeeded}/{link1.Change};second={link2.Succeeded}/{link2.Change}");
        }
        await Assert.That(outcomes[0]).IsEqualTo(outcomes[1]);
        await Assert.That(outcomes[0]).IsEqualTo("first=True/Linked;second=True/Linked");
    }

    [Test]
    public async Task Create_WithNullRequiredNavigation_FailsRequiredOnBothHosts() {
        // L-15a: a bare to-one is required, so a null borrower fails Required from the module.
        const string Dsl = """
            domain Watch
            Patron: entity {
              Flag: Text
              loans: many Loan
            }
            Loan: entity {
              Code: Text
              borrower: Patron
            }
            """;
        var (domain, analysis, session) = Evolve(Dsl);

        // Simulated host: creating a Loan without its borrower.
        var exec = ModuleExec.For(domain);
        var simulated = exec.Create("Loan", new Dictionary<string, object?> { ["Code"] = "L1" });
        await Assert.That(simulated.Succeeded).IsFalse();
        await Assert.That(simulated.Failure?.Kind).IsEqualTo(Poly.Runtime.DomainFailureKind.Required);
        await Assert.That(simulated.Failure?.ElementPath).IsEqualTo("Loan.borrower");
        await Assert.That(simulated.Failure?.Message).IsEqualTo("'borrower' is required.");

        // Printed host: the generated Create with a null borrower.
        var cs = new CSharpGenerator().Generate(session.Lower(domain, analysis));
        var asm = WhenAnySimulatePrintAgreeTests.CompileGenerated(cs, "NullNavPin");
        var printed = PrintedCreateFailure(asm, "Loan", ("code", "L1"));
        await Assert.That(printed?.Kind).IsEqualTo(Poly.Runtime.DomainFailureKind.Required);
        await Assert.That(printed?.ElementPath).IsEqualTo("Loan.borrower");
        await Assert.That(printed?.Message).IsEqualTo("'borrower' is required.");
    }

    [Test]
    public async Task Create_ActionBindingNullNav_FailsRequiredOnBothHosts() {
        // The binding is legal at analysis (it binds the navigation), but its value is null at run
        // time: the module's own Create fails it Required on both hosts.
        const string Dsl = """
            domain NullBind
            NullBindBook: entity { Title: Text }
            NullBindPatron: entity {
              Name: Text
              favorite: NullBindBook optional
              loans: many NullBindLoan
              Borrow: action { create in loans { book: favorite } }
            }
            NullBindLoan: entity {
              book: NullBindBook
              patron: NullBindPatron
            }
            """;
        var outcomes = await ParityScenario.FromDsl(Dsl, "NullBindPin").AssertAgree(side => {
            side.Create("NullBindPatron", ("Name", "p"));
            side.Invoke("Borrow");
        });
        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Required");
        await Assert.That(outcomes[1].FailurePath).IsEqualTo("NullBindPatron.Borrow.NullBindLoan.book");
    }

    /// <summary>Runs the printed <c>Create</c> by name and returns its failure, or null when it succeeded.</summary>
    private static Poly.Runtime.DomainFailure? PrintedCreateFailure(
        System.Reflection.Assembly asm, string typeName, params (string Name, object? Value)[] named) {
        var create = asm.GetType(typeName)!.GetMethods(PrintedMembers.Any)
            .Where(m => m.Name == "Create").OrderByDescending(m => m.GetParameters().Length).First();
        var args = create.GetParameters().Select(p =>
            named.FirstOrDefault(n => string.Equals(n.Name, p.Name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } hit
                ? hit.Value
                : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        var result = create.Invoke(null, args)!;
        return result.GetType().GetProperty("Error", PrintedMembers.Any)!.GetValue(result) as Poly.Runtime.DomainFailure;
    }

    // A child created holding its singular navigation is in the parent's inverse collection on both hosts:
    // the simulated side passes the navigation into Create, as the printed side does.
    [Test]
    public async Task InverseCollection_CreatedFromTheSingularSide_IsMaintainedOnBothHosts() {
        const string Dsl = """
            domain Watch
            Paper: entity {
              Title: Text
              trs: many Tr via Tracks
              A: stage {
                Advance: action { transition to B }
              }
              B: stage { }
            }
            Tr: entity {
              Tracks: Paper
              P: stage {
                when Tracks B {
                  transition to Q
                }
              }
              Q: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(Dsl, "InverseCollectionPin").AssertAgree(side => {
            var paper = side.Create("Paper", ("Title", "p"));
            _ = side.Create("Tr", ("Tracks", paper));
            side.Use(paper);
            side.Invoke("Advance");
        });
        await Assert.That(outcomes[2].State["trs"]).IsEqualTo("1");
    }

    private static (string Printed, string Simulated) PrintedAndSimulated(string dsl) {
        var (domain, analysis, session) = Evolve(dsl);
        var files = session.Emit(domain, analysis);
        var printed = string.Join("\n// ----\n", files.OrderBy(f => f.FileName, StringComparer.Ordinal)
            .Select(f => $"// {f.FileName}\n{f.Source}"));
        var exec = ModuleExec.For(domain);
        var loan = exec.Create("Loan", new Dictionary<string, object?> { ["Code"] = "L1" }).Instance!;
        var patron = exec.Create("Patron", new Dictionary<string, object?> { ["Flag"] = "NONE" }).Instance!;
        _ = exec.Link("loans", patron, loan);
        _ = exec.Invoke(loan, "Overdue");
        return (printed, $"Flag={patron.Bag["Flag"]};LoanStage={ModuleReads.Stage(loan)}");
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, [.. ExtensionCatalog.ProductAuthoring, "persistence"]);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}