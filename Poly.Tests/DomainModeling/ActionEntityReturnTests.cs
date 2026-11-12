using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using DmAction = Poly.DomainModeling.Ontology.Action;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// P3: action <c>-&gt; Entity</c> requires create producer; runtime returns created instance.
/// </summary>
public class ActionEntityReturnTests {
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

    private static IEnumerable<Node> FlattenSyntax(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var n in FlattenSyntax(child))
                yield return n;
        }
    }

    /// <summary>Evolves a domain that is EXPECTED to fail analysis; returns the
    /// concatenated error messages (fail-closed: evolution must reject it).</summary>
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var customerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var customer = exec.Create((customerEntity).Name, new Dictionary<string, object?> { ["Name"] = "A" }).Instance!;
        var result = exec.Invoke(customer, "PlaceOrder");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Instance?.EntityName).IsEqualTo("Order");
        await Assert.That(result.Instance).IsNotNull();
        await Assert.That(result.Instance!.EntityName).IsEqualTo("Order");
        await Assert.That((string)result.Instance.Bag["Code"]!).IsEqualTo("O1");
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
    public async Task Analyze_CreateWithAllRequiredProvided_NoError() {
        // Providing every required property → no DMEFF011.
        var (_, analysis) = Evolve("""
            domain CreateOk
            Token: entity { Lexeme: Text }
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
              Name: Text
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
              Name: Text
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
            Token: entity { Lexeme: Text default("let") }
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
    public async Task InvokeAction_CreateIn_BindsSingularNavInitializer() {
        var (domain, _) = Evolve("""
            domain CreateNavBinding
            Book: entity { Title: Text }
            Patron: entity {
              loans: many Loan
              CheckOut: action (book: Book) {
                create in loans { book: book }
              }
            }
            Loan: entity {
              book: Book
              borrower: Patron
            }
            """);
        var exec = ModuleExec.For(domain);
        var bookEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Book");
        var patronEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var book = exec.Create((bookEntity).Name, new Dictionary<string, object?> { ["Title"] = "Dune" }).Instance!;
        var patron = exec.Create((patronEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(patron, "CheckOut", new Dictionary<string, object?> { ["book"] = book });
        await Assert.That(result.Succeeded).IsTrue();
        var loan = result.CreatedChildren.Single();
        await Assert.That(ModuleReads.Related(exec, "book", loan).Single()).IsEqualTo(book);
    }

    [Test]
    public async Task InvokeAction_CreateInNavInitializerUniqueCollision_IsFailure() {
        var (domain, _) = Evolve("""
            domain Test
            uses persistence
            Book: entity { ISBN: Text unique }
            Patron: entity {
              loans: many Loan
              CheckOut: action (book: Book) {
                create in loans { book: book }
              }
            }
            Loan: entity { book: Book }
            """);
        var exec = ModuleExec.For(domain);
        var bookE = domain.Types.OfType<Entity>().First(e => e.Name == "Book");
        var patronE = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        exec.Create((bookE).Name, new Dictionary<string, object?> { ["ISBN"] = "978-1" });
        // Create fails and registers nothing. The deleted store left this book
        // unregistered; create-in adopted it and failed unique. Pass that bag.
        _ = exec.Create((bookE).Name, new Dictionary<string, object?> { ["ISBN"] = "978-1" });
        var duplicate = new ModuleInstance(
            "duplicate", bookE.Name, new Dictionary<string, object?> { ["ISBN"] = "978-1" });
        var patron = exec.Create((patronE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(patron, "CheckOut", new Dictionary<string, object?> { ["book"] = duplicate });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
    }

    [Test]
    public async Task InvokeAction_CreateInConstraintFail_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Parking
            Permit: entity {
              Plate: Text pattern("^[A-Z0-9]{2,8}$")
            }
            Lot: entity {
              Occupied: Number default(0)
              permits: many Permit
              Issue: action (plate: Text) {
                assign Occupied to Occupied + 1
                create in permits { Plate: plate }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var lotEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var lot = exec.Create((lotEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(lot, "Issue", new Dictionary<string, object?> { ["plate"] = "x" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("pattern");
        await Assert.That(lot.Bag["Occupied"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_CreateInUniqueCollision_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
            }
            Lot: entity {
              Occupied: Number default(0)
              permits: many Permit
              Issue: action (plate: Text) {
                assign Occupied to Occupied + 1
                create in permits { Plate: plate }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var lot = exec.Create((lotE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(lot, "Issue", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
        await Assert.That(lot.Bag["Occupied"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_CreateInUniqueDefaultCollision_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique default("STD")
            }
            Lot: entity {
              Occupied: Number default(0)
              permits: many Permit
              Issue: action {
                assign Occupied to Occupied + 1
                create in permits { }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?>()).Instance!;
        var lot = exec.Create((lotE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(lot, "Issue");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
        await Assert.That(lot.Bag["Occupied"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_AssignUniqueCollision_DoesNotMutate() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
              Relabel: action (plate: Text) {
                assign Plate to plate
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var other = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "XYZ999" }).Instance!;
        var result = exec.Invoke(other, "Relabel", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
        await Assert.That((string)other.Bag["Plate"]!).IsEqualTo("XYZ999");
    }

    [Test]
    public async Task InvokeAction_ConditionalCreateUniqueCollision_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
            }
            Lot: entity {
              Occupied: Number default(0)
              permits: many Permit
              Issue: action (plate: Text) {
                assign Occupied to Occupied + 1
                if (plate != "") {
                  create in permits { Plate: plate }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var lot = exec.Create((lotE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(lot, "Issue", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
        await Assert.That(lot.Bag["Occupied"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task SetProperty_UniqueCollision_ThrowsBeforeMutate() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var other = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "XYZ999" }).Instance!;
        var thrown = false;
        try {
            exec.SetProperty(other, "Plate", "ABC123");
        }
        catch (InvalidOperationException ex) {
            thrown = true;
            await Assert.That(ex.Message).Contains("Unique");
        }
        await Assert.That(thrown).IsTrue();
        await Assert.That((string)other.Bag["Plate"]!).IsEqualTo("XYZ999");
    }

    [Test]
    public async Task InvokeAction_ConditionalAssignUniqueCollision_DoesNotMutate() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
              Relabel: action (plate: Text) {
                if (plate != "") {
                  assign Plate to plate
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var other = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "XYZ999" }).Instance!;
        var result = exec.Invoke(other, "Relabel", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
        await Assert.That((string)other.Bag["Plate"]!).IsEqualTo("XYZ999");
    }

    [Test]
    public async Task InvokeAction_ElseAssignUniqueCollision_DoesNotMutate() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
              Relabel: action (plate: Text) {
                if (plate == "") {
                } else {
                  assign Plate to plate
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var other = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "XYZ999" }).Instance!;
        var result = exec.Invoke(other, "Relabel", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
        await Assert.That((string)other.Bag["Plate"]!).IsEqualTo("XYZ999");
    }

    [Test]
    public async Task InvokeAction_NestedIfAssignUniqueCollision_DoesNotMutate() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
              Relabel: action (plate: Text) {
                if (plate != "") {
                  if (plate != "") {
                    assign Plate to plate
                  }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var other = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "XYZ999" }).Instance!;
        var result = exec.Invoke(other, "Relabel", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
        await Assert.That((string)other.Bag["Plate"]!).IsEqualTo("XYZ999");
    }

    [Test]
    public async Task InvokeAction_ConditionDrift_AssignThenIfCreate_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
            }
            Lot: entity {
              Enabled: Boolean default(false)
              Occupied: Number default(0)
              permits: many Permit
              Issue: action (plate: Text) {
                assign Enabled to true
                if (Enabled) {
                  assign Occupied to Occupied + 1
                  create in permits { Plate: plate }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var lot = exec.Create((lotE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(lot, "Issue", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unique");
        await Assert.That(lot.Bag["Occupied"]).IsEqualTo(0L);
        await Assert.That((bool)lot.Bag["Enabled"]!).IsFalse();
        await Assert.That(result.CreatedChildren).IsEmpty();

        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(
            domain, "Lot", "Issue", out var issue)).IsTrue();
        await Assert.That(issue?.Body).IsNotNull();
        var flat = FlattenSyntax(issue!.Body!).ToList();
        var createIdx = flat.FindIndex(n =>
            n is Invoke inv && inv.Delegate is Member { MemberName: "CreatePermits" });
        var createAssignIdx = flat.FindIndex(n =>
            n is Assignment a && a.Destination is Member { MemberName: "Enabled" });
        await Assert.That(flat.Any(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } })).IsFalse();
        await Assert.That(createAssignIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(createIdx).IsGreaterThan(createAssignIdx);
    }

    [Test]
    public async Task InvokeAction_ConditionDrift_AssignFalseSkipsCreate_Succeeds() {
        var (domain, _) = Evolve("""
            domain Parking
            uses persistence
            Permit: entity {
              Plate: Text unique
            }
            Lot: entity {
              Enabled: Boolean default(true)
              Occupied: Number default(0)
              permits: many Permit
              Issue: action (plate: Text) {
                assign Enabled to false
                if (Enabled) {
                  assign Occupied to Occupied + 1
                  create in permits { Plate: plate }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var existing = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Plate"] = "ABC123" }).Instance!;
        var lot = exec.Create((lotE).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(lot, "Issue", new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That((bool)lot.Bag["Enabled"]!).IsFalse();
        await Assert.That(lot.Bag["Occupied"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_RequireRelExists_BlocksWhenUnlinked() {
        var (domain, _) = Evolve("""
            domain Campus
            Advisor: entity { Name: Text }
            Student: entity {
              Name: Text
              Meetings: Number default(0)
              advisor: Advisor
              HasAdvisor: policy { advisor exists }
              Enrolled: stage {
                Meet: action
                  require HasAdvisor
                {
                  assign Meetings to Meetings + 1
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var studentEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Student");
        var student = exec.Create((studentEntity).Name, new Dictionary<string, object?> { ["Name"] = "Alex" }).Instance!;
        var result = exec.Invoke(student, "Meet");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("HasAdvisor");
        await Assert.That(student.Bag["Meetings"]).IsEqualTo(0L);
    }

    [Test]
    public async Task InvokeAction_RequireCustomerExists_BlocksWhenUnlinked() {
        var (domain, _) = Evolve("""
            domain Billing
            Customer: entity { Name: Text }
            Line: entity {
              Sku: Text
              Open: stage {
                Post: action { transition to Posted }
              }
              Posted: stage { }
            }
            Invoice: entity {
              Total: Number default(0)
              customer: Customer
              lines: many Line
              HasCustomer: policy { customer exists }
              Open: stage {
                Submit: action
                  require HasCustomer
                {
                  for lines as line where line in Open
                    invoke line.Post
                  transition to Posted
                }
              }
              Posted: stage { }
            }
            """);
        var exec = ModuleExec.For(domain);
        var invoiceEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Invoice");
        var invoice = exec.Create((invoiceEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(invoice, "Submit");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("HasCustomer");
    }

    [Test]
    public async Task InvokeAction_CrossEntityInvoke_FailsFastWhenCalleeWrongStage() {
        var (domain, _) = Evolve("""
            domain Kitchen
            Station: entity {
              Name: Text
              Ready: stage {
                Fire: action { transition to Busy }
              }
              Busy: stage {
                Clear: action { transition to Ready }
              }
            }
            Ticket: entity {
              TableNo: Text
              station: Station
              Queued: stage {
                Send: action {
                  invoke station.Fire
                  transition to Cooking
                }
              }
              Cooking: stage { }
            }
            """);
        var exec = ModuleExec.For(domain);
        var stationEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Station");
        var ticketEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var station = exec.Create((stationEntity).Name, new Dictionary<string, object?> { ["Name"] = "Grill" }).Instance!;
        var ticket = exec.Create((ticketEntity).Name, new Dictionary<string, object?> { ["TableNo"] = "12" }).Instance!;
        exec.Link("station", ticket, station);

        await Assert.That(exec.Invoke(station, "Fire").Succeeded).IsTrue();
        var send = exec.Invoke(ticket, "Send");
        await Assert.That(send.Succeeded).IsFalse();
        await Assert.That(send.Error).Contains("requires stage 'Ready'");
        await Assert.That(ModuleReads.Stage(ticket)).IsEqualTo("Queued");
        await Assert.That(ModuleReads.Stage(station)).IsEqualTo("Busy");
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
    [Test]
    public async Task InvokeAction_UntakenCreateInBranch_IgnoresIllegalInitializer() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              stays: many Stay
              Book: action (nights: Number, confirm: Boolean) {
                if (confirm is true) {
                  create in stays { Nights: nights }
                }
                assign OpenStays to OpenStays + 1
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = false });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(1L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_CreateConstraintFail_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                create Stay { Nights: nights }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_UntakenCreateBranch_PriorAssignsStand() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm is true) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = false });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(1L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_TakenCreateBranch_ConstraintFail_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm is true) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = true });
        // Failure, not throw-after-mutate (EffectExecutor catch path).
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Nights");
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_UntakenCreateBranch_TwoInitializers_DoesNotThrowIsSuccess() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
              Rate: Number range(1, 999)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, rate: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm is true) {
                  create Stay { Nights: nights Rate: rate }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L, ["rate"] = 0L, ["confirm"] = false });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(1L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_TakenCreateBranch_TwoInitializers_ConstraintFail_DoesNotThrowIsSuccess() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
              Rate: Number range(1, 999)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, rate: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm is true) {
                  create Stay { Nights: nights Rate: rate }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L, ["rate"] = 0L, ["confirm"] = true });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_UntakenCreateBranch_FiveInitializers_DoesNotThrowIsSuccess() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              A: Number range(1, 9)
              B: Number range(1, 9)
              C: Number range(1, 9)
              D: Number range(1, 9)
              E: Number range(1, 9)
            }
            Guest: entity {
              Book: action (a: Number, b: Number, c: Number, d: Number, e: Number, confirm: Boolean) {
                if (confirm is true) {
                  create Stay { A: a B: b C: c D: d E: e }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> {
            ["a"] = 0L,
            ["b"] = 0L,
            ["c"] = 0L,
            ["d"] = 0L,
            ["e"] = 0L,
            ["confirm"] = false
        });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_TakenCreateBranch_FiveInitializers_ConstraintFail_DoesNotThrowIsSuccess() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              A: Number range(1, 9)
              B: Number range(1, 9)
              C: Number range(1, 9)
              D: Number range(1, 9)
              E: Number range(1, 9)
            }
            Guest: entity {
              Book: action (a: Number, b: Number, c: Number, d: Number, e: Number, confirm: Boolean) {
                if (confirm is true) {
                  create Stay { A: a B: b C: c D: d E: e }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> {
            ["a"] = 0L,
            ["b"] = 0L,
            ["c"] = 0L,
            ["d"] = 0L,
            ["e"] = 0L,
            ["confirm"] = true
        });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_ElseIfCreateBranch_ConstraintFail_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean, wait: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm is true) {
                } else if (wait is true) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = false, ["wait"] = true });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Nights");
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_IfOnMutatedProperty_CreateIllegal_DoesNotApplyPriorAssign() {
        // Assign, then the if, then Stay.Create. A Nights failure restores OpenStays.
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                if (OpenStays >= 1) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Nights");
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();

        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(
            domain, "Guest", "Book", out var book)).IsTrue();
        await Assert.That(book?.Body).IsNotNull();
        var flat = FlattenSyntax(book!.Body!).ToList();
        var createIdx = flat.FindIndex(n =>
            n is Invoke inv && inv.Delegate is Member { MemberName: "Create" });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment a
            && a.Destination is Member { MemberName: "OpenStays" });
        await Assert.That(flat.Any(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } })).IsFalse();
        await Assert.That(assignIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
    }

    [Test]
    public async Task InvokeAction_NestedIfOnMutatedProperty_CreateIllegal_DoesNotApplyPriorAssign() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm) {
                  if (OpenStays >= 1) {
                    create Stay { Nights: nights }
                  }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = true });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Nights");
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();

        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(
            domain, "Guest", "Book", out var book)).IsTrue();
        await Assert.That(book?.Body).IsNotNull();
        var flat = FlattenSyntax(book!.Body!).ToList();
        var createIdx = flat.FindIndex(n =>
            n is Invoke inv && inv.Delegate is Member { MemberName: "Create" });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment a
            && a.Destination is Member { MemberName: "OpenStays" });
        await Assert.That(flat.Any(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } })).IsFalse();
        await Assert.That(assignIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
    }

    [Test]
    public async Task InvokeAction_ElseIfOnMutatedProperty_CreateIllegal_DoesNotApplyPriorAssign() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm) {
                } else if (OpenStays >= 1) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "Book", new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = false });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Nights");
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();

        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(
            domain, "Guest", "Book", out var book)).IsTrue();
        await Assert.That(book?.Body).IsNotNull();
        var flat = FlattenSyntax(book!.Body!).ToList();
        var createIdx = flat.FindIndex(n =>
            n is Invoke inv && inv.Delegate is Member { MemberName: "Create" });
        var assignIdx = flat.FindIndex(n =>
            n is Assignment a
            && a.Destination is Member { MemberName: "OpenStays" });
        await Assert.That(flat.Any(n =>
            n is Invoke { Delegate: Member { MemberName: "ProbeCreate" } })).IsFalse();
        await Assert.That(assignIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
    }

    [Test]
    public async Task Create_OnEntryIfCreate_DoesNotThrowCannotLower() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              Flag: Number default(1)
              Draft: stage {
                entry {
                  if (Flag >= 1) {
                    create Stay { Nights: 1 }
                  }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var created = exec.Create(guestEntity.Name, new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(created.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(created.CreatedChildren[0].EntityName).IsEqualTo("Stay");
    }

    [Test]
    public async Task Create_OnEntryIfCreate_Illegal_DoesNotSucceedSilent() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Code: Text pattern("^[A-Z]{3}$")
            }
            Guest: entity {
              Flag: Number default(1)
              Tag: Text default("bad")
              Draft: stage {
                entry {
                  if (Flag >= 1) {
                    create Stay { Code: Tag }
                  }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        // DomainEntityInstance.Create threw ConstraintFailureException. The module
        // factory still throws from the void entry effect; a result would diverge.
        await Assert.That(() => exec.Create(guestEntity.Name, new Dictionary<string, object?>()))
            .Throws<ConstraintFailureException>()
            .WithMessageContaining("Code");
    }

    [Test]
    public async Task TransitionStage_OnEntryIfCreate_DoesNotThrowCannotLower() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              Flag: Number default(1)
              Draft: stage {
                OpenIt: action { transition to Open }
              }
              Open: stage {
                entry {
                  if (Flag >= 1) {
                    create Stay { Nights: 1 }
                  }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "OpenIt");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(result.CreatedChildren[0].EntityName).IsEqualTo("Stay");
    }

    [Test]
    public async Task InvokeAction_TransitionTo_IfOnlyIllegalCreate_DoesNotFlipStage() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Code: Text pattern("^[A-Z]{3}$")
            }
            Guest: entity {
              Flag: Number default(1)
              Tag: Text default("bad")
              Draft: stage {
                OpenIt: action { transition to Open }
              }
              Open: stage {
                entry {
                  if (Flag >= 1) {
                    create Stay { Code: Tag }
                  }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "OpenIt");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Code");
        await Assert.That(ModuleReads.Stage(guest)).IsEqualTo("Draft");
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_TransitionTo_EntryAssignThenIllegalCreate_DoesNotApplyEntryAssign() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Code: Text pattern("^[A-Z]{3}$")
            }
            Guest: entity {
              Flag: Number default(1)
              Tag: Text default("bad")
              OpenStays: Number default(0)
              Draft: stage {
                OpenIt: action { transition to Open }
              }
              Open: stage {
                entry {
                  assign OpenStays to OpenStays + 1
                  if (Flag >= 1) {
                    create Stay { Code: Tag }
                  }
                }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = exec.Create((guestEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(guest, "OpenIt");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Code");
        await Assert.That(guest.Bag["OpenStays"]).IsEqualTo(0L);
        await Assert.That(ModuleReads.Stage(guest)).IsEqualTo("Draft");
        await Assert.That(result.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task Subscription_IfCreate_RunsTakenBranch() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Patron: entity {
              Flag: Text
              Auto: Number default(1)
              stays: many Stay
              loans: many Loan
              when loans Overdue {
                if (Auto >= 1) {
                  create Stay { Nights: 1 }
                }
                assign Flag to "FIRED"
              }
            }
            Loan: entity {
              Code: Text
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage { }
            }
            """);
        var exec = ModuleExec.For(domain);
        var patronEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var loanEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Loan");
        var patron = exec.Create((patronEntity).Name, new Dictionary<string, object?> { ["Flag"] = "NONE" }).Instance!;
        var loan = exec.Create((loanEntity).Name, new Dictionary<string, object?> { ["Code"] = "L1" }).Instance!;
        exec.Link("loans", patron, loan);

        var result = exec.Invoke(loan, "Overdue");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That((string)patron.Bag["Flag"]!).IsEqualTo("FIRED");
        await Assert.That(result.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(result.CreatedChildren[0].EntityName).IsEqualTo("Stay");
    }

    [Test]
    public async Task InvokeAction_EntityInvokeCancel_DispatchesCurrentStage() {
        var (domain, _) = Evolve("""
            domain Tickets
            Ticket: entity {
              Flag: Number default(0)
              Draft: stage {
                Cancel: action { assign Flag to 1 }
                OpenIt: action { transition to Open }
              }
              Open: stage {
                Cancel: action { assign Flag to 2 }
              }
              Closed: stage { }
              Abort: action { invoke Cancel }
            }
            """);
        var exec = ModuleExec.For(domain);
        var ticketEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var draftTicket = exec.Create((ticketEntity).Name, new Dictionary<string, object?>()).Instance!;
        var draftAbort = exec.Invoke(draftTicket, "Abort");
        await Assert.That(draftAbort.Succeeded).IsTrue();
        await Assert.That(draftTicket.Bag["Flag"]).IsEqualTo(1L);

        var openTicket = exec.Create((ticketEntity).Name, new Dictionary<string, object?>()).Instance!;
        var opened = exec.Invoke(openTicket, "OpenIt");
        await Assert.That(opened.Succeeded).IsTrue();
        var openAbort = exec.Invoke(openTicket, "Abort");
        await Assert.That(openAbort.Succeeded).IsTrue();
        await Assert.That(openTicket.Bag["Flag"]).IsEqualTo(2L);
    }

    [Test]
    public async Task Invoke_NestedInvokeFailure_FailFastsBeforeLaterEffects() {
        var (domain, analysis) = Evolve("""
            domain Shop
            Item: entity {
              Name: Text
              Draft: stage { }
            }
            Cart: entity {
              items: many Item
              Flag: Number default(0)
              AlwaysFail: policy { Flag > 99 }
              Prep: action require AlwaysFail { assign Flag to 1 }
              Place: action -> Item {
                invoke Prep
                assign Flag to 2
                create in items { Name: "x" }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var cartEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Cart");
        var cart = exec.Create((cartEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(cart, "Place");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(cart.Bag["Flag"]).IsEqualTo(0L);
        await Assert.That(result.CreatedChildren).IsEmpty();
        await Assert.That(result.Instance).IsNull();
    }

    [Test]
    public async Task Invoke_NestedInvokeSuccess_RunsLaterEffectsAndReturnsProducer() {
        var (domain, analysis) = Evolve("""
            domain Shop
            Item: entity {
              Name: Text
              Draft: stage { }
            }
            Cart: entity {
              items: many Item
              Flag: Number default(0)
              Prep: action { assign Flag to 1 }
              Place: action -> Item {
                invoke Prep
                assign Flag to 2
                create in items { Name: "x" }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var cartEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Cart");
        var cart = exec.Create((cartEntity).Name, new Dictionary<string, object?>()).Instance!;
        var result = exec.Invoke(cart, "Place");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(cart.Bag["Flag"]).IsEqualTo(2L);
        await Assert.That(result.Instance?.EntityName).IsEqualTo("Item");
        await Assert.That(result.Instance).IsNotNull();
        await Assert.That(result.Instance!.EntityName).IsEqualTo("Item");
        await Assert.That((string)result.Instance.Bag["Name"]!).IsEqualTo("x");
    }

    [Test]
    public async Task Invoke_TypeMismatchArg_FailsWithoutMutating() {
        var errors = EvolveExpectingError("""
            domain Shop
            Widget: entity {
              Used: Number default(0)
              Apply: action (item: Widget) { assign Used to Used + 1 }
            }
            Gadget: entity { Name: Text }
            Host: entity {
              widget: Widget
              Open: stage {
                Run: action (gadget: Gadget) {
                  invoke widget.Apply(item: gadget)
                }
              }
            }
            """);
        await Assert.That(errors).Contains("type mismatch in argument 'item' of invoke 'Apply'");
    }

}