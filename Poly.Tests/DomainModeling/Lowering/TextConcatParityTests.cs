using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Runtime;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class TextConcatParityTests {
    static string Dsl(string expression, string target = "Code") => $$"""
        domain Cat
        Item: entity {
          Status: Text
          Suffix: Text
          Count: Number
          Total: Number
          Code: Text
          Tag: action { assign {{target}} to {{expression}} }
        }
        """;

    static void CreateAndTag(ParitySide side) {
        side.Create("Item", ("Status", "paid"), ("Suffix", "x"), ("Count", 3L), ("Total", 1L), ("Code", ""));
        side.Invoke("Tag");
    }

    [Test]
    [Arguments("\"S\" + Status", "Spaid")]
    [Arguments("Status + Suffix", "paidx")]
    [Arguments("Status + \"-\" + Suffix", "paid-x")]
    public async Task Invoke_WhenTextPlusText_BothSidesConcatenate(string expression, string expected) {
        var outcomes = await ParityScenario.FromDsl(Dsl(expression), "ParityConcat")
            .AssertAgree(CreateAndTag);
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["Code"]).IsEqualTo(expected);
    }

    [Test]
    public async Task Invoke_WhenNumberPlusNumber_BothSidesAdd() {
        var outcomes = await ParityScenario.FromDsl(Dsl("Count + Total", "Total"), "ParityConcatNumbers")
            .AssertAgree(CreateAndTag);
        await Assert.That(outcomes[1].State["Total"]).IsEqualTo("4");
    }

    // Text + Number is invalid: Analyze rejects it (TextConcatAnalysisTests). With G1, Export / FromDomain
    // refuse an analysis with Errors, so neither simulate nor printed C# can run the mixed assign.
    [Test]
    [Arguments("Status", "Count")]
    [Arguments("Count", "Status")]
    public async Task TextWithNumberOnApiBuiltDomain_BothSidesRefuse(string left, string right) {
        var domain = EvolvedDomain.FromDsl(Dsl("Status + Suffix")).Domain;
        var entity = domain.Types.OfType<Entity>().Single();
        var action = entity.Actions.Single();
        var assign = (AssignEffect)action.Effects.Single();
        var edited = assign with { Value = DomainExpression.Add(DomainExpression.Property(left), DomainExpression.Property(right)) };
        entity = entity with { Actions = [action with { Effects = [edited] }] };
        domain = domain with { Types = [.. domain.Types.Select(t => t is Entity ? entity : t)] };

        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.HasErrors).IsTrue();
        var first = analysis.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message;

        var exportEx = Assert.Throws<InvalidOperationException>(
            () => new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(exportEx!.Message).IsEqualTo(first);

        var entityForCreate = domain.Types.OfType<Entity>().Single();
        var createEx = Assert.Throws<InvalidOperationException>(
            () => DomainEntityInstance.Create(entityForCreate, null, domain));
        await Assert.That(createEx!.Message).IsEqualTo(first);
    }
}
