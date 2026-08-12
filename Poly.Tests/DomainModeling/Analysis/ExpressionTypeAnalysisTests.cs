using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling.Analysis;

public class ExpressionTypeAnalysisTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).Apply(changes);
    }

    private static bool HasError(EvolutionResult result, string messageContains) =>
        result.Analysis.Diagnostics.Any(d => d.Message.Contains(messageContains, StringComparison.Ordinal));

    [Test]
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
            Event: entity { Label: Text default(today) }
            """);
        await Assert.That(result.Succeeded).IsFalse();
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
}