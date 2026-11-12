using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

public class EntityLevelActionHintTests {
    [Test]
    public async Task EntityLevel_ActionBesideStage_Hints() {
        var result = HintFixture.Parse("""
            domain Test
            Cart: entity {
              Items: Number
              CheckedOut: stage {
                Pay: action { assign Items to 0 }
              }
              AddItem: action { assign Items to Items + 1 }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(HintFixture.HasError(result, "AddItem")).IsTrue();
        await Assert.That(result.Analysis.Diagnostics.Count(d =>
            d.Code == DomainModelDiagnosticCodes.EntityLevelAction)).IsEqualTo(1);
    }

    [Test]
    public async Task StageAction_Only_NoEntityLevelHint() {
        var result = HintFixture.Parse("""
            domain Test
            Cart: entity {
              Items: Number
              CheckedOut: stage {
                AddItem: action { assign Items to Items + 1 }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EntityLevelAction)).IsFalse();
    }
}

file static class HintFixture {
    internal static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).ApplyAndAnalyze(changes);
    }

    internal static bool HasError(EvolutionResult result, string messageContains) =>
        result.Analysis.Diagnostics.Any(d => d.Message.Contains(messageContains, StringComparison.Ordinal));
}

public class AuthoringSuggestionTests {
    private static readonly string[] BannedTokens = [
        "add(", "kind:", "apply_dsl", "export_dsl", "validate_dsl", "poly_",
        "add_policy", "add_stage", "add_action_to_stage", "get_domain_overview",
    ];

    private static IEnumerable<string> AuthoringHints(EvolutionResult result) =>
        result.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.AuthoringSuggestion)
            .Select(d => d.Message);

    [Test]
    public async Task Hints_NameNoRemovedTool() {
        var result = HintFixture.Parse("""
            domain Test
            Task: entity {
              Title: Text
              Note: Text
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        var hints = AuthoringHints(result).ToList();
        await Assert.That(hints.Count).IsEqualTo(1);
        var message = hints[0];
        await Assert.That(message).DoesNotContain("add(");
        await Assert.That(message).DoesNotContain("apply_dsl");
        await Assert.That(message).DoesNotContain("kind:");
        await Assert.That(message).Contains("Draft: stage {");
    }

    [Test]
    public async Task Hints_MissingActions_NamesTheDeclarationForm_NotATool() {
        var result = HintFixture.Parse("""
            domain Test
            Flow: entity {
              Label: Text
              Active: stage { }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        var hints = AuthoringHints(result).ToList();
        await Assert.That(hints.Count).IsEqualTo(1);
        var message = hints[0];
        await Assert.That(message).Contains("Active: stage {");
        await Assert.That(message).Contains("Submit: action {");
        foreach (var banned in BannedTokens)
            await Assert.That(message).DoesNotContain(banned);
    }

    [Test]
    public async Task Hints_MissingPolicy_NamesTheDeclarationForm_NotATool() {
        var result = HintFixture.Parse("""
            domain Test
            Order: entity {
              Paid: Boolean
              Open: stage { Submit: action { } }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        var hints = AuthoringHints(result).ToList();
        await Assert.That(hints.Count).IsEqualTo(1);
        var message = hints[0];
        await Assert.That(message).Contains("Eligible: policy {");
        foreach (var banned in BannedTokens)
            await Assert.That(message).DoesNotContain(banned);
    }

    [Test]
    public async Task Hints_NoHintNamesATool_CoreWide() {
        var result = HintFixture.Parse("""
            domain Test
            Task: entity {
              Title: Text
              Note: Text
            }
            Flow: entity {
              Label: Text
              Active: stage { }
            }
            Order: entity {
              Paid: Boolean
              Open: stage { Submit: action { } }
            }
            Ticket: entity {
              Total: Number
              Valid: policy { Total > 0 }
              Idle: stage { }
              Touch: action { }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        var diagnostics = result.Analysis.Diagnostics;
        await Assert.That(diagnostics.Count(d => d.Code == DomainModelDiagnosticCodes.AuthoringSuggestion)).IsEqualTo(3);
        await Assert.That(diagnostics.Count(d => d.Code == DomainModelDiagnosticCodes.EntityLevelAction)).IsEqualTo(1);
        await Assert.That(diagnostics.Count(d => d.Code == DomainModelDiagnosticCodes.UnconditionalAction)).IsEqualTo(1);

        foreach (var hint in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Hint))
            foreach (var banned in BannedTokens)
                await Assert.That(hint.Message).DoesNotContain(banned);
    }
}