using System.Text.RegularExpressions;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #545: a stage <c>require P</c> and an action <c>require P</c> are the same gate on the same
/// predicate, so the lowered body emits it once. The second copy was dead: the first guard
/// returned. An action policy with a different name is a different check and still prints.
/// </summary>
public class PolicyGuardEmissionTests {
    private const string StageAndAction = """
        domain C
        Item: entity { }
        Contract: entity {
          Items: many Item
          HasRoom: policy { count Items < 3 }
          Draft: stage require HasRoom {
            Claim: action require HasRoom { create in Items { } }
          }
        }
        """;

    private const string StageOnly = """
        domain C
        Item: entity { }
        Contract: entity {
          Items: many Item
          HasRoom: policy { count Items < 3 }
          Draft: stage require HasRoom {
            Claim: action { create in Items { } }
          }
        }
        """;

    private const string DifferentNames = """
        domain C
        Item: entity { }
        Contract: entity {
          Items: many Item
          HasRoom: policy { count Items < 3 }
          NotFull: policy { count Items < 9 }
          Draft: stage require HasRoom {
            Claim: action require NotFull { create in Items { } }
          }
        }
        """;

    private static string ClaimBody(string dsl) {
        var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
        var cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        return ExportedCSharp.ExtractMethod(cs, "private DomainResult __BodyOfClaim()");
    }

    private static int Occurrences(string text, string needle) =>
        Regex.Matches(text, Regex.Escape(needle)).Count;

    [Test]
    public async Task StageAndActionRequireSamePolicy_BodyChecksItOnce() {
        var body = ClaimBody(StageAndAction);

        await Assert.That(Occurrences(body, "blocked by policy 'HasRoom'.\"")).IsEqualTo(1);
    }

    // The gate before the body is one check; the stage requirement's re-check after the body (#518/#577) is a
    // different check — the body must keep the invariant — and carries its own message suffix.
    private const string PostCheck = "It would leave stage 'Draft' invalid.";

    [Test]
    public async Task StageAndActionRequireSamePolicy_KeepsTheAfterBodyRecheck() {
        var body = ClaimBody(StageAndAction);

        await Assert.That(Occurrences(body, PostCheck)).IsEqualTo(1);
        await Assert.That(Occurrences(body, "!this.HasRoom()")).IsEqualTo(2);
    }

    [Test]
    public async Task StageRequireAlone_BodyChecksItOnce() {
        var body = ClaimBody(StageOnly);

        await Assert.That(Occurrences(body, "blocked by policy 'HasRoom'.\"")).IsEqualTo(1);
        await Assert.That(Occurrences(body, PostCheck)).IsEqualTo(1);
    }

    [Test]
    public async Task ActionPolicyWithADifferentName_StillChecksBoth() {
        var body = ClaimBody(DifferentNames);

        await Assert.That(Occurrences(body, "blocked by policy 'HasRoom'.\"")).IsEqualTo(1);
        await Assert.That(Occurrences(body, "!this.NotFull()")).IsEqualTo(1);
    }

    // Removing the duplicate guard leaves the verdict, the failure and the state identical on both hosts.
    [Test]
    public async Task StageAndActionRequireSamePolicy_ParityIsUnchanged() {
        var scenario = ParityScenario.FromDsl(StageAndAction, "PolicyGuardEmission", recordGraph: true);

        var (simulate, printed) = scenario.Run(side => {
            side.Create("Contract");
            side.Invoke("Claim");
            side.Invoke("Claim");
            side.Invoke("Claim");
            side.Invoke("Claim");
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        // The third claim would fill Items to 3 and leave Draft invalid, so the stage requirement's re-check
        // after the body refuses it and rolls it back (#518/#577); the fourth starts from the same two items.
        await Assert.That(string.Join(",", simulate.Select(o => o.Success)))
            .IsEqualTo("True,True,True,False,False");
        await Assert.That(simulate[3].FailureGuard).IsEqualTo("HasRoom");
        await Assert.That(printed[3].FailureGuard).IsEqualTo("HasRoom");
        await Assert.That(simulate[4].FailureGuard).IsEqualTo("HasRoom");
        await Assert.That(printed[4].FailureGuard).IsEqualTo("HasRoom");
        await Assert.That(simulate[4].FailurePath).IsEqualTo("Contract.Claim");
        await Assert.That(printed[4].FailurePath).IsEqualTo("Contract.Claim");
    }
}