using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// pack-3b-2: a loaded <see cref="Domain"/> in a <see cref="DomainSuite"/> resolves the
/// <see cref="ImportedContract.SourceIdentifier"/> of <c>contract internal billing</c> and
/// fills the declared empty/partial contract. Hand-authored body survives; the
/// clash/leak rules in <see cref="ContractIntegrationAnalyzer"/> still apply after fill.
/// </summary>
public sealed class InternalDomainResolutionTests {
    private static Domain BillingSource() =>
        DomainFactory.Create("billing", b => b
            .AddValueType("ChargeRequest",
                new Property("Amount", new DomainTypeReference("Number"), []),
                new Property("Currency", new DomainTypeReference("Text"), []))
            .AddEntity("Ledger")
            .AddActionWithParameters("Ledger", "Charge",
                new Property("request", new DomainTypeReference("ChargeRequest"), [])));

    private static Domain ParseParent(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded) {
            throw new InvalidOperationException(
                "Test parent DSL failed to apply: " +
                string.Join("; ", result.Analysis.Diagnostics.Select(d => d.Message)));
        }
        return result.Root;
    }

    private static DomainSuite SuiteWith(Domain parent) => new([BillingSource(), parent]);

    [Test]
    public async Task FillInternalContracts_EmptyContract_ResolvesChargeRequestFromLoadedDomain() {
        var parent = ParseParent("""
            domain Parent
            Billing: contract internal billing v1 {}
            """);

        var filled = SuiteWith(parent).FillInternalContracts(parent);
        var contract = filled.ImportedContracts.Single(c => c.Name == "Billing");

        await Assert.That(contract.Types.Select(t => t.Name)).Contains("ChargeRequest");
        await Assert.That(contract.Endpoints.Select(e => e.Name)).Contains("Charge");
        await Assert.That(contract.Name).IsEqualTo("Billing");
        await Assert.That(contract.SourceIdentifier).IsEqualTo("billing");
        await Assert.That(contract.Version).IsEqualTo("v1");
    }

    [Test]
    public async Task FillInternalContracts_HandAuthoredBody_IsPreservedAndFilled() {
        var parent = ParseParent("""
            domain Parent
            Order: entity {}
            Billing: contract internal billing v1 {
              ChargeRequest: value {
                Amount: Number
                Currency: Text
              }
            }
            """);

        var filled = SuiteWith(parent).FillInternalContracts(parent);
        var contract = filled.ImportedContracts.Single(c => c.Name == "Billing");

        // Hand-authored value type survives exactly once (producer does not duplicate it).
        await Assert.That(contract.Types.Count(t => t.Name == "ChargeRequest")).IsEqualTo(1);
        // Producer filled the missing endpoint surface.
        await Assert.That(contract.Endpoints.Select(e => e.Name)).Contains("Charge");
        // Hand-authored version preserved.
        await Assert.That(contract.Version).IsEqualTo("v1");
    }

    [Test]
    public async Task FillInternalContracts_UnresolvedSourceIdentifier_Throws() {
        var parent = ParseParent("""
            domain Parent
            Billing: contract internal nosuchdomain v1 {}
            """);

        await Assert.That(() => SuiteWith(parent).FillInternalContracts(parent))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("nosuchdomain");
    }

    [Test]
    public async Task FillInternalContracts_FilledDomain_StillFailsClashAndLeakAnalysis() {
        var parent = ParseParent("""
            domain Parent
            Billing: contract internal billing v1 {}
            ChargeRequest: value {
              Amount: Number
              Currency: Text
            }
            Order: entity {
              Total: ChargeRequest
            }
            """);

        var filled = SuiteWith(parent).FillInternalContracts(parent);
        var analysis = DomainModelAnalyzer.Analyze(filled);

        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToList();
        // Filled contract value type clashes with the parent's own value type.
        await Assert.That(errors.Any(m => m.Contains("clashes with"))).IsTrue();
        // Stored property leak onto a contract value type is still rejected.
        await Assert.That(errors.Any(m => m.Contains("Stored state must use parent-domain types"))).IsTrue();
    }
}