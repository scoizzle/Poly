using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class ParityTests {
    const string CreateFailDsl = """
        domain Lab
        Widget: entity {
          Score: Number range(1, 10) default(5)
          Bump: Number default(100)
          Draft: stage {
            entry { assign Score to Bump }
          }
        }
        """;

    const string ActionFailDsl = """
        domain Shop
        Item: entity {
          Ready: Boolean default(false)
          CanPromote: policy { Ready is true }
          Promote: action require CanPromote {
            assign Ready to true
          }
        }
        """;

    const string StageDsl = """
        domain Desk
        Ticket: entity {
          Note: Text default("open")
          Open: stage {
            Close: action {
              assign Note to "closed"
              transition to Closed
            }
          }
          Closed: stage { }
        }
        """;

    const string PolicyDsl = """
        domain Library
        Patron: entity {
          Name: Text required
          Email: Text unique
          MaxItems: Number range(0, 20) required
          loans: many Loan
          HasOverdueLoans: policy { any loans where Status is "Overdue" }
        }
        Loan: entity {
          Status: Text
        }
        """;

    const string LinkDsl = """
        domain Shop
        Kid: entity {
          Name: Text
        }
        Parent: entity {
          Name: Text
          kids: many Kid
          AddKid: action { create in kids { Name: "x" } }
        }
        """;

    [Test]
    public async Task Create_OnEntryRangeViolation_FailsWithSameMessage() {
        var scenario = ParityScenario.FromDsl(CreateFailDsl, "ParityCreateFail");
        var step = scenario.Create("Widget");
        await step.AssertAgree();
        await Assert.That(step.Simulate.Success).IsFalse();
        await Assert.That(step.Simulate.FailureMessage).Contains("must be <= 10");
    }

    [Test]
    public async Task Promote_WhenGuardUnmet_FailsWithSameMessageAndLeavesProperties() {
        var scenario = ParityScenario.FromDsl(ActionFailDsl, "ParityActionFail");
        var created = scenario.Create("Item", ("Ready", false));
        await created.AssertAgree();
        var step = scenario.Invoke("Promote");
        await step.AssertAgree();
        await Assert.That(step.Simulate.Success).IsFalse();
        await Assert.That(step.Simulate.FailureMessage).Contains("CanPromote");
        await Assert.That((bool)step.Simulate.Properties["Ready"]!).IsFalse();
    }

    [Test]
    public async Task Close_TransitionsStage_AgreesOnStageAndProperties() {
        var scenario = ParityScenario.FromDsl(StageDsl, "ParityStage");
        var created = scenario.Create("Ticket");
        await created.AssertAgree();
        await Assert.That(created.Simulate.Stage).IsEqualTo("Open");
        var step = scenario.Invoke("Close");
        await step.AssertAgree();
        await Assert.That(step.Simulate.Success).IsTrue();
        await Assert.That(step.Simulate.Stage).IsEqualTo("Closed");
        await Assert.That(step.Simulate.Properties["Note"]).IsEqualTo("closed");
    }

    [Test]
    public async Task Patron_HasOverdueLoans_AgreesOnSimulateAndPrinted() {
        var scenario = ParityScenario.FromDsl(PolicyDsl, "ParityPolicy");
        var overdue = scenario.Create("Loan", ("Status", "Overdue"));
        await overdue.AssertAgree();
        var active = scenario.Create("Loan", ("Status", "Active"));
        await active.AssertAgree();
        var withOverdue = scenario.Create("Patron",
            ("Name", "Ada"), ("Email", "ada-overdue@lib.test"), ("MaxItems", 5L),
            ("loans", new[] { overdue.Entity!, active.Entity! }));
        await withOverdue.AssertAgree();
        var held = scenario.EvaluatePolicy("HasOverdueLoans");
        await held.AssertAgree();
        await Assert.That((bool)held.Simulate.Properties["HasOverdueLoans"]!).IsTrue();

        var returned = scenario.Create("Loan", ("Status", "Returned"));
        await returned.AssertAgree();
        var clearLoan = scenario.Create("Loan", ("Status", "Active"));
        await clearLoan.AssertAgree();
        var withoutOverdue = scenario.Create("Patron",
            ("Name", "Ada"), ("Email", "ada-clear@lib.test"), ("MaxItems", 5L),
            ("loans", new[] { returned.Entity!, clearLoan.Entity! }));
        await withoutOverdue.AssertAgree();
        var clear = scenario.EvaluatePolicy("HasOverdueLoans");
        await clear.AssertAgree();
        await Assert.That((bool)clear.Simulate.Properties["HasOverdueLoans"]!).IsFalse();
    }

    [Test]
    public async Task Parent_CreateInChild_AgreesOnChildCount() {
        var scenario = ParityScenario.FromDsl(LinkDsl, "ParityLink");
        var created = scenario.Create("Parent", ("Name", "Pat"));
        await created.AssertAgree();
        await Assert.That(created.Simulate.Properties["kids"]).IsEqualTo(0);
        var step = scenario.Invoke("AddKid");
        await step.AssertAgree();
        await Assert.That(step.Simulate.Success).IsTrue();
        await Assert.That(step.Simulate.Properties["kids"]).IsEqualTo(1);
    }

    // B1 owns these printed-C# compile failures (remove an entry when that domain compiles).
    static readonly IReadOnlyDictionary<string, string> KnownGaps = new Dictionary<string, string>(StringComparer.Ordinal);

    [Test]
    [Arguments("docs/probes/fleet-eval/09-transport/warehouse.poly")]
    [Arguments("docs/probes/fleet-eval/09-transport/orders.poly")]
    [Arguments("docs/probes/fleet-eval/09-transport/clinic.poly")]
    [Arguments("docs/probes/fleet-eval/12-mcp/mcp-library.poly")]
    [Arguments("docs/probes/dogfood/university.poly")]
    [Arguments("docs/probes/dogfood/crm.poly")]
    [Arguments("docs/probes/dogfood/hotel.poly")]
    [Arguments("docs/probes/dogfood/simulate-create-type.poly")]
    [Arguments("docs/probes/dogfood/simulate-create-in.poly")]
    [Arguments("docs/probes/dogfood/simulate-create-create-in.poly")]
    public async Task PrintedCSharpCompiles(string relativePath) {
        var poly = await File.ReadAllTextAsync(Path.Combine(FindRepoRoot(), relativePath));
        Domain domain;
        AnalysisResult analysis;
        try {
            (domain, analysis) = EvolvedDomain.FromDsl(poly);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Parse/analyze failed for '{relativePath}': {ex.Message}", ex);
        }
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        string? error = null;
        try {
            ExportedCSharp.CompileAndLoad(cs, "Print_" + Path.GetFileNameWithoutExtension(relativePath));
        }
        catch (InvalidOperationException ex) {
            error = ex.Message.Split('\n')[0];
        }
        if (KnownGaps.TryGetValue(relativePath, out var reason)) {
            await Assert.That(error).IsNotNull()
                .Because($"KnownGaps '{relativePath}' ({reason}) must still fail to compile until B1 fixes it");
        }
        else {
            await Assert.That(error).IsNull()
                .Because($"printed C# for '{relativePath}' must compile: {error}");
        }
    }

    static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}
