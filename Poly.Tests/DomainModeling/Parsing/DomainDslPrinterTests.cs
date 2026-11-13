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

    internal static string PrintWithImplicitSet(string poly) {
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

/// <summary>
/// G-03 characterisation: what the printer writes today for a number, per site. <see cref="Stable"/> rows must NOT move when
/// <c>DslNumberText</c> lands; <see cref="Moving"/> rows record today's text for the inputs this slice changes.
/// </summary>
public sealed class DslNumberPrintPinTests {
    private static readonly DomainDslPrinter Printer = new();

    private static string Literal(object value) => Printer.PrintTestExpression(DomainExpression.Literal(value));

    private static string Bound(object value) => Printer.PrintConstraintText(new RangeConstraint(value, value));

    private static string Equality(object value) => Printer.PrintConstraintText(new EqualityConstraint(value));

    /// <summary>(label, value, literal text, range text, equals text) that stay as they are.</summary>
    internal static IEnumerable<(string, object, string, string, string)> Stable() {
        yield return ("long 3", 3L, "3", "range(3, 3)", "equals(3)");
        yield return ("int 3", 3, "3", "range(3, 3)", "equals(3)");
        yield return ("long.MinValue", long.MinValue, "-9223372036854775808", "range(-9223372036854775808, -9223372036854775808)", "equals(-9223372036854775808)");
        yield return ("decimal 1.5", 1.5m, "1.5", "range(1.5, 1.5)", "equals(1.5)");
        yield return ("decimal 1.50", 1.50m, "1.50", "range(1.50, 1.50)", "equals(1.50)");
        yield return ("decimal 1.0", 1.0m, "1.0", "range(1.0, 1.0)", "equals(1.0)");
        yield return ("decimal 1e15", 1e15m, "1000000000000000", "range(1000000000000000, 1000000000000000)", "equals(1000000000000000)");
        yield return ("decimal 1e-7", 0.0000001m, "0.0000001", "range(0.0000001, 0.0000001)", "equals(0.0000001)");
        yield return ("decimal.MaxValue", decimal.MaxValue, "79228162514264337593543950335", "range(79228162514264337593543950335, 79228162514264337593543950335)", "equals(79228162514264337593543950335)");
        yield return ("decimal.MinValue", decimal.MinValue, "-79228162514264337593543950335", "range(-79228162514264337593543950335, -79228162514264337593543950335)", "equals(-79228162514264337593543950335)");
        yield return ("double 2.5", 2.5d, "2.5", "range(2.5, 2.5)", "equals(2.5)");
        yield return ("double 3 (integral)", 3d, "3.0", "range(3, 3)", "equals(3.0)");
        yield return ("double 0.1", 0.1d, "0.1", "range(0.1, 0.1)", "equals(0.1)");
        yield return ("double 1e-7", 1e-7d, "0.0000001", "range(1E-07, 1E-07)", "equals(0.0000001)");
        yield return ("double 1e15", 1e15d, "1000000000000000.0", "range(1000000000000000, 1000000000000000)", "equals(1000000000000000.0)");
        yield return ("double 1e21", 1e21d, "1000000000000000000000.0", "range(1E+21, 1E+21)", "equals(1000000000000000000000.0)");
    }

    /// <summary>Inputs this slice moves, with today's text. <c>null</c> in a column means that site is not pinned for the input.</summary>
    internal static IEnumerable<(string, object, string, string, string)> Moving() {
        yield return ("double 5e-324", 5e-324d, "0.0", "range(5E-324, 5E-324)", "equals(0.0)");
        yield return ("double -0.0", -0.0d, "-0.0", "range(-0, -0)", "equals(-0.0)");
        yield return ("double 0.1+0.2", 0.1d + 0.2d, "0.3", "range(0.30000000000000004, 0.30000000000000004)", "equals(0.3)");
        yield return ("double 123456789.123456789", 123456789.123456789d, "123456789.123457", "range(123456789.12345679, 123456789.12345679)", "equals(123456789.123457)");
        yield return ("float 1e15", 1e15f, "1E+15", "range(1E+15, 1E+15)", "equals(1E+15)");
        yield return ("double NaN", double.NaN, "NaN", "range(NaN, NaN)", "equals(NaN)");
        yield return ("double +Infinity", double.PositiveInfinity, "Infinity", "range(Infinity, Infinity)", "equals(Infinity)");
        yield return ("double -Infinity", double.NegativeInfinity, "-Infinity", "range(-Infinity, -Infinity)", "equals(-Infinity)");
    }

    [Test]
    public async Task StableRows_PrintTheSameAsToday() {
        var seen = 0;
        foreach (var (label, value, literal, bound, equality) in Stable()) {
            await Assert.That($"{label}: {Literal(value)}").IsEqualTo($"{label}: {literal}");
            await Assert.That($"{label}: {Bound(value)}").IsEqualTo($"{label}: {bound}");
            await Assert.That($"{label}: {Equality(value)}").IsEqualTo($"{label}: {equality}");
            seen++;
        }
        await Assert.That(seen).IsEqualTo(16);
    }

    [Test]
    public async Task MovingRows_TodayText() {
        var seen = 0;
        foreach (var (label, value, literal, bound, equality) in Moving()) {
            await Assert.That($"{label}: {Literal(value)}").IsEqualTo($"{label}: {literal}");
            await Assert.That($"{label}: {Bound(value)}").IsEqualTo($"{label}: {bound}");
            await Assert.That($"{label}: {Equality(value)}").IsEqualTo($"{label}: {equality}");
            seen++;
        }
        await Assert.That(seen).IsEqualTo(8);
    }

    [Test]
    public async Task Double_MaxValue_TodayPrintsApproximateDigits() {
        // Today: 15 significant digits then zeros (the value is lost), and the reader cannot hold 309 digits.
        var text = Literal(double.MaxValue);
        await Assert.That(text.StartsWith("17976931348623", StringComparison.Ordinal) && text.EndsWith(".0", StringComparison.Ordinal)).IsTrue();
        await Assert.That(Bound(double.MaxValue)).IsEqualTo("range(1.7976931348623157E+308, 1.7976931348623157E+308)");
    }

    [Test]
    public async Task NonNumericSites_PrintTheSameAsToday() {
        await Assert.That(Printer.PrintConstraintText(new LengthConstraint(1, 5))).IsEqualTo("length(1, 5)");
        await Assert.That(Printer.PrintConstraintText(new LengthConstraint(3, 3))).IsEqualTo("length(3)");
        // A missing bound prints empty (`range(0, )`): kept as it is.
        await Assert.That(Printer.PrintConstraintText(new RangeConstraint(0L, null))).IsEqualTo("range(0, )");
    }

    [Test]
    public async Task ParsedExponentRepro_PrintsPlainDigitsToday() {
        // #531's literal repro no longer yields an exponent on master: the reader stores a Number bound as long.
        var text = DomainDslPrinterTests.PrintWithImplicitSet("domain T\nA: entity {\n  N: Number range(0, 1000000000000000)\n}\n");
        await Assert.That(text).Contains("range(0, 1000000000000000)");
    }
}