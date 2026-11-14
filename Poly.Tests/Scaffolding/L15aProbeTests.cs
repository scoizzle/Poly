using Poly.Tests.TestHelpers;

namespace Poly.Tests.Scaffolding;

// L-15a step-0 probes (scaffolding; deleted in the slice's last commit).
public sealed class L15aProbeTests {
    // P3: `create in` inside a `when` block runs on both hosts and fills the auto-wired back-reference.
    [Test]
    public async Task P3_CreateInInsideWhen_RunsOnBothHosts() {
        const string Dsl = """
            domain ProbeWhen
            Patron: entity {
              Owed: Number default(0)
              loans: many Loan via borrower
              fines: many Fine via patron
              when loans Overdue {
                create in fines { Amount: 5 }
                assign Owed to Owed + 5
              }
            }
            Loan: entity {
              Code: Text
              borrower: Patron
              Draft: stage { Lapse: action { transition to Overdue } }
              Overdue: stage { }
            }
            Fine: entity {
              Amount: Number
              patron: Patron
            }
            """;
        var outcomes = await ParityScenario.FromDsl(Dsl, "L15aProbeP3").AssertAgree(side => {
            var patron = side.Create("Patron");
            var loan = side.Create("Loan", ("Code", "L1"), ("borrower", patron));
            side.Use(loan);
            side.Invoke("Lapse");
            side.Use(patron);
            side.UseLinked("fines");
        });
        Console.WriteLine("P3: " + string.Join(" | ", outcomes.Select(o =>
            $"{o.Step} ok={o.Success} msg={o.Message} state={string.Join(",", o.State.Select(kv => kv.Key + "=" + kv.Value))}")));
        await Assert.That(outcomes[4].State["fines"]).IsEqualTo("1");
    }

    // P1: every shipped document's new errors (DMEFF011 on a navigation, DMREL006, an owned bare part,
    // a navigation key), with the message, and the sweep rows that changed.
    [Test]
    public async Task P1_ShippedDocumentsUnderTheRules() {
        var root = Poly.Tests.Docs.GuideBlocks.RepoRoot();
        foreach (var file in Poly.Tests.DomainModeling.Analysis.CorpusVerdictSweepTests.PolyFiles()) {
            var result = Poly.Tests.Docs.GuideBlocks.Analyze(File.ReadAllText(Path.Combine(root, file)));
            foreach (var d in result.Diagnostics.Where(d => d.IsError))
                if (d.Message.Contains("navigation", StringComparison.Ordinal) || d.Message.Contains("owned single part", StringComparison.Ordinal)
                    || d.Code == "DMREL006")
                    Console.WriteLine($"P1 {file} {d.Code} {d.Message}");
        }
        Console.WriteLine("P1SWEEP\n" + Poly.Tests.DomainModeling.Analysis.CorpusVerdictSweepTests.SweepRows());
        await Assert.That(root).IsNotEmpty();
    }

    // P4: can MCP create_instance pass a navigation's instance id?
    [Test]
    public async Task P4_McpCreateInstance_WithNavigationId() {
        var (sessionId, _) = McpTestTools.Store.Create("ProbeNav");
        var dsl = McpTestTools.Dsl.ApplyDsl(sessionId, """
            domain ProbeNav
            Owner: entity { Name: Text }
            Pet: entity {
              Tag: Text
              owner: Owner
            }
            """);
        await Assert.That(dsl.Success).IsTrue();
        var owner = McpTestTools.Runtime.CreateInstance(sessionId, "Owner", """{"Name":"o"}""");
        var ownerJson = System.Text.Json.JsonSerializer.Serialize(owner.Data);
        var id = System.Text.RegularExpressions.Regex.Match(ownerJson, "\"instanceId\":\"([^\"]+)\"").Groups[1].Value;
        var pet = McpTestTools.Runtime.CreateInstance(sessionId, "Pet", "{\"Tag\":\"t\",\"owner\":\"" + id + "\"}");
        Console.WriteLine($"P4: ownerId={id} petSuccess={pet.Success} msg={pet.Message}");
        var bare = McpTestTools.Runtime.CreateInstance(sessionId, "Pet", """{"Tag":"t"}""");
        Console.WriteLine($"P4: bare petSuccess={bare.Success} msg={bare.Message}");
        await Assert.That(id).IsNotEmpty();
    }
}