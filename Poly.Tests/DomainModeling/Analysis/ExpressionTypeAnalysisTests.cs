using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;

namespace Poly.Tests.DomainModeling.Analysis;

public class ExpressionTypeAnalysisTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).Apply(changes);
    }

    private static bool HasError(EvolutionResult result, string messageContains) =>
        result.Analysis.Diagnostics.Any(d => d.Message.Contains(messageContains, StringComparison.Ordinal)); [Test]
    public async Task Comparison_TextVsNumber_Rejected() {
        var result = Parse("""
            domain Test
            Person: entity {
              Name: Text
              Bad: policy { Name >= 18 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "comparison between incompatible types")).IsTrue();
    }

    [Test]
    public async Task Comparison_EnumVsNumber_Rejected() {
        var result = Parse("""
            domain Test
            Status: enum { On, Off }
            Item: entity {
              Status: Status
              Bad: policy { Status == 5 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Comparison_EnumVsInvalidMember_Rejected() {
        var result = Parse("""
            domain Test
            Status: enum { On, Off }
            Item: entity {
              Status: Status
              Bad: policy { Status == "Bogus" }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "not a member of enum 'Status'")).IsTrue();
    }

    [Test]
    public async Task Comparison_EnumVsValidMember_Succeeds() {
        var result = Parse("""
            domain Test
            Status: enum { On, Off }
            Item: entity {
              Status: Status
              Good: policy { Status == "On" }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Assign_NumberProp_FromTextLiteral_Rejected() {
        var result = Parse("""
            domain Test
            Item: entity {
              Count: Number
              Set: action { assign Count to "hello" }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "type mismatch in assign to property 'Count'")).IsTrue();
    }

    [Test]
    public async Task Arithmetic_StringPlusNumber_Rejected() {
        var result = Parse("""
            domain Test
            Item: entity {
              Name: Text
              Count: Number
              Bad: policy { Name + Count > 0 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Not_OnNumber_Rejected() {
        var result = Parse("""
            domain Test
            Item: entity {
              Count: Number
              Bad: policy { not Count }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Default_TodayOnText_Rejected() {
        var result = Parse("""
            domain Test
            Event: entity { Label: Text default(Today) }
            """);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task CreateIn_NonMemberEnumLiteral_Rejected() {
        // Discovery round5 F4: a non-member enum string in a create-in initializer
        // previously passed analysis and broke the export at compile (CS1503).
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low, Out }
            Bin: entity { Status: StockLevel }
            Box: entity {
              bins: many Bin
              seed: action {
                create in bins { Status: "Bogus" }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "not a member of enum 'StockLevel'")).IsTrue();
    }

    [Test]
    public async Task CreateIn_MemberEnumLiteral_Succeeds() {
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low, Out }
            Bin: entity { Status: StockLevel }
            Box: entity {
              bins: many Bin
              seed: action {
                create in bins { Status: "InStock" }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Invoke_WrongTypedArgument_Rejected() {
        // Discovery round5 F7: a Text expression bound to a Number parameter
        // (invoke line.Mark(amount: line Status)) previously passed analysis
        // and broke the export at compile (CS1503).
        var result = Parse("""
            domain Test
            LineItem: entity {
              Status: Text
              Qty: Number default(0)
              IsOpen: policy { Status is "x" }
              Mark: action (amount: Number) {
                assign Qty to amount
              }
            }
            Invoice: entity {
              lines: many LineItem
              Bad: action {
                for lines as line where line IsOpen invoke line.Mark(amount: line Status)
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "type mismatch in argument 'amount' of invoke 'Mark'")).IsTrue();
    }

    [Test]
    public async Task ForEachInvoke_EntityReturningAction_Rejected() {
        // Discovery round5 F8: fan-out invoking an entity-returning action from a
        // void body previously passed analysis and broke the export at compile (CS0029).
        var result = Parse("""
            domain Test
            LineItem: entity {
              Qty: Number default(0)
              Copy: action -> LineItem {
                create LineItem { Qty: Qty }
              }
            }
            Invoice: entity {
              lines: many LineItem
              Bad: action {
                for lines as line invoke line.Copy()
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "declares a return type")).IsTrue();
    }

    [Test]
    public async Task DateArithmetic_DatePlusNumber_Succeeds() {
        var result = Parse("""
            domain Test
            Loan: entity {
              DueDate: Date
              IsDueSoon: policy { DueDate + 7 > DueDate }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task WellTypedDomain_Succeeds() {
        var result = Parse("""
            domain Test
            Person: entity {
              Age: Number
              Name: Text
              Adult: policy { Age >= 18 }
              HasName: policy { Name != "" }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Assign_NonMemberEnumIdentifier_Rejected() {
        // Round-5 F1 (sibling form): a bare non-member enum identifier on an assign RHS
        // previously passed analysis and failed only at compile (CS1061).
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low, Out }
            Bin: entity {
              Status: StockLevel
              seed: action { assign Status to Bogus }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "not a member of enum 'StockLevel'")).IsTrue();
    }

    [Test]
    public async Task CreateIn_NonMemberEnumIdentifier_Rejected() {
        // Round-5 F1 (sibling form): a bare non-member enum identifier in a create-in
        // initializer — same as the string-literal case but the bare-identifier sibling.
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low, Out }
            Bin: entity { Status: StockLevel }
            Box: entity {
              bins: many Bin
              seed: action { create in bins { Status: Bogus } }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "not a member of enum 'StockLevel'")).IsTrue();
    }

    [Test]
    public async Task Assign_EnumPropToSameTypedProp_Succeeds() {
        // F1 must not over-reject: assigning one enum-typed property to another is valid.
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low }
            Bin: entity {
              Status: StockLevel
              Previous: StockLevel
              seed: action { assign Status to Previous }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Default_NowOnNumber_Rejected() {
        // Round-5 F2/F6: default(Now) on a non-Date target must fail at analysis, not codegen.
        var result = Parse("""
            domain Test
            Item: entity { Qty: Number default(Now) }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "default(Now) is not compatible")).IsTrue();
    }

    [Test]
    public async Task Default_TodayOnNumber_Rejected() {
        var result = Parse("""
            domain Test
            Item: entity { Qty: Number default(Today) }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "default(Today) is not compatible")).IsTrue();
    }

    [Test]
    public async Task ForEachInvoke_ArithmeticOverBinderRoot_Rejected() {
        // Round-5 F3: arithmetic over a binder-root property must be type-checked —
        // a Text binder prop + 1 bound to a Number param is rejected at analysis.
        var result = Parse("""
            domain Test
            Line: entity {
              Qty: Number
              Status: Text
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Go: action {
                for lines as line invoke line.Mark(amount: line Status + 1)
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "cannot assign 'Text' to 'Number'")).IsTrue();
    }

    [Test]
    public async Task ForEachInvoke_NumericArithmeticOverBinderRoot_Succeeds() {
        // Round-5 F3: numeric arithmetic over a binder-root property is valid.
        var result = Parse("""
            domain Test
            Line: entity {
              Qty: Number
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Go: action {
                for lines as line invoke line.Mark(amount: line Qty + 1)
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task CreateIn_NonCatalogLookup_FallsBackGracefully() {
        // Round-5 F7: when the catalog lookup (DomainTypeLookupMetadata) is absent, create-in
        // target resolution must fall back to the plain expression walk without crashing —
        // the enum-membership check is skipped, analysis still runs.
        var dsl = """
            domain Test
            StockLevel: enum { InStock, Low }
            Bin: entity { Status: StockLevel }
            Box: entity {
              bins: many Bin
              seed: action { create in bins { Status: InStock } }
            }
            """;
        var parsed = Parse(dsl);
        var analysis = new Poly.Analysis.AnalyzerBuilder()
            .AddAnalyzer(new DomainCatalogPass())
            .AddAnalyzer(new ExpressionTypeAnalyzer())
            .Build()
            .Analyze(parsed.Root!);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == Poly.Analysis.DiagnosticSeverity.Error)).IsFalse();
    }
}