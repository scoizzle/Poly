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
        var store = new DomainInstanceStore();
        var bookEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Book");
        var patronEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var book = DomainEntityInstance.Create(bookEntity,
            new Dictionary<string, object?> { ["Title"] = "Dune" }, domain);
        var patron = DomainEntityInstance.Create(patronEntity, domain: domain);
        store.Add(book);
        store.Add(patron);

        var result = patron.InvokeAction("CheckOut",
            new Dictionary<string, object?> { ["book"] = book });
        await Assert.That(result.Succeeded).IsTrue();
        var loan = patron.CreatedChildren.Single();
        await Assert.That(store.GetRelatedInstances("book", loan).Single()).IsEqualTo(book);
    }

    [Test]
    public async Task InvokeAction_CreateInNavInitializerUniqueCollision_IsFailure() {
        var (domain, _) = Evolve("""
            domain Test
            Book: entity { ISBN: Text unique required }
            Patron: entity {
              loans: many Loan
              CheckOut: action (book: Book) {
                create in loans { book: book }
              }
            }
            Loan: entity { book: Book }
            """);
        var store = new DomainInstanceStore();
        var bookE = domain.Types.OfType<Entity>().First(e => e.Name == "Book");
        var patronE = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var inStore = DomainEntityInstance.Create(bookE,
            new Dictionary<string, object?> { ["ISBN"] = "978-1" }, domain);
        var duplicate = DomainEntityInstance.Create(bookE,
            new Dictionary<string, object?> { ["ISBN"] = "978-1" }, domain);
        var patron = DomainEntityInstance.Create(patronE, domain: domain);
        store.Add(inStore);
        store.Add(patron);
        var result = patron.InvokeAction("CheckOut",
            new Dictionary<string, object?> { ["book"] = duplicate });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Unique");
    }

    [Test]
    public async Task InvokeAction_CreateInConstraintFail_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Parking
            Permit: entity {
              Plate: Text required pattern("^[A-Z0-9]{2,8}$")
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
        var lotEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var lot = DomainEntityInstance.Create(lotEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(lot);

        var result = lot.InvokeAction("Issue",
            new Dictionary<string, object?> { ["plate"] = "x" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("pattern");
        await Assert.That(lot.GetProperty<object>("Occupied")).IsEqualTo(0L);
        await Assert.That(lot.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_CreateInUniqueCollision_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Parking
            Permit: entity {
              Plate: Text unique required
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
        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var existing = DomainEntityInstance.Create(permitE,
            new Dictionary<string, object?> { ["Plate"] = "ABC123" }, domain);
        var lot = DomainEntityInstance.Create(lotE, domain: domain);
        store.Add(existing);
        store.Add(lot);

        var result = lot.InvokeAction("Issue",
            new Dictionary<string, object?> { ["plate"] = "ABC123" });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Unique");
        await Assert.That(lot.GetProperty<object>("Occupied")).IsEqualTo(0L);
        await Assert.That(lot.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_RequireRelExists_BlocksWhenUnlinked() {
        var (domain, _) = Evolve("""
            domain Campus
            Advisor: entity { Name: Text required }
            Student: entity {
              Name: Text required
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
        var studentEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Student");
        var student = DomainEntityInstance.Create(studentEntity,
            new Dictionary<string, object?> { ["Name"] = "Alex" }, domain);
        var store = new DomainInstanceStore();
        store.Add(student);

        var result = student.InvokeAction("Meet");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards).Contains("HasAdvisor");
        await Assert.That(student.GetProperty<object>("Meetings")).IsEqualTo(0L);
    }

    [Test]
    public async Task InvokeAction_RequireCustomerExists_BlocksWhenUnlinked() {
        var (domain, _) = Evolve("""
            domain Billing
            Customer: entity { Name: Text required }
            Line: entity {
              Sku: Text required
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
        var invoiceEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Invoice");
        var invoice = DomainEntityInstance.Create(invoiceEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(invoice);

        var result = invoice.InvokeAction("Submit");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards).Contains("HasCustomer");
    }

    [Test]
    public async Task InvokeAction_CrossEntityInvoke_FailsFastWhenCalleeWrongStage() {
        var (domain, _) = Evolve("""
            domain Kitchen
            Station: entity {
              Name: Text required
              Ready: stage {
                Fire: action { transition to Busy }
              }
              Busy: stage {
                Clear: action { transition to Ready }
              }
            }
            Ticket: entity {
              TableNo: Text required
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
        var stationEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Station");
        var ticketEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var store = new DomainInstanceStore();
        var station = DomainEntityInstance.Create(stationEntity,
            new Dictionary<string, object?> { ["Name"] = "Grill" }, domain);
        var ticket = DomainEntityInstance.Create(ticketEntity,
            new Dictionary<string, object?> { ["TableNo"] = "12" }, domain);
        store.Add(station);
        store.Add(ticket);
        store.Link("station", ticket, station);

        await Assert.That(station.InvokeAction("Fire").Succeeded).IsTrue();
        var send = ticket.InvokeAction("Send");
        await Assert.That(send.Succeeded).IsFalse();
        await Assert.That(send.ErrorMessage).Contains("only available in stage");
        await Assert.That(ticket.CurrentStage).IsEqualTo("Queued");
        await Assert.That(station.CurrentStage).IsEqualTo("Busy");
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
              Nights: Number range(1, 21) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = false });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(1L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_CreateConstraintFail_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                create Stay { Nights: nights }
              }
            }
            """);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(0L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_UntakenCreateBranch_PriorAssignsStand() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = false });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(1L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_TakenCreateBranch_ConstraintFail_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = true });
        // Failure, not throw-after-mutate (EffectExecutor catch path).
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Nights");
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(0L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_UntakenCreateBranch_TwoInitializers_DoesNotThrowIsSuccess() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
              Rate: Number range(1, 999) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L, ["rate"] = 0L, ["confirm"] = false });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(1L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_TakenCreateBranch_TwoInitializers_ConstraintFail_DoesNotThrowIsSuccess() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
              Rate: Number range(1, 999) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L, ["rate"] = 0L, ["confirm"] = true });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).IsNotNull();
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(0L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_UntakenCreateBranch_FiveInitializers_DoesNotThrowIsSuccess() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              A: Number range(1, 9) required
              B: Number range(1, 9) required
              C: Number range(1, 9) required
              D: Number range(1, 9) required
              E: Number range(1, 9) required
            }
            Guest: entity {
              Book: action (a: Number, b: Number, c: Number, d: Number, e: Number, confirm: Boolean) {
                if (confirm is true) {
                  create Stay { A: a B: b C: c D: d E: e }
                }
              }
            }
            """);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> {
                ["a"] = 0L, ["b"] = 0L, ["c"] = 0L, ["d"] = 0L, ["e"] = 0L, ["confirm"] = false
            });
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_TakenCreateBranch_FiveInitializers_ConstraintFail_DoesNotThrowIsSuccess() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              A: Number range(1, 9) required
              B: Number range(1, 9) required
              C: Number range(1, 9) required
              D: Number range(1, 9) required
              E: Number range(1, 9) required
            }
            Guest: entity {
              Book: action (a: Number, b: Number, c: Number, d: Number, e: Number, confirm: Boolean) {
                if (confirm is true) {
                  create Stay { A: a B: b C: c D: d E: e }
                }
              }
            }
            """);
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> {
                ["a"] = 0L, ["b"] = 0L, ["c"] = 0L, ["d"] = 0L, ["e"] = 0L, ["confirm"] = true
            });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).IsNotNull();
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_ElseIfCreateBranch_ConstraintFail_DoesNotApplyPriorAssigns() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        var result = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L, ["confirm"] = false, ["wait"] = true });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Nights");
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(0L);
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_IfOnMutatedProperty_CreateIllegal_StillAppliesPriorAssign() {
        // Documented miss: taken-ness is the pre-effect bag. assign OpenStays+1
        // then if (OpenStays >= 1) { create illegal } still applies the assign.
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);

        _ = guest.InvokeAction("Book",
            new Dictionary<string, object?> { ["nights"] = 0L });
        // Documented miss: prevalidate/probes evaluate OpenStays on the pre-assign
        // bag (0), so the assign stands.
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(1L);
    }

    [Test]
    public async Task Create_OnEntryIfCreate_DoesNotThrowCannotLower() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        await Assert.That(guest.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(guest.CreatedChildren[0].Entity.Name).IsEqualTo("Stay");
    }

    [Test]
    public async Task Create_OnEntryIfCreate_Illegal_DoesNotSucceedSilent() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Code: Text pattern("^[A-Z]{3}$") required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var thrown = false;
        try {
            DomainEntityInstance.Create(guestEntity, domain: domain);
        }
        catch (InvalidOperationException ex) {
            thrown = true;
            await Assert.That(ex.Message).Contains("Code");
        }
        await Assert.That(thrown).IsTrue();
    }

    [Test]
    public async Task TransitionStage_OnEntryIfCreate_DoesNotThrowCannotLower() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);
        var result = guest.InvokeAction("OpenIt");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(guest.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(guest.CreatedChildren[0].Entity.Name).IsEqualTo("Stay");
    }

    [Test]
    public async Task InvokeAction_TransitionTo_IfOnlyIllegalCreate_DoesNotFlipStage() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Code: Text pattern("^[A-Z]{3}$") required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);
        var result = guest.InvokeAction("OpenIt");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Code");
        await Assert.That(guest.CurrentStage).IsEqualTo("Draft");
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task InvokeAction_TransitionTo_EntryAssignThenIllegalCreate_DoesNotApplyEntryAssign() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Code: Text pattern("^[A-Z]{3}$") required
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
        var guestEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Guest");
        var guest = DomainEntityInstance.Create(guestEntity, domain: domain);
        var store = new DomainInstanceStore();
        store.Add(guest);
        var result = guest.InvokeAction("OpenIt");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Code");
        await Assert.That(guest.GetProperty<object>("OpenStays")).IsEqualTo(0L);
        await Assert.That(guest.CurrentStage).IsEqualTo("Draft");
        await Assert.That(guest.CreatedChildren).IsEmpty();
    }

    [Test]
    public async Task Subscription_IfCreate_RunsTakenBranch() {
        var (domain, _) = Evolve("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21) required
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
        var store = new DomainInstanceStore();
        var patronEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var loanEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Loan");
        var patron = DomainEntityInstance.Create(patronEntity,
            new Dictionary<string, object?> { ["Flag"] = "NONE" }, domain: domain);
        var loan = DomainEntityInstance.Create(loanEntity,
            new Dictionary<string, object?> { ["Code"] = "L1" }, domain: domain);
        store.Add(patron);
        store.Add(loan);
        store.Link("loans", patron, loan);

        var result = loan.InvokeAction("Overdue");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("FIRED");
        await Assert.That(patron.CreatedChildren.Count).IsEqualTo(1);
        await Assert.That(patron.CreatedChildren[0].Entity.Name).IsEqualTo("Stay");
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
        var ticketEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var store = new DomainInstanceStore();

        var draftTicket = DomainEntityInstance.Create(ticketEntity, domain: domain);
        store.Add(draftTicket);
        var draftAbort = draftTicket.InvokeAction("Abort");
        await Assert.That(draftAbort.Succeeded).IsTrue();
        await Assert.That(draftTicket.GetProperty<object>("Flag")).IsEqualTo(1L);

        var openTicket = DomainEntityInstance.Create(ticketEntity, domain: domain);
        store.Add(openTicket);
        var opened = openTicket.InvokeAction("OpenIt");
        await Assert.That(opened.Succeeded).IsTrue();
        var openAbort = openTicket.InvokeAction("Abort");
        await Assert.That(openAbort.Succeeded).IsTrue();
        await Assert.That(openTicket.GetProperty<object>("Flag")).IsEqualTo(2L);
    }

}
