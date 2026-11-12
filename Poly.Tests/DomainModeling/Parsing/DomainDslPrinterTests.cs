using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Parsing;

// ─── DomainDslPrinter expression print: binder → Grammar Printer → DslTokenWriter ──
// pack-1-3: expr-primary literals/true/false/null/ident print through the table;
// constructs without a printable pattern yet fall back to existing dispatch;
// DateOperation (no binder, no dispatch handler) fails closed.

public sealed class DomainDslPrinterTests {
    [Test]
    public async Task PrintExpression_DateOperation_WithoutBinder_Throws() {
        var printer = new DomainDslPrinter(SessionBuilder.CreateEmpty().LoadCore().Build());
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

    private const string InterleavedDsl = """
        domain Orders
        Customer: entity {
          Name: Text
        }
        Order: entity {
          Id: Text
          Status: stage { }
          Total: Number
          Submit: action { }
          Valid: policy { Total > 0 }
          customer: Customer
        }
        """;

    private static (EvolutionResult Result, string Text) Generate(string poly) {
        var session = DomainSession.Create(DslSyntaxTree.Parse(poly).Uses, SessionOptions.Bare(ExtensionCatalog.Core));
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly, session).Parse(), session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return (result, new DomainDslPrinter(session).Print(result.Root));
    }

    /// <summary>The names of the member lines at one indent level inside <paramref name="entityName"/>'s body.</summary>
    private static IReadOnlyList<string> MemberNames(string text, string entityName) {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, l =>
            l.StartsWith(entityName + ":", StringComparison.Ordinal)
            || l.StartsWith("public entity " + entityName + " ", StringComparison.Ordinal));
        var names = new List<string>();
        for (var i = start + 1; i < lines.Length && lines[i] != "}"; i++) {
            var line = lines[i];
            if (line.Length < 3 || line[0] != ' ' || line[1] != ' ' || line[2] == ' ')
                continue;
            var end = line.IndexOf(':', 2);
            if (end > 0)
                names.Add(line[2..end]);
        }
        return names;
    }

    private static IReadOnlyList<string> EntityOrder(string text) =>
        [.. text.Split('\n').Where(l => l.Length > 0 && l[0] != ' ' && l[0] != '}' && l.Contains("entity", StringComparison.Ordinal))
            .Select(l => l.StartsWith("public entity ", StringComparison.Ordinal)
                ? l["public entity ".Length..].Split(' ')[0]
                : l[..l.IndexOf(':')])];

    [Test]
    public async Task Print_KeepsEntityAndMemberDeclarationOrder() {
        const string zedAlpha = """
            domain D
            Zed: entity {
              Name: Text
            }
            Alpha: entity {
              Zed1: Text
              mgr: Zed
              buddy: Alpha
              Age: Number
            }
            """;
        var (result, text) = Generate(zedAlpha);

        await Assert.That(string.Join(",", EntityOrder(text))).IsEqualTo("Zed,Alpha");
        await Assert.That(string.Join(",", MemberNames(text, "Alpha"))).IsEqualTo("Zed1,mgr,buddy,Age");

        var alpha = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Alpha");
        var before = result.Analysis.GetStructure(alpha)!.ConstructorParameters.Select(p => p.Name).ToList();
        var (reparsed, _) = Generate(text);
        var alpha2 = reparsed.Root.Types.OfType<Entity>().Single(e => e.Name == "Alpha");
        var after = reparsed.Analysis.GetStructure(alpha2)!.ConstructorParameters.Select(p => p.Name).ToList();

        await Assert.That(string.Join(",", after)).IsEqualTo(string.Join(",", before));
    }

    [Test]
    public async Task Print_EntityMembers_FollowDeclarationOrder() {
        const string second = """
            domain Orders
            Customer: entity {
              Name: Text
            }
            Order: entity {
              Id: Text
              Status: stage { }
              Total: Number
              Submit: action { }
              Valid: policy { Total > 0 }
              customer: Customer
            }
            Thing: entity {
              Guard: policy { Size > 0 }
              Size: Number
              Open: stage { }
              Close: action { }
            }
            """;
        var (_, text) = Generate(second);

        await Assert.That(string.Join(",", MemberNames(text, "Order"))).IsEqualTo("Id,Status,Total,Submit,Valid,customer");
        await Assert.That(string.Join(",", MemberNames(text, "Thing"))).IsEqualTo("Guard,Size,Open,Close");
    }

    [Test]
    public async Task Print_EnumsValueTypesAndEntities_KeepModelOrder() {
        const string poly = """
            domain D
            Zeta: enum { A, B }
            Alpha: enum { C, D }
            Zv: value {
              X: Number
            }
            Av: value {
              Y: Number
            }
            Zent: entity {
              N: Number
            }
            Aent: entity {
              M: Number
            }
            """;
        var (result, text) = Generate(poly);

        var enumOrder = text.IndexOf("Zeta", StringComparison.Ordinal) < text.IndexOf("Alpha", StringComparison.Ordinal);
        var valueOrder = text.IndexOf("Zv", StringComparison.Ordinal) < text.IndexOf("Av", StringComparison.Ordinal);
        var entityOrder = text.IndexOf("Zent", StringComparison.Ordinal) < text.IndexOf("Aent", StringComparison.Ordinal);

        await Assert.That(enumOrder).IsTrue();
        await Assert.That(valueOrder).IsTrue();
        await Assert.That(entityOrder).IsTrue();
        await Assert.That(result.Root.ContractBindings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Print_ContractsAndBindings_KeepModelOrder() {
        var (_, text) = Generate("""
            domain D
            Zed: contract internal zed v1 {
              ZReq: value {
                Amount: Number
              }
              Charge: outbound operation ZReq
            }
            Alpha: contract internal alpha v1 {
              AReq: value {
                Amount: Number
              }
              Charge: outbound operation AReq
            }
            Deal: entity {
              Amount: Number
              Active: stage {
                PayZ: action (request: ZReq) {
                  assign Amount to Amount
                }
                PayA: action (request: AReq) {
                  assign Amount to Amount
                }
              }
            }
            ZedBind: bind Zed Charge to PayZ request
            AlphaBind: bind Alpha Charge to PayA request
            """);

        await Assert.That(text.IndexOf("Zed: contract", StringComparison.Ordinal)).IsGreaterThan(-1);
        await Assert.That(text.IndexOf("Zed: contract", StringComparison.Ordinal))
            .IsLessThan(text.IndexOf("Alpha: contract", StringComparison.Ordinal));
        await Assert.That(text.IndexOf("ZedBind:", StringComparison.Ordinal)).IsGreaterThan(-1);
        await Assert.That(text.IndexOf("ZedBind:", StringComparison.Ordinal))
            .IsLessThan(text.IndexOf("AlphaBind:", StringComparison.Ordinal));
    }

    [Test]
    public async Task Print_EntityWithEmptyMemberOrder_FallsBackToListOrder() {
        var (parsed, _) = Generate("""
            domain T
            E: entity {
              Size: Number
              Open: stage { }
              Close: action
                require Big
              { }
              Big: policy { Size > 0 }
            }
            """);
        var source = parsed.Root.Types.OfType<Entity>().Single();
        var bare = source with { MemberOrder = [] };
        var text = new DomainDslPrinter().Print(new Domain("T", [bare]));

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(text).Parse());

        await Assert.That(reparsed.Succeeded).IsTrue().Because(reparsed.FailureSummary ?? "");
        var e2 = reparsed.Root.Types.OfType<Entity>().Single();
        await Assert.That(string.Join(",", EntityDeclarationOrder.Members(e2).Select(m => m.Kind.ToString())))
            .IsEqualTo(string.Join(",", EntityDeclarationOrder.Members(bare).Select(m => m.Kind.ToString())));
    }

    private static string PrintWithImplicitSet(string poly) {
        var session = DomainSession.Create(DslSyntaxTree.Parse(poly).Uses, new SessionOptions(ProductCatalog.Model, ProductCatalog.ImplicitIds));
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(
                DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), ProductCatalog.ImplicitIds, session),
                session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return new DomainDslPrinter(session, ProductCatalog.ImplicitIds).Print(result.Root);
    }

    private static IReadOnlyList<string> UsesLines(string text) =>
        [.. text.Split('\n').Where(l => l.StartsWith("uses ", StringComparison.Ordinal))];

    [Test]
    public async Task Print_DoesNotAddUsesStorage_WhenDocumentDidNotDeclareIt() {
        var text = PrintWithImplicitSet("""
            domain T
            E: entity {
              N: Number
            }
            """);

        await Assert.That(UsesLines(text).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Print_KeepsAuthoredUses_WhenDifferentFromImplicitSet() {
        var text = PrintWithImplicitSet("""
            domain T
            uses persistence
            uses http
            uses storage
            E: entity {
              N: Number
            }
            """);

        await Assert.That(string.Join("|", UsesLines(text))).IsEqualTo("uses persistence|uses http|uses storage");
    }

    [Test]
    public async Task Print_AuthoredUsesInReverseOfImplicitSet_StaysPrinted() {
        var text = PrintWithImplicitSet("""
            domain T
            uses persistence
            uses storage
            E: entity {
              N: Number
            }
            """);

        await Assert.That(string.Join("|", UsesLines(text))).IsEqualTo("uses persistence|uses storage");
    }

    [Test]
    public async Task Print_AuthoredUsesEqualToImplicitSet_IsOmitted_ModelEqual() {
        const string poly = """
            domain T
            uses storage
            uses persistence
            E: entity {
              N: Number
            }
            """;
        var text = PrintWithImplicitSet(poly);

        await Assert.That(UsesLines(text).Count).IsEqualTo(0);

        var tree = DslSyntaxTree.Parse(poly);
        var before = DomainSession.Create(tree.Uses, new SessionOptions(ProductCatalog.Model, ProductCatalog.ImplicitIds));
        var after = DomainSession.Create(DslSyntaxTree.Parse(text).Uses, new SessionOptions(ProductCatalog.Model, ProductCatalog.ImplicitIds));
        await Assert.That(string.Join(",", after.Extensions)).IsEqualTo(string.Join(",", before.Extensions));
    }
}