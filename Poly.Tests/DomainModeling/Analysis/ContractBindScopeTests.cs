using Poly.Analysis;
using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// S-03 (#602, #603): the contract bind scope. A bind name is unique in its real scope, and
/// Lower honours one bind per action. Both refusals are analysis errors positioned on the
/// second declaration, naming the first: <c>DMCON017</c> for a reused bind name and
/// <c>DMCON018</c> for a second bind on an action Lower already spliced.
/// <para>
/// #602: two binds shared one name; diagnostics and binding facts keyed by that name were
/// ambiguous and the printer emitted both. #603: a second bind on one action was silently
/// dropped at Lower (its endpoint body never spliced). Both analyzed with no diagnostic.
/// </para>
/// </summary>
public sealed class ContractBindScopeTests {
    /// <summary>
    /// Applies without the analysis gate so the proposed root survives a failing analysis;
    /// the diagnostics then reference the nodes of that proposed tree.
    /// </summary>
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl) {
        var session = ExtensionCatalog.Core.Authoring;
        var applied = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl, session).Parse(), session: session);
        if (!applied.Succeeded)
            throw new InvalidOperationException(applied.FailureSummary ?? "mutation failed");
        var domain = applied.Root;
        return (domain, session.Analyze(domain));
    }

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    /// <summary>#602 verbatim: one contract, two endpoints, two actions, the same bind name twice.</summary>
    private const string DuplicateBindNameDsl = """
        domain Orders

        Billing: contract internal billing v1 {
          ChargeRequest: value {
            Amount: Number range(0, 1000)
          }
          Charge: outbound operation ChargeRequest
          Refund: outbound operation ChargeRequest
        }

        Order: entity {
          Paid: Boolean optional
          Pay: action (request: ChargeRequest) { assign Paid to true }
          Pay2: action (request: ChargeRequest) { assign Paid to true }
        }

        B1: bind Billing Charge to Pay request
        B1: bind Billing Refund to Pay2 request
        """;

    /// <summary>
    /// #603 shape, with a hand-authored <c>contract internal</c> in place of a library that
    /// contributes endpoint bodies: analysis alone sees the two binds on one action, so the
    /// rule is provable without a loaded package.
    /// </summary>
    private const string SecondBindOnSameActionDsl = """
        domain Orders

        Billing: contract internal billing v1 {
          SmallRequest: value {
            Amount: Number range(0, 50)
            Currency: Text length(3, 3)
          }
          Charge: outbound operation SmallRequest
          Refund: outbound operation SmallRequest
        }

        Order: entity {
          Total: Number optional
          Paid: Boolean optional
          Pay2: action (request: SmallRequest) { assign Paid to true }
        }

        ChargeOrder: bind Billing Charge to Pay2 request
        B2: bind Billing Refund to Pay2 request
        """;

    [Test]
    public async Task DuplicateBindName_IsErrorNamingBoth() {
        var (domain, analysis) = Analyze(DuplicateBindNameDsl);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ContractBindDuplicateName);
        await Assert.That(errors.Count).IsEqualTo(1);

        // Positioned on the second declaration, not the first.
        await Assert.That(ReferenceEquals(errors[0].Node, domain.ContractBindings[1])).IsTrue();
        await Assert.That(errors[0].Message)
            .Contains("'B1'")
            .And.Contains("Pay")
            .And.Contains("Pay2");
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ContractBindActionAlreadyBound).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task SecondBindOnSameAction_IsErrorNamingBoth() {
        var (domain, analysis) = Analyze(SecondBindOnSameActionDsl);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ContractBindActionAlreadyBound);
        await Assert.That(errors.Count).IsEqualTo(1);

        await Assert.That(ReferenceEquals(errors[0].Node, domain.ContractBindings[1])).IsTrue();
        await Assert.That(errors[0].Message)
            .Contains("Pay2")
            .And.Contains("ChargeOrder")
            .And.Contains("B2");
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ContractBindDuplicateName).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task DistinctBindNamesOnDistinctActions_AnalyzeClean() {
        var (_, analysis) = Analyze("""
            domain Orders

            Billing: contract internal billing v1 {
              ChargeRequest: value {
                Amount: Number range(0, 1000)
              }
              Charge: outbound operation ChargeRequest
              Refund: outbound operation ChargeRequest
            }

            Order: entity {
              Paid: Boolean optional
              Pay: action (request: ChargeRequest) { assign Paid to true }
              Pay2: action (request: ChargeRequest) { assign Paid to true }
            }

            ChargeOrder: bind Billing Charge to Pay request
            RefundOrder: bind Billing Refund to Pay2 request
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ContractBindDuplicateName).Count)
            .IsEqualTo(0);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ContractBindActionAlreadyBound).Count)
            .IsEqualTo(0);
    }

    /// <summary>
    /// The host half of "positioned": polyc maps the analysis node to a source span. A second
    /// bind on an action has a unique written path, so its span is the second declaration.
    /// </summary>
    [Test]
    public async Task Polyc_PlacesSecondBindOnSameActionOnTheSecondDeclaration() {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, "authoring");
        var result = compiler.Compile(SecondBindOnSameActionDsl, "bind-scope.poly");

        var errors = result.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.ContractBindActionAlreadyBound)
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Placed).IsTrue();
        await Assert.That(errors[0].File).IsEqualTo("bind-scope.poly");
        await Assert.That(errors[0].Line).IsEqualTo(19);
        await Assert.That(result.Succeeded).IsFalse();
    }
}