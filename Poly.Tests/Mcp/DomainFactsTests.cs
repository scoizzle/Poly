using System.Text.Json;

using Poly.Mcp.Tools;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// #367 C-5: get_domain_analysis surfaces the facts the analysis already proves — per-action stage
/// invariants (narrowed ranges, merged constraints, postconditions) and guard-closed properties.
/// Read-only exposure: nothing here may move a verdict, diagnostic or the contract fingerprint.
/// </summary>
public class DomainFactsTests {
    private const string Source = """
        domain Demo

        Item: entity {
          Qty: Number range(0, 100) default(0)
          Floor: Number range(0, 100) default(0)
          Big: policy { Qty >= 10 }
          Ordered: policy { Floor < Qty }
          Run: action
            require Big
            require Ordered
          {
            assign Qty to 50
          }
        }
        """;

    private static AnalysisData Analyze(string name) {
        var (sessionId, _) = McpTestTools.Store.Create(name);
        var dsl = McpTestTools.Dsl.ApplyDsl(sessionId, Source);
        if (!dsl.Success)
            throw new InvalidOperationException(string.Join("; ", (dsl.Diagnostics ?? []).Concat([dsl.Message])));
        var response = McpTestTools.Query.GetDomainAnalysis(sessionId);
        return (AnalysisData)response.Data!;
    }

    [Test]
    public async Task GetDomainAnalysis_Serialized_CarriesActionInvariantAndGuardClosedFacts() {
        var json = JsonSerializer.Serialize(Analyze("Facts"));

        await Assert.That(json).Contains("\"actionInvariants\":[");
        await Assert.That(json).Contains("\"guardClosedProperties\":[");
    }

    [Test]
    public async Task ActionInvariants_NameNarrowedRangeMergedConstraintsAndRelation() {
        var data = Analyze("Shape");

        await Assert.That(data.ActionInvariants).IsNotNull();
        var run = data.ActionInvariants!.Single(a => a.EntityName == "Item" && a.ActionName == "Run");
        await Assert.That(run.StageName).IsNull();
        await Assert.That(run.Preconditions).IsEquivalentTo(new[] { "Big", "Ordered" });

        var qty = run.Properties.Single(p => p.Name == "Qty");
        await Assert.That(qty.Min).IsEqualTo(10m);
        await Assert.That(qty.Max).IsEqualTo(100m);
        await Assert.That(run.Properties.Single(p => p.Name == "Floor").Constraints.Any(c => c.Contains("Qty"))).IsTrue();

        var post = run.Postconditions.Single(p => p.TargetProperty == "Qty");
        await Assert.That(post.DeclaringAction).IsEqualTo("Run");
        await Assert.That(post.Min).IsEqualTo(50m);
        await Assert.That(post.Max).IsEqualTo(50m);
    }

    [Test]
    public async Task GuardClosedProperties_ListsProvenClosedPropertiesPerEntity() {
        var data = Analyze("Closed");

        await Assert.That(data.GuardClosedProperties).IsNotNull();
        var item = data.GuardClosedProperties!.Single(g => g.EntityName == "Item");
        await Assert.That(item.PropertyNames).Contains("Qty");
        await Assert.That(item.PropertyNames.SequenceEqual(item.PropertyNames.Order(StringComparer.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Facts_AreDeterministic_AndDoNotMoveDiagnostics() {
        var first = Analyze("DetA");
        var second = Analyze("DetB");

        await Assert.That(JsonSerializer.Serialize(second)).IsEqualTo(JsonSerializer.Serialize(first));
        await Assert.That(JsonSerializer.Serialize(first.DiagnosticFacts)).IsEqualTo(JsonSerializer.Serialize(second.DiagnosticFacts));
    }

    [Test]
    public async Task ReadingFacts_LeavesAnalysisRevisionAndDiagnosticsByteIdentical() {
        var (sessionId, _) = McpTestTools.Store.Create("ReadOnly");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Source).Success).IsTrue();
        McpTestTools.Store.TryGet(sessionId, out var before);
        var diagnosticsBefore = JsonSerializer.Serialize(before.LatestAnalysis!.Diagnostics.Select(d => new { d.Severity, d.Code, d.Message }));
        var analysisBefore = before.LatestAnalysis;

        _ = McpTestTools.Query.GetDomainAnalysis(sessionId);
        _ = McpTestTools.Query.GetDomainAnalysis(sessionId);

        McpTestTools.Store.TryGet(sessionId, out var after);
        await Assert.That(after.Revision).IsEqualTo(before.Revision);
        await Assert.That(ReferenceEquals(after.LatestAnalysis, analysisBefore)).IsTrue();
        await Assert.That(JsonSerializer.Serialize(after.LatestAnalysis!.Diagnostics.Select(d => new { d.Severity, d.Code, d.Message })))
            .IsEqualTo(diagnosticsBefore);
    }

    [Test]
    public async Task EntitiesWithoutActions_ProduceNoInvariantFacts() {
        var (sessionId, _) = McpTestTools.Store.Create("NoActions");
        var dsl = McpTestTools.Dsl.ApplyDsl(sessionId, """
            domain Plain

            Item: entity {
              Qty: Number range(0, 10)
            }
            """);
        await Assert.That(dsl.Success).IsTrue();

        var data = (AnalysisData)McpTestTools.Query.GetDomainAnalysis(sessionId).Data!;

        await Assert.That(data.ActionInvariants).IsNull();
    }
}