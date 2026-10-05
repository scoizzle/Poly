using Poly.DomainModeling;
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

    // Text + Number is invalid: Analyze rejects it, so the DSL cannot author it (TextConcatAnalysisTests).
    // A domain edited through the API skips Analyze and the sides still differ: the simulator refuses the
    // action while the printed C# concatenates. The G1 gate makes this unreachable; the row pins today's behaviour.
    [Test]
    [Arguments("Status", "Count", "paid3")]
    [Arguments("Count", "Status", "3paid")]
    public async Task KnownGap_TextWithNumberOnApiBuiltDomain_SimulateRefusesAndPrintedConcatenates(string left, string right, string printedCode) {
        var domain = EvolvedDomain.FromDsl(Dsl("Status + Suffix")).Domain;
        var entity = domain.Types.OfType<Entity>().Single();
        var action = entity.Actions.Single();
        var assign = (AssignEffect)action.Effects.Single();
        var edited = assign with { Value = DomainExpression.Add(DomainExpression.Property(left), DomainExpression.Property(right)) };
        entity = entity with { Actions = [action with { Effects = [edited] }] };
        domain = domain with { Types = [.. domain.Types.Select(t => t is Entity ? entity : t)] };

        var (simulate, printed) = ParityScenario.FromDomain(domain, "ParityConcatMixed" + left).Run(CreateAndTag);

        await Assert.That(simulate[1].Success).IsFalse();
        await Assert.That(simulate[1].Message).StartsWith("VM compile rejected: arithmetic operand is not numeric");
        await Assert.That(printed[1].Success).IsTrue();
        await Assert.That(printed[1].State["Code"]).IsEqualTo(printedCode);
    }
}
