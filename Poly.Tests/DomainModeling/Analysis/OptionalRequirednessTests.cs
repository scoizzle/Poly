using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Requiredness is one published fact: every entity property is required at create
/// unless it carries <c>optional</c>. <c>default(...)</c> and the first stage's entry
/// assignment still supply a value, so they are not missing-create errors.
/// </summary>
public class OptionalRequirednessTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    /// <summary>Evolves a domain that is EXPECTED to fail analysis; returns its error diagnostics.</summary>
    private static IReadOnlyList<Diagnostic> EvolveExpectingErrorDiagnostics(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (result.Succeeded)
            throw new InvalidOperationException("Expected evolution to fail, but it succeeded.");
        return result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    /// <summary>Evolves a domain that is EXPECTED to fail analysis; returns the
    /// concatenated error messages.</summary>
    private static string EvolveExpectingError(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (result.Succeeded)
            throw new InvalidOperationException("Expected evolution to fail, but it succeeded.");
        return string.Join("; ",
            result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
    }

    [Test]
    public async Task Create_BareCreate_OmittingRequiredProperty_ReportsDMEFF011() {
        // The bare `create X { }` path shares the required-coverage check with
        // `create in`; a property without `optional` must be supplied here too.
        var errors = EvolveExpectingErrorDiagnostics("""
            domain CreateBareDirect
            Token: entity { Lexeme: Text }
            Box: entity {
              Make: action -> Token {
                create Token { }
              }
            }
            """);
        await Assert.That(errors.Any(d =>
            d.Code == DomainModelDiagnosticCodes.CreateMissingRequiredProperty
            && d.Message.Contains("required property 'Lexeme'", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Create_OmittingBareProperty_ReportsDMEFF011() {
        // A property with no `optional` is required: a create that omits it must
        // fail analysis, not only at runtime.
        var message = EvolveExpectingError("""
            domain CreateBare
            Token: entity { Lexeme: Text }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { }
              }
            }
            """);
        await Assert.That(message).Contains("required property 'Lexeme'");
    }

    [Test]
    public async Task Parse_RequiredWord_IsRejected() {
        var message = Assert.Throws<FormatException>(() => Evolve("""
            domain Both
            Token: entity { Lexeme: Text required optional }
            """));
        await Assert.That(message!.Message).Contains("'required' is not a constraint");
    }

    [Test]
    public async Task Analyze_RequiredWithDefault_MayBeOmitted() {
        var (domain, analysis) = Evolve("""
            domain CreateDefault
            Token: entity { Lexeme: Text default("let") }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { }
              }
            }
            """);
        var token = domain.Types.OfType<Entity>().Single(e => e.Name == "Token");
        await Assert.That(analysis.GetStructure(token)!.RequiredPropertyNames.Contains("Lexeme")).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("required property", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_EntryAssignedProperty_MayBeOmitted() {
        var (domain, analysis) = Evolve("""
            domain CreateEntry
            Token: entity {
              Lexeme: Text
              Frozen: Boolean
              Active: stage {
                entry { assign Frozen to true }
              }
            }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { Lexeme: "let" }
              }
            }
            """);
        var token = domain.Types.OfType<Entity>().Single(e => e.Name == "Token");
        await Assert.That(analysis.GetStructure(token)!.RequiredPropertyNames.Contains("Frozen")).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("required property", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_OptionalProperty_IsNotRequired() {
        var (domain, analysis) = Evolve("""
            domain OptionalFact
            Token: entity { Lexeme: Text optional }
            """);
        var token = domain.Types.OfType<Entity>().Single();
        await Assert.That(analysis.GetStructure(token)!.RequiredPropertyNames.Contains("Lexeme")).IsFalse();
    }

    [Test]
    public async Task Analyze_CreateOmittingOptionalProperty_HasNoError() {
        var (domain, analysis) = Evolve("""
            domain OptionalCreate
            Token: entity { Lexeme: Text optional }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { }
              }
            }
            """);
        var token = domain.Types.OfType<Entity>().Single(e => e.Name == "Token");

        await Assert.That(analysis.GetStructure(token)!.RequiredPropertyNames.Contains("Lexeme")).IsFalse();
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("required property", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ActionParameter_Bare_IsRequired() {
        var (domain, _) = Evolve("""
            domain Params
            Token: entity {
              Lexeme: Text
              Rename: action (name: Text) { assign Lexeme to name }
            }
            """);
        var behavior = BehaviorMetadata.BuildBehavior(domain);
        var rename = behavior.Entities.Single(e => e.Name == "Token").Actions.Single(a => a.Name == "Rename");
        await Assert.That(rename.Parameters.Single().IsRequired).IsTrue();
    }

    // ── L-15a: a bare to-one navigation is required, as a bare property is ──────────────────

    /// <summary>Every analysis diagnostic of <paramref name="poly"/>, whether or not evolution succeeded.</summary>
    private static IReadOnlyList<Diagnostic> Diagnose(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        return result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    private const string RequiredNavMessage = "navigation 'patron' is required: bind it, or declare it `optional`";

    [Test]
    public async Task Create_OmittingRequiredToOne_ReportsDMEFF011() {
        // Both create forms: a bare `create` names no source, so nothing fills `patron`; a `create in`
        // over a relationship whose back-reference is another navigation leaves `patron` unbound too.
        var errors = Diagnose("""
            domain ReqNavCreate
            ReqNavPatron: entity {
              Name: Text
              fines: many ReqNavFine via patron
            }
            ReqNavDesk: entity {
              Code: Text
              issued: many ReqNavFine via desk
              Issue: action {
                create ReqNavFine { Amount: 5 }
              }
              IssueIn: action {
                create in issued { Amount: 6 }
              }
            }
            ReqNavFine: entity {
              Amount: Number
              patron: ReqNavPatron
              desk: ReqNavDesk optional
            }
            """);
        var missing = errors.Where(d =>
            d.Code == DomainModelDiagnosticCodes.CreateMissingRequiredProperty
            && d.Message.Contains(RequiredNavMessage, StringComparison.Ordinal)).ToList();
        await Assert.That(missing.Count).IsEqualTo(2);
        await Assert.That(errors.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CreateIn_AutoWiredBackReference_IsExempt() {
        var (_, analysis) = Evolve("""
            domain ReqNavAutoWire
            ReqWirePatron: entity {
              Name: Text
              fines: many ReqWireFine
              Charge: action { create in fines { Amount: 5 } }
            }
            ReqWireFine: entity {
              Amount: Number
              patron: ReqWirePatron
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task CreateIn_ViaNamedBackReference_IsExempt_AndFilledOnBothHosts() {
        // Two to-ones back to Person: unique-by-type finds none, so the collection's `via` decides
        // which one `create in` fills. The other is bound.
        const string Dsl = """
            domain ReqNavVia
            ReqViaPerson: entity {
              Name: Text
              assignedTasks: many ReqViaTask via assignedTo
              reviewTasks: many ReqViaTask via reviewedBy
              Assign: action (reviewer: ReqViaPerson) {
                create in assignedTasks { Title: "t" reviewedBy: reviewer }
              }
            }
            ReqViaTask: entity {
              Title: Text
              assignedTo: ReqViaPerson
              reviewedBy: ReqViaPerson
            }
            """;
        await Assert.That(Diagnose(Dsl)).IsEmpty();
        var outcomes = await Poly.Tests.TestHelpers.ParityScenario.FromDsl(Dsl, "ReqNavVia").AssertAgree(side => {
            var reviewer = side.Create("ReqViaPerson", ("Name", "r"));
            var assignee = side.Create("ReqViaPerson", ("Name", "a"));
            side.Use(assignee);
            side.Invoke("Assign", ("reviewer", reviewer));
            side.UseLinked("assignedTasks");
        });
        await Assert.That(outcomes[3].Success).IsTrue();
        await Assert.That(outcomes[4].State["assignedTo"]).IsEqualTo("1");
        await Assert.That(outcomes[4].State["reviewedBy"]).IsEqualTo("1");
    }

    [Test]
    public async Task Create_BindingRequiredToOne_SatisfiesDMEFF011() {
        var (_, analysis) = Evolve("""
            domain ReqNavBind
            ReqBindHall: entity { Name: Text }
            ReqBindVenue: entity {
              Name: Text
              events: many ReqBindEvent
              Book: action (hall: ReqBindHall) { create in events { Title: "x" hall: hall } }
            }
            ReqBindEvent: entity {
              Title: Text
              hall: ReqBindHall
              venue: ReqBindVenue
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task Create_OmittingOptionalToOne_HasNoError() {
        var (_, analysis) = Evolve("""
            domain ReqNavOptional
            ReqOptPatron: entity { Name: Text }
            ReqOptDesk: entity {
              Code: Text
              Issue: action { create ReqOptFine { Amount: 5 } }
            }
            ReqOptFine: entity {
              Amount: Number
              patron: ReqOptPatron optional
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task BareSelfToOne_IsRefused() {
        var errors = Diagnose("""
            domain ReqNavSelf
            ReqSelfAccount: entity {
              Name: Text
              parent: ReqSelfAccount
            }
            """);
        var refused = errors.Where(d => d.Code == DomainModelDiagnosticCodes.RequiredToOneCycle).ToList();
        await Assert.That(refused.Count).IsEqualTo(1);
        await Assert.That(refused[0].Message).Contains("'ReqSelfAccount.parent'");
        await Assert.That(refused[0].Message).Contains("optional");
    }

    [Test]
    public async Task OptionalSelfToOne_IsLegal() {
        var (_, analysis) = Evolve("""
            domain ReqNavSelfOk
            ReqSelfOkAccount: entity {
              Name: Text
              parent: ReqSelfOkAccount optional
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task RequiredToOneCycle_IsRefused() {
        var errors = Diagnose("""
            domain ReqNavCycle
            ReqCycleA: entity {
              Name: Text
              b: ReqCycleB
            }
            ReqCycleB: entity {
              Name: Text
              c: ReqCycleC
            }
            ReqCycleC: entity {
              Name: Text
              a: ReqCycleA
            }
            """);
        var refused = errors.Where(d => d.Code == DomainModelDiagnosticCodes.RequiredToOneCycle).ToList();
        await Assert.That(refused.Count).IsEqualTo(1);
        await Assert.That(refused[0].Message).Contains("ReqCycleA -> ReqCycleB -> ReqCycleC -> ReqCycleA");
        await Assert.That(refused[0].Message).Contains("optional");
    }

    [Test]
    public async Task OwnedNavigation_BreaksARequiredCycle() {
        var (_, analysis) = Evolve("""
            domain ReqNavOwnedPair
            ReqPairReservation: entity {
              Code: Text
              folio: owned ReqPairFolio optional
            }
            ReqPairFolio: entity {
              Total: Number
              reservation: ReqPairReservation
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task AllToOnesOptional_EntityIsRoot() {
        var (domain, analysis) = Evolve("""
            domain ReqNavRoot
            ReqRootPatron: entity { Name: Text }
            ReqRootFine: entity {
              Amount: Number
              patron: ReqRootPatron optional
            }
            ReqRootLoan: entity {
              Code: Text
              patron: ReqRootPatron
            }
            """);
        Entity Named(string name) => domain.Types.OfType<Entity>().Single(e => e.Name == name);
        await Assert.That(analysis.GetStructure(Named("ReqRootFine"))!.IsRoot).IsTrue();
        await Assert.That(analysis.GetStructure(Named("ReqRootLoan"))!.IsRoot).IsFalse();
    }

    [Test]
    public async Task KeyNamingANavigation_IsRefused() {
        // Owner ruling D (#677): a key over a navigation is accepted and unenforced today, so it is
        // refused until its foreign-key shape is decided.
        var errors = Diagnose("""
            domain ReqNavKey
            ReqKeyCustomer: entity key(Code) { Code: Text }
            ReqKeyOrder: entity key(customer) {
              Number: Text
              customer: ReqKeyCustomer
            }
            """);
        var refused = errors.Where(d => d.Code == DomainModelDiagnosticCodes.InvalidKeyDesignation).ToList();
        await Assert.That(refused.Count).IsEqualTo(1);
        await Assert.That(refused[0].Message).Contains("key member 'customer' is a navigation");
        await Assert.That(refused[0].Message).Contains("not supported yet");
    }
}