using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;
using Poly.DomainModeling.Ontology.Effects;

using DmAction = Poly.DomainModeling.Ontology.Action;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// P3: action <c>-&gt; Entity</c> requires create producer; runtime returns created instance.
/// </summary>
public class ActionEntityReturnTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    /// <summary>Evolves a domain that is EXPECTED to fail analysis; returns the
    /// concatenated error messages (fail-closed: evolution must reject it).</summary>
    private static string EvolveExpectingError(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (result.Succeeded)
            throw new InvalidOperationException("Expected evolution to fail, but it succeeded.");
        return string.Join("; ",
            result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
    }

    [Test]
    public async Task Analyze_ReturnTypeWithoutCreate_ReportsDMEFF009() {
        var draft = new Stage("Draft", [], [], [], []);
        var done = new Stage("Done", [], [], [], []);
        var place = new DmAction(
            "Place",
            new InvocationResult([new InvocationResult.Member("Instance", new DomainTypeReference("Order"), [])]),
            [],
            [new StageTransitionEffect(new StageReference("Done"))],
            []);
        var order = new Entity("Order", [], [place], [], [draft, done]);
        var domain = DomainTestFactory.Create("RetNoCreate", [order], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        await Assert.That(errors.Any(d =>
            d.Message.Contains("declares return type", StringComparison.Ordinal)
            && d.Message.Contains("no create", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Analyze_PrimitiveReturn_ReportsNotSupported() {
        var x = new Property("X", new DomainTypeReference("Number"), []);
        var compute = new DmAction(
            "Compute",
            new InvocationResult([new InvocationResult.Member("Value", new DomainTypeReference("Number"), [])]),
            [],
            [new AssignEffect(DomainExpression.Property("X"), DomainExpression.Literal(1L))],
            []);
        var order = new Entity("Order", [x], [compute], [], []);
        var domain = DomainFactory.Create("PrimRet");
        domain = domain with {
            Types = domain.Types.Concat([order]).ToList()
        };
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        await Assert.That(errors.Any(d =>
            d.Message.Contains("only entity", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Invoke_CreateInWithReturnType_ReturnsCreatedInstance() {
        var (domain, analysis) = Evolve("""
            domain RetCreate
            Customer: entity {
              Name: Text
              orders: many Order
              PlaceOrder: action -> Order {
                create in orders { Code: "O1" }
              }
              Active: stage {}
            }
            Order: entity {
              Code: Text
              Draft: stage {}
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var customerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var customer = DomainEntityInstance.Create(customerEntity,
            new Dictionary<string, object?> { ["Name"] = "A" }, domain);
        store.Add(customer);

        var result = customer.InvokeAction("PlaceOrder");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.ResultTypeName).IsEqualTo("Order");
        await Assert.That(result.ResultInstance).IsNotNull();
        await Assert.That(result.ResultInstance!.Entity.Name).IsEqualTo("Order");
        await Assert.That(result.ResultInstance.GetProperty<string>("Code")).IsEqualTo("O1");
    }

    [Test]
    public async Task Analyze_CreateInReturn_HappyPath_NoError() {
        var (_, analysis) = Evolve("""
            domain RetOk
            Customer: entity {
              orders: many Order
              PlaceOrder: action -> Order {
                create in orders { }
              }
            }
            Order: entity { Draft: stage {} }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("declares return type", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_ReturnTypeWithCreateNotLast_ReportsDMEFF010() {
        // DMEFF010: the create yielding the return value must be the FINAL statement.
        // `create in tokens { }; transition to Parsing` — create is not last → error.
        var message = EvolveExpectingError("""
            domain RetLast
            Token: entity { Kind: Text }
            Box: entity {
              tokens: many Token
              Lex: action -> Token {
                create in tokens { Kind: "let" }
                transition to Done
              }
              Done: stage { }
            }
            """);
        await Assert.That(message).Contains("declares return type 'Token'");
        await Assert.That(message).Contains("final statement");
    }

    [Test]
    public async Task Analyze_ReturnTypeWithAssignAfterCreate_ReportsDMEFF010() {
        // Even a harmless assign after the create is not a producer → error.
        var message = EvolveExpectingError("""
            domain RetLastAssign
            Order: entity { Code: Text }
            Customer: entity {
              Name: Text
              orders: many Order
              Place: action -> Order {
                create in orders { Code: "O1" }
                assign Name to "last"
              }
            }
            """);
        await Assert.That(message).Contains("final statement");
    }

    [Test]
    public async Task Analyze_ReturnTypeConditionalBothBranchesCreate_NoError() {
        // Final statement is a conditional; every branch ends in a producer → OK.
        var (_, analysis) = Evolve("""
            domain RetCond
            Order: entity { Code: Text }
            Customer: entity {
              Rush: Boolean
              orders: many Order
              Place: action -> Order {
                if (Rush is true) {
                  create in orders { Code: "rush" }
                } else {
                  create in orders { Code: "normal" }
                }
              }
            }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("declares return type", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_ReturnTypeConditionalWithoutElse_ReportsDMEFF010() {
        // Final conditional without an else can produce nothing when the condition
        // is false → fail closed.
        var message = EvolveExpectingError("""
            domain RetCondNoElse
            Order: entity { Code: Text }
            Customer: entity {
              Rush: Boolean
              orders: many Order
              Place: action -> Order {
                if (Rush is true) {
                  create in orders { Code: "rush" }
                }
              }
            }
            """);
        await Assert.That(message).Contains("final statement");
    }

    [Test]
    public async Task Analyze_ReturnTypeConditionalBranchEndsInNonProducer_ReportsDMEFF010() {
        // One branch of the final conditional ends in a non-producer → error.
        var message = EvolveExpectingError("""
            domain RetCondBadBranch
            Order: entity { Code: Text }
            Customer: entity {
              Name: Text
              Rush: Boolean
              orders: many Order
              Place: action -> Order {
                if (Rush is true) {
                  create in orders { Code: "rush" }
                } else {
                  assign Name to "none"
                }
              }
            }
            """);
        await Assert.That(message).Contains("final statement");
    }

    [Test]
    public async Task Analyze_CreateMissingRequiredProperty_ReportsDMEFF011() {
        // DMEFF011: `create in tokens { }` must provide every `required` property
        // (Token.Lexeme) — otherwise the generated Create factory throws at runtime.
        var message = EvolveExpectingError("""
            domain CreateReq
            Token: entity { Lexeme: Text required }
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
    public async Task Analyze_CreateWithAllRequiredProvided_NoError() {
        // Providing every required property → no DMEFF011.
        var (_, analysis) = Evolve("""
            domain CreateOk
            Token: entity { Lexeme: Text required }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { Lexeme: "let" }
              }
            }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("required property", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_CreateIn_BackRefNavPresent_NoError() {
        // The back-reference nav (typed as the source entity) is auto-wired by
        // create-in (guide §0.3) — it is not a required property to provide.
        // (Navs cannot carry `required` in the DSL; this pins the skip path.)
        var (_, analysis) = Evolve("""
            domain CreateBackRef
            Pet: entity {
              Name: Text required
              owner: Owner
            }
            Owner: entity {
              pets: many Pet
              Adopt: action {
                create in pets { Name: "Rex" }
              }
            }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("required property", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_CreateIn_NonBackRefRequiredMissing_ReportsDMEFF011() {
        // A required property (Name) must be provided even with a back-ref present.
        var message = EvolveExpectingError("""
            domain CreateNonBackRef
            Pet: entity {
              Name: Text required
              owner: Owner
            }
            Owner: entity {
              pets: many Pet
              Adopt: action {
                create in pets { }
              }
            }
            """);
        await Assert.That(message).Contains("required property 'Name'");
    }

    [Test]
    public async Task Analyze_CreateRequiredWithDefault_NoError() {
        // A required property WITH a default does not need an explicit initializer.
        var (_, analysis) = Evolve("""
            domain CreateDefault
            Token: entity { Lexeme: Text required default("let") }
            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { }
              }
            }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("required property", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_CreateInWithSingularNavBinding_NoError() {
        // A create-in initializer may bind a SINGULAR navigation property of the
        // target (e.g. `create in loans { book: book }`) — the runtime evaluates it
        // into the child's value bag and the exporter wires it as a Create(...) nav
        // parameter. The analyzer must not reject it (it previously reported
        // "unknown property 'book'" — the shipped library demo relies on this).
        var (_, analysis) = Evolve("""
            domain CreateNavBinding
            Book: entity { Title: Text }
            Patron: entity {
              loans: many Loan
              CheckOut: action (book: Book) -> Loan {
                create in loans { book: book }
              }
            }
            Loan: entity {
              book: Book
              borrower: Patron
            }
            """);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("unknown property 'book'", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_CreateInWithCollectionNavBinding_ReportsUnknownProperty() {
        // A `many` collection nav is NOT a bindable initializer target (the exporter
        // emits empty collections for those) — binding it must still fail closed.
        var message = EvolveExpectingError("""
            domain CreateNavCollection
            Token: entity {
              Kind: Text
              links: many Token
            }
            Box: entity {
              tokens: many Token
              Make: action {
                create in tokens { Kind: "k" links: Box }
              }
            }
            """);
        await Assert.That(message).Contains("unknown property 'links'");
    }
}