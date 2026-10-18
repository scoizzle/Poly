using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Required is the default; <c>optional</c> is the explicit opt-out. A <c>create</c> /
/// <c>create in</c> must provide every property that is not <c>optional</c> and has no
/// <c>default(...)</c>. <c>default(X)</c> is the initialization axis (omit at create ⇒ X);
/// <c>optional</c> is the requiredness axis (the value may be absent). They are orthogonal.
/// </summary>
public sealed class OptionalConstraintTests {
    private static EvolutionResult Apply(string dsl) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(dsl).Parse());

    private static bool HasMissingRequired(EvolutionResult result, string property) =>
        result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains($"required property '{property}'", StringComparison.Ordinal));

    [Test]
    public async Task CreateIn_OptionalPropertyOmitted_NoError() {
        var result = Apply("""
            domain Opt
            Box: entity {
              tokens: many Token
              Make: action { create in tokens { } }
            }
            Token: entity { Lexeme: Text optional }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(HasMissingRequired(result, "Lexeme")).IsFalse();
    }

    [Test]
    public async Task CreateIn_UndefaultedNonOptionalPropertyOmitted_IsError() {
        var result = Apply("""
            domain Req
            Box: entity {
              tokens: many Token
              Make: action { create in tokens { } }
            }
            Token: entity { Lexeme: Text }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasMissingRequired(result, "Lexeme")).IsTrue();
    }

    [Test]
    public async Task CreateIn_DefaultedPropertyOmitted_NoError() {
        var result = Apply("""
            domain Def
            Box: entity {
              tokens: many Token
              Make: action { create in tokens { } }
            }
            Token: entity { Lexeme: Text default("x") }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(HasMissingRequired(result, "Lexeme")).IsFalse();
    }

    [Test]
    public async Task Optional_PrintsAndRoundTrips() {
        var result = Apply("domain D\n\nThing: entity {\n  Name: Text optional\n  Count: Number optional\n}");
        await Assert.That(result.Succeeded).IsTrue();
        var printed = new DomainDslPrinter().Print(result.Root!);
        await Assert.That(printed).Contains("optional");

        var reparsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(printed).Parse());
        await Assert.That(reparsed.Succeeded).IsTrue();
    }
}