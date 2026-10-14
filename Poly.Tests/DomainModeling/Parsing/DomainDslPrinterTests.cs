using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

// ─── DomainDslPrinter expression print: binder → Grammar Printer → DslTokenWriter ──
// pack-1-3: expr-primary literals/true/false/null/ident print through the table;
// constructs without a printable pattern yet fall back to existing dispatch;
// DateOperation (no binder, no dispatch handler) fails closed.

public sealed class DomainDslPrinterTests {
    [Test]
    public async Task PrintExpression_DateOperation_WithoutBinder_Throws() {
        var printer = new DomainDslPrinter();
        var expr = new DateOperation(
            DomainExpression.Property("DueDate"),
            DomainExpression.Literal(14),
            DateOperationKind.AddDays);

        await Assert.That(() => printer.PrintTestExpression(expr))
            .Throws<InvalidOperationException>()
            .WithMessage("Cannot print expression type 'DateOperation': no registered print binder or pattern.");
    }

    [Test]
    public async Task PrintExpression_LiteralAndProperty_MatchProductSpacing() {
        var printer = new DomainDslPrinter();
        var expr = DomainExpression.GreaterThanOrEqual(
            DomainExpression.Property("Age"),
            DomainExpression.Literal(18L));

        await Assert.That(printer.PrintTestExpression(expr)).IsEqualTo("Age >= 18");
    }

    [Test]
    public async Task PrintExpression_AndNot_MatchProductSpacing() {
        var printer = new DomainDslPrinter();
        var expr = DomainExpression.And(
            DomainExpression.Property("A"),
            DomainExpression.Not(DomainExpression.Property("B")));

        await Assert.That(printer.PrintTestExpression(expr)).IsEqualTo("(A and not B)");
    }

    [Test]
    public async Task PrintExpression_StringLiteral_QuotesAndEscaping() {
        var printer = new DomainDslPrinter();
        var expr = DomainExpression.Equal(
            DomainExpression.Property("Status"),
            DomainExpression.Literal("say \"hi\""));

        await Assert.That(printer.PrintTestExpression(expr)).IsEqualTo("Status is \"say \\\"hi\\\"\"");
    }

    [Test]
    public async Task Print_FunctionWithParameters_EmitsFunctionForm() {
        var a = new Property("a", new DomainTypeReference("Number"), []);
        var b = new Property("b", new DomainTypeReference("Number"), []);
        var fn = new DomainFunction(
            "Add",
            [a, b],
            new DomainTypeReference("Number"),
            DomainExpression.Add(DomainExpression.Parameter("a"), DomainExpression.Parameter("b")));
        var domain = new Domain("T", []) { Functions = [fn] };

        var printed = new DomainDslPrinter().Print(domain);

        await Assert.That(printed).Contains("function Add(a: Number, b: Number): Number = (a + b)");
    }

    [Test]
    public async Task Print_FunctionWithNoParameters_EmitsEmptyParensAndReturnType() {
        var fn = new DomainFunction(
            "Pi",
            [],
            new DomainTypeReference("Number"),
            DomainExpression.Literal(3L));
        var domain = new Domain("T", []) { Functions = [fn] };

        var printed = new DomainDslPrinter().Print(domain);

        await Assert.That(printed).Contains("function Pi(): Number = 3");
    }

    [Test]
    public async Task Print_DefaultClock_PrintsNowAndToday() {
        var created = new Property("CreatedAt", new DomainTypeReference("DateTime"),
            [new DefaultValueConstraint(new Now())]);
        var opened = new Property("OpenedOn", new DomainTypeReference("Date"),
            [new DefaultValueConstraint(new Today())]);
        var entity = new Entity("Account", [created, opened], [], [], []);
        var domain = new Domain("T", [entity]);
        var session = DomainSession.ForExtensions([ExtensionCatalog.TemporalId]);
        var printed = new DomainDslPrinter(session).Print(domain);

        await Assert.That(printed).Contains("default(Now)");
        await Assert.That(printed).Contains("default(Today)");
        await Assert.That(printed).DoesNotContain("Children");

        var reparsed = new PolyDslParser(printed, session).Parse();
        await Assert.That(reparsed.Count > 0).IsTrue();
    }

    [Test]
    public async Task Parse_CreateIn_AllowsPrimitiveKeywordPropertyName() {
        const string poly = """
            domain D
            Note: entity { Text: Text }
            Book: entity {
              notes: many Note
              Active: stage {
                Mark: action {
                  create in notes { Text: "high usage" }
                }
              }
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        await Assert.That(changes.Count > 0).IsTrue();
    }
}