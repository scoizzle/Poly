using Poly.DomainModeling.Lowering;
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
        Basket: entity {
          Items: Number range(0, 3) default(3)
          AddItem: action {
            assign Items to Items + 1
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
    public async Task Create_WhenEntryEffectBreaksRange_FailsTheSameWay() {
        var outcomes = await ParityScenario.FromDsl(CreateFailDsl, "ParityCreateFail")
            .AssertAgree(side => side.Create("Widget"));
        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).Contains("must be <= 10");
    }

    [Test]
    public async Task Invoke_WhenAssignBreaksRange_FailsTheSameWay() {
        var outcomes = await ParityScenario.FromDsl(ActionFailDsl, "ParityActionFail")
            .AssertAgree(side => {
                side.Create("Basket");
                side.Invoke("AddItem");
            });
        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].State["Items"]).IsEqualTo("3");
    }

    [Test]
    public async Task Invoke_WhenActionTransitions_AgreesOnStageAndProperties() {
        var outcomes = await ParityScenario.FromDsl(StageDsl, "ParityStage")
            .AssertAgree(side => {
                side.Create("Ticket");
                side.Invoke("Close");
            });
        await Assert.That(outcomes[0].State["Stage"]).IsEqualTo("Open");
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("Closed");
        await Assert.That(outcomes[1].State["Note"]).IsEqualTo("closed");
    }

    [Test]
    public async Task EvaluatePolicy_HasOverdueLoans_AgreesForLinkedLoans() {
        var outcomes = await ParityScenario.FromDsl(PolicyDsl, "ParityPolicy")
            .AssertAgree(side => {
                var overdue = side.Create("Loan", ("Status", "Overdue"));
                var active = side.Create("Loan", ("Status", "Active"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-overdue@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { overdue, active }));
                side.EvaluatePolicy("HasOverdueLoans");

                var returned = side.Create("Loan", ("Status", "Returned"));
                side.Create("Patron", ("Name", "Ada"), ("Email", "ada-clear@lib.test"), ("MaxItems", 5L),
                    ("loans", new[] { returned }));
                side.EvaluatePolicy("HasOverdueLoans");
            });
        await Assert.That(outcomes[3].State["HasOverdueLoans"]).IsEqualTo("True");
        await Assert.That(outcomes[6].State["HasOverdueLoans"]).IsEqualTo("False");
    }

    [Test]
    public async Task Invoke_CreateInAction_AgreesOnLinkedChildren() {
        var outcomes = await ParityScenario.FromDsl(LinkDsl, "ParityLink")
            .AssertAgree(side => {
                side.Create("Parent", ("Name", "Pat"));
                side.Invoke("AddKid");
            });
        await Assert.That(outcomes[0].State["kids"]).IsEqualTo("0");
        await Assert.That(outcomes[1].State["kids"]).IsEqualTo("1");
    }

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
        var (domain, analysis) = EvolvedDomain.FromDsl(poly);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        // CompileAndLoad throws with the compiler errors when the printed code does not compile.
        await Assert.That(ExportedCSharp.CompileAndLoad(cs, "Print_" + Path.GetFileNameWithoutExtension(relativePath))).IsNotNull();
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
