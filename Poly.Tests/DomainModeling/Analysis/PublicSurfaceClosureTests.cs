using Poly.Analysis;
using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #417 (epic #427, spike #413 decision 12): a public member is the exported contract,
/// so its signature must be closed. A parameter, result, or public value-type field
/// that names a type internal to the declaring domain is <c>DMCON002</c> and refuses
/// <c>Lower</c> through the existing <c>ThrowIfHasErrors</c> door.
/// <para>
/// #418: a public action must also be reachable through public. A public action on an
/// internal entity is <c>DMCON003</c>, because a public entity exports its name and its
/// public actions; the export never projects an action through an internal owner.
/// </para>
/// <para>
/// Not checked, by decision: a public entity's properties (decision 13 exports its name
/// and public actions only), an imported contract's ACL value types (that contract's
/// surface), and enum payloads (no visibility mark; #418 gives them a digest entry).
/// </para>
/// </summary>
public sealed class PublicSurfaceClosureTests {
    private static AnalysisResult Analyze(string dsl) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse())
            .Analysis;

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string dsl) {
        var session = ExtensionCatalog.Core.Authoring;
        var applied = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl, session).Parse(), session: session);
        if (!applied.Succeeded)
            throw new InvalidOperationException(applied.FailureSummary ?? "mutation failed");
        var domain = applied.Root;
        return (domain, session.Analyze(domain), session);
    }

    private const string InternalParameterDsl = """
        domain Test

        Money: value {
          Amount: Number
        }

        public entity Order {
          Total: Number
          public action Pay (bill: Money) {
          }
        }
        """;

    [Test]
    public async Task PublicAction_InternalValueTypeParameter_ReportsDmcon002() {
        var analysis = Analyze(InternalParameterDsl);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .Contains("Pay")
            .And.Contains("bill")
            .And.Contains("Money")
            .And.Contains("public");
    }

    [Test]
    public async Task PublicAction_InternalEntityResult_ReportsDmcon002() {
        var analysis = Analyze("""
            domain Test

            Order: entity {
              Draft: stage { }
            }

            public entity Customer {
              orders: many Order
              public action Place -> Order {
                create in orders { }
              }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .Contains("Place")
            .And.Contains("Order")
            .And.Contains("result");
    }

    [Test]
    public async Task PublicAction_PublicSignatureTypes_AnalyzesClean() {
        var analysis = Analyze("""
            domain Test

            public value Money {
              Amount: Number
            }

            public entity Order {
              Draft: stage { }
            }

            public entity Customer {
              orders: many Order
              public action Place (bill: Money) -> Order {
                create in orders { }
              }
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task PublicValueType_InternalFieldType_ReportsDmcon002() {
        var analysis = Analyze("""
            domain Test

            Inner: value {
              Amount: Number
            }

            public value Envelope {
              Body: Inner
            }

            Order: entity {
              Total: Number
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .Contains("Envelope")
            .And.Contains("Body")
            .And.Contains("Inner");
    }

    [Test]
    public async Task PublicValueType_PublicFields_AnalyzesClean() {
        var analysis = Analyze("""
            domain Test

            public value Inner {
              Amount: Number
            }

            public value Envelope {
              Body: Inner
              Note: Text
            }

            Order: entity {
              Total: Number
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task StageAction_InternalParameterType_ReportsDmcon002() {
        var analysis = Analyze("""
            domain Test

            Code: value {
              Value: Text
            }

            public entity Order {
              Open: stage {
                public action Close (code: Code) {
                }
              }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .Contains("Close")
            .And.Contains("code")
            .And.Contains("Code");
    }

    /// <summary>
    /// The Stripe shape: a used contract's value type is that contract's exported surface,
    /// not this domain's internal member, so a public bound action may name it.
    /// </summary>
    [Test]
    public async Task BoundContractAclPayload_OnPublicAction_AnalyzesClean() {
        var analysis = Analyze("""
            domain Test

            public entity Order {
              Total: Number
              public action Pay (request: ChargeRequest) {
              }
            }

            Stripe: contract external stripe v1 {
              ChargeRequest: value {
                Amount: Number
                Currency: Text
              }
              Charge: outbound operation ChargeRequest
            }

            ChargeOrder: bind Stripe Charge to Pay request
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    /// <summary>
    /// Enums carry no visibility mark (#416 left them unmarked), so a public signature
    /// treats them like a primitive. #418 decides whether they get a mark or a digest entry.
    /// </summary>
    [Test]
    public async Task EnumPayload_OnPublicAction_AnalyzesClean() {
        var analysis = Analyze("""
            domain Test

            Color: enum {
              Red,
              Green,
              Blue,
            }

            public entity Order {
              public action Paint (color: Color) {
              }
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task DomainWithoutPublicMembers_ReportsNoDmcon002() {
        var analysis = Analyze("""
            domain Test

            Money: value {
              Amount: Number
            }

            Order: entity {
              Total: Number
              Pay: action (bill: Money) {
              }
              Open: stage {
                Close: action (bill: Money) {
                }
              }
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task PublicAction_OnInternalEntity_ReportsDmcon003() {
        var analysis = Analyze("""
            domain Test

            Order: entity {
              public action Pay {
              }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.PublicMemberOnInternalOwner);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .Contains("Pay")
            .And.Contains("Order")
            .And.Contains("internal")
            .And.Contains("public");
    }

    [Test]
    public async Task PublicStageAction_OnInternalEntity_ReportsDmcon003() {
        var analysis = Analyze("""
            domain Test

            Order: entity {
              Open: stage {
                public action Close {
                }
              }
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.PublicMemberOnInternalOwner);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .Contains("Close")
            .And.Contains("Order");
    }

    /// <summary>
    /// Reachability is checked before signature closure: an action that is not on the
    /// surface at all reports one fix, not two.
    /// </summary>
    [Test]
    public async Task PublicAction_OnInternalEntity_ReportsOnlyDmcon003() {
        var analysis = Analyze("""
            domain Test

            Money: value {
              Amount: Number
            }

            Order: entity {
              public action Pay (bill: Money) {
              }
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicMemberOnInternalOwner).Count).IsEqualTo(1);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed)).IsEmpty();
    }

    [Test]
    public async Task PublicAction_OnPublicEntity_AnalyzesClean() {
        var analysis = Analyze("""
            domain Test

            public entity Order {
              Total: Number
              public action Pay {
              }
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicMemberOnInternalOwner)).IsEmpty();
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicSurfaceNotClosed)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task InternalAction_OnInternalEntity_AnalyzesClean() {
        var analysis = Analyze("""
            domain Test

            Order: entity {
              Total: Number
              Pay: action {
              }
              Open: stage {
                Close: action {
                }
              }
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.PublicMemberOnInternalOwner)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task PublicAction_OnInternalEntity_LowerThrows() {
        var (domain, analysis, session) = Evolve("""
            domain Test

            Order: entity {
              public action Pay {
              }
            }
            """);

        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(() => session.Lower(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Pay");
    }

    [Test]
    public async Task Polyc_ReportsDmcon003PlacedAtTheAction() {
        const string Dsl = """
            domain Test

            Order: entity {
              public action Pay {
              }
            }
            """;

        var compiler = new PolyCompiler(ProductCatalog.Catalog, "authoring");
        var result = compiler.Compile(Dsl, "reachable.poly");

        var errors = result.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.PublicMemberOnInternalOwner)
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Placed).IsTrue();
        await Assert.That(errors[0].File).IsEqualTo("reachable.poly");
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task PublicAction_InternalParameterType_LowerThrows() {
        var (domain, analysis, session) = Evolve(InternalParameterDsl);

        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(() => session.Lower(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Money");
    }

    [Test]
    public async Task PublicAction_AfterMarkingTypePublic_LowerSucceeds() {
        const string FixedDsl = """
            domain Test

            public value Money {
              Amount: Number
            }

            public entity Order {
              Total: Number
              public action Pay (bill: Money) {
              }
            }
            """;

        var (domain, analysis, session) = Evolve(FixedDsl);

        await Assert.That(analysis.HasErrors).IsFalse();
        var module = session.Lower(domain, analysis);
        await Assert.That(module.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Polyc_ReportsDmcon002PlacedAtTheMember() {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, "authoring");
        var result = compiler.Compile(InternalParameterDsl, "closed.poly");

        var errors = result.Diagnostics.Where(d => d.Code == DomainModelDiagnosticCodes.PublicSurfaceNotClosed).ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Placed).IsTrue();
        await Assert.That(errors[0].File).IsEqualTo("closed.poly");
        await Assert.That(result.Succeeded).IsFalse();
    }
}