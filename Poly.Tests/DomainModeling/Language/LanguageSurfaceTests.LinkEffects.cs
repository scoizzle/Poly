using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// F-16 (#407, #690): <c>link Rel to binder</c> and <c>unlink Rel from binder</c> are syntax, IR and
/// analysis only. Every valid use is refused with DMEFF020 until L-13 lowers it.
/// </summary>
public sealed partial class LanguageSurfaceTests {
    private const string LinkBase = """
        domain Board
        Task: entity { Name: Text }
        Line: entity { Qty: Number }
        Shelf: entity { Items: many Task }
        Board: entity {
          Note: Text optional
          Backlog: many Task
          Active: many Task
          Lines: many owned Line
          Open: stage {
            Move: action { for t in Backlog { assign Note to "x" } }
            Sweep: action { for l in Lines { assign Note to "y" } }
          }
        }
        """;

    private const string LinkRoundTrip = """
        domain Board
        Task: entity { Name: Text }
        Board: entity {
          Note: Text optional
          Backlog: many Task
          Active: many Task
          Open: stage {
            Move: action {
              for t in Backlog {
                link Active to t
                unlink Backlog from t
              }
            }
          }
        }
        """;

    private const string LinkInConditionalAndFor = """
        domain Board
        Task: entity { Name: Text }
        Board: entity {
          Note: Text optional
          Backlog: many Task
          Active: many Task
          Open: stage {
            Move: action {
              if (Note is "go") {
                for t in Backlog {
                  if (Note is "x") { link Active to t } else { unlink Active from t }
                }
              }
            }
          }
        }
        """;

    private static Domain ApplyUnanalyzed(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            var nested = effect switch {
                ForEachEffect fe => fe.Body,
                ConditionalEffect ce => [.. ce.ThenEffects, .. ce.ElseEffects ?? []],
                CompositeEffect co => co.Effects,
                _ => [],
            };
            foreach (var inner in FlattenEffects(nested))
                yield return inner;
        }
    }

    private static IReadOnlyList<Effect> AllActionEffects(Domain domain) =>
        [.. FlattenEffects(domain.Types.OfType<Entity>().SelectMany(e => e.Stages).SelectMany(s => s.Actions).SelectMany(a => a.Effects))];

    private static Domain RewriteBoardAction(Domain domain, string actionName, Func<IReadOnlyList<Effect>, IReadOnlyList<Effect>> rewrite) {
        var types = domain.Types.Select(t => t is Entity { Name: "Board" } board
            ? board with {
                Stages = board.Stages.Select(s => s with {
                    Actions = s.Actions.Select(a => a.Name == actionName
                        ? a with { Effects = rewrite(a.Effects) }
                        : a).ToList()
                }).ToList()
            }
            : t).ToList();
        return domain with { Types = types };
    }

    private static Domain WithForBody(Domain domain, string actionName, params Effect[] body) =>
        RewriteBoardAction(domain, actionName, effects =>
            [.. effects.Select(e => e is ForEachEffect fe ? fe with { Body = body } : e)]);

    private static Domain WithTopLevel(Domain domain, string actionName, params Effect[] effects) =>
        RewriteBoardAction(domain, actionName, _ => effects);

    private static LinkEffect Link(string relationship, string binder) =>
        new(relationship, DomainExpression.Property(binder));

    private static UnlinkEffect Unlink(string relationship, string binder) =>
        new(relationship, DomainExpression.Property(binder));

    private static IReadOnlyList<Diagnostic> LinkDiagnostics(Domain domain) =>
        [.. DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)];

    private static IReadOnlyList<Diagnostic> BindingErrors(Domain domain) =>
        [.. LinkDiagnostics(domain).Where(d => d.Code == DomainModelDiagnosticCodes.EffectBinding && d.Node is LinkEffect or UnlinkEffect)];

    private static IReadOnlyList<Diagnostic> NotLowerable(Domain domain) =>
        [.. LinkDiagnostics(domain).Where(d => d.Code == DomainModelDiagnosticCodes.EffectLinkNotLowerable)];

    private static GrammarException ParseEffectFails(string effectLine) =>
        Assert.Throws<GrammarException>(() => new PolyDslParser(string.Join("\n",
            "domain D",
            "E: entity {",
            "  Go: action {",
            "    " + effectLine,
            "  }",
            "}")).Parse())!;

    [Test]
    public async Task LinkEffect_RoundTrips() {
        var domain = ApplyUnanalyzed(LinkRoundTrip);
        var link = AllActionEffects(domain).OfType<LinkEffect>().Single();

        await Assert.That(link.RelationshipName).IsEqualTo("Active");
        await Assert.That(link.Target is PropertyAccess { Name: "t" }).IsTrue();

        var printed = new DomainDslPrinter().Print(domain);
        await Assert.That(printed).Contains("link Active to t");
        await Assert.That(new DomainDslPrinter().Print(ApplyUnanalyzed(printed))).IsEqualTo(printed);
    }

    [Test]
    public async Task UnlinkEffect_RoundTrips() {
        var domain = ApplyUnanalyzed(LinkRoundTrip);
        var unlink = AllActionEffects(domain).OfType<UnlinkEffect>().Single();

        await Assert.That(unlink.RelationshipName).IsEqualTo("Backlog");
        await Assert.That(unlink.Target is PropertyAccess { Name: "t" }).IsTrue();

        var printed = new DomainDslPrinter().Print(domain);
        await Assert.That(printed).Contains("unlink Backlog from t");
    }

    [Test]
    public async Task LinkEffect_InConditionalAndForBody_RoundTrips() {
        var domain = ApplyUnanalyzed(LinkInConditionalAndFor);
        var effects = AllActionEffects(domain);

        await Assert.That(effects.OfType<LinkEffect>().Count()).IsEqualTo(1);
        await Assert.That(effects.OfType<UnlinkEffect>().Count()).IsEqualTo(1);

        var printed = new DomainDslPrinter().Print(domain);
        await Assert.That(printed).Contains("link Active to t");
        await Assert.That(printed).Contains("unlink Active from t");
        await Assert.That(new DomainDslPrinter().Print(ApplyUnanalyzed(printed))).IsEqualTo(printed);
    }

    [Test]
    public async Task Link_MissingTo_IsPositionedErrorWithFormHint() {
        var ex = ParseEffectFails("link Active x");

        await Assert.That(ex.Message).Contains("expected '<relationship> to <binder>'");
        await Assert.That(ex.Line).IsEqualTo(4);
        await Assert.That(ex.Column).IsEqualTo(5);
    }

    [Test]
    public async Task Link_MissingRelationship_IsPositionedErrorWithFormHint() {
        var ex = ParseEffectFails("link to t");

        await Assert.That(ex.Message).Contains("expected '<relationship> to <binder>'");
        await Assert.That(ex.Line).IsEqualTo(4);
    }

    [Test]
    public async Task Unlink_WrongPreposition_IsPositionedErrorWithFormHint() {
        var ex = ParseEffectFails("unlink Active to t");

        await Assert.That(ex.Message).Contains("expected '<relationship> from <binder>'");
        await Assert.That(ex.Line).IsEqualTo(4);
        await Assert.That(ex.Column).IsEqualTo(5);
    }

    [Test]
    public async Task Link_MissingTarget_IsPositionedError() {
        var ex = Assert.Throws<GrammarException>(() => new PolyDslParser(string.Join("\n",
            "domain D",
            "E: entity {",
            "  Go: action {",
            "    link Active to",
            "  }",
            "}")).Parse())!;

        await Assert.That(ex.Message).DoesNotContain("Expected effect (");
        await Assert.That(ex.Line).IsGreaterThan(0);
    }

    [Test]
    public async Task Link_UppercaseIdentifier_StaysAnIdentifier() {
        // The keywords are lowercase and case-exact: `Link` is still an ordinary name.
        var domain = ApplyUnanalyzed("""
            domain Board
            Task: entity { Link: Text optional }
            """);

        await Assert.That(domain.Types.OfType<Entity>().Single().Properties.Single().Name).IsEqualTo("Link");
    }

    [Test]
    public async Task LinkTarget_NotABinder_IsPositionedError() {
        var domain = WithTopLevel(ApplyUnanalyzed(LinkBase), "Move", Link("Active", "Note"));

        var errors = BindingErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node is LinkEffect).IsTrue();
        await Assert.That(errors[0].Message).Contains("'for' binder");
        await Assert.That(NotLowerable(domain).Count).IsEqualTo(0);
    }

    [Test]
    public async Task LinkTarget_NonBareExpression_IsPositionedError() {
        var effect = new LinkEffect("Active", DomainExpression.Literal(1L));
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Move", effect);

        var errors = BindingErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(errors[0].Node, effect)).IsTrue();
    }

    [Test]
    public async Task LinkUnknownRelationship_IsPositionedError() {
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Move", Link("Nope", "t"));

        var errors = BindingErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("'Nope'");
        await Assert.That(NotLowerable(domain).Count).IsEqualTo(0);
    }

    [Test]
    public async Task LinkRelationshipOfAnotherEntity_IsPositionedError() {
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Move", Unlink("Items", "t"));

        var errors = BindingErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("'Items'");
        await Assert.That(errors[0].Message).Contains("'Shelf'");
    }

    [Test]
    public async Task LinkTargetTypeMismatch_IsPositionedError() {
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Sweep", Link("Active", "l"));

        var errors = BindingErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("'Line'");
        await Assert.That(errors[0].Message).Contains("'Task'");
        await Assert.That(NotLowerable(domain).Count).IsEqualTo(0);
    }

    [Test]
    public async Task LinkOnOwnedNavigation_IsRefused() {
        var effect = Link("Lines", "l");
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Sweep", effect);

        var errors = BindingErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(errors[0].Node, effect)).IsTrue();
        await Assert.That(errors[0].Message).Contains("'Lines'");
        await Assert.That(errors[0].Message).Contains("owner is fixed");
        await Assert.That(errors[0].Message).Contains("create in");
        await Assert.That(NotLowerable(domain).Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnlinkOnOwnedNavigation_IsRefused() {
        var effect = Unlink("Lines", "l");
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Sweep", effect);

        var errors = BindingErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(errors[0].Node, effect)).IsTrue();
        await Assert.That(errors[0].Message).Contains("owner is fixed");
        await Assert.That(NotLowerable(domain).Count).IsEqualTo(0);
    }

    [Test]
    public async Task LinkEffect_ValidShape_IsRefusedUntilLowering() {
        var link = Link("Active", "t");
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Move", link);

        var refusals = NotLowerable(domain);

        await Assert.That(refusals.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(refusals[0].Node, link)).IsTrue();
        await Assert.That(refusals[0].Message).Contains("L-13");
        await Assert.That(BindingErrors(domain).Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnlinkEffect_ValidShape_IsRefusedUntilLowering() {
        var unlink = Unlink("Backlog", "t");
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Move", unlink);

        var refusals = NotLowerable(domain);

        await Assert.That(refusals.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(refusals[0].Node, unlink)).IsTrue();
    }

    [Test]
    public async Task LinkEffect_InConditionalInForBody_IsRefusedOnce() {
        var link = Link("Active", "t");
        var nested = new ConditionalEffect(DomainExpression.Literal(true), [link], null);
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Move", nested);

        var refusals = NotLowerable(domain);

        await Assert.That(refusals.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(refusals[0].Node, link)).IsTrue();
    }

    [Test]
    public async Task LinkEffect_TargetBinder_ReportsNothingButTheRefusal() {
        // A bare binder is a PropertyAccess: no generic walker may report it as an unknown property.
        var domain = WithForBody(ApplyUnanalyzed(LinkBase), "Move", Link("Active", "t"), Unlink("Backlog", "t"));

        var codes = LinkDiagnostics(domain).Select(d => d.Code).ToList();

        await Assert.That(codes.Count).IsEqualTo(2);
        await Assert.That(codes.All(c => c == DomainModelDiagnosticCodes.EffectLinkNotLowerable)).IsTrue();
    }

    [Test]
    public async Task LinkBase_AnalyzesCleanWithoutLinkEffects() {
        await Assert.That(LinkDiagnostics(ApplyUnanalyzed(LinkBase)).Count).IsEqualTo(0);
    }

    private static string PositionDsl(string entry, string exit, string when) => $$"""
        domain Board
        Task: entity {
          Name: Text
          Draft: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        Board: entity {
          Note: Text optional
          Backlog: many Task
          Active: many Task
          Tracks: Task
          Open: stage {
            entry { {{entry}} }
            exit { {{exit}} }
            when Tracks Done as peer { {{when}} }
          }
        }
        """;

    private static IReadOnlyList<(string Position, string Bare, string InFor)> LinkPositions() => [
        ("entry", "link Active to t", "for t in Backlog { link Active to t }"),
        ("exit", "unlink Active from t", "for t in Backlog { unlink Active from t }"),
        ("when", "link Active to peer", "for t in Backlog { link Active to t }"),
    ];

    private static string PositionDslFor(string position, string effect) => position switch {
        "entry" => PositionDsl(effect, "assign Note to \"x\"", "assign Note to \"y\""),
        "exit" => PositionDsl("assign Note to \"x\"", effect, "assign Note to \"y\""),
        _ => PositionDsl("assign Note to \"x\"", "assign Note to \"y\"", effect),
    };

    private static IReadOnlyList<Diagnostic> PositionErrors(string dsl) =>
        LinkDiagnostics(ApplyUnanalyzed(dsl));

    [Test]
    public async Task LinkBare_InEntryExitAndWhenBodies_IsBinderError() {
        foreach (var (position, bare, _) in LinkPositions()) {
            var errors = PositionErrors(PositionDslFor(position, bare));

            await Assert.That(errors.Count(d => d.Code == DomainModelDiagnosticCodes.EffectBinding && d.Node is LinkEffect or UnlinkEffect))
                .IsEqualTo(1).Because(position);
            await Assert.That(errors.Count(d => d.Code == DomainModelDiagnosticCodes.EffectLinkNotLowerable))
                .IsEqualTo(0).Because(position);
        }
    }

    [Test]
    public async Task LinkInFor_InEntryExitAndWhenBodies_IsRefusedOnce() {
        foreach (var (position, _, inFor) in LinkPositions()) {
            var errors = PositionErrors(PositionDslFor(position, inFor));

            await Assert.That(errors.Count(d => d.Code == DomainModelDiagnosticCodes.EffectLinkNotLowerable))
                .IsEqualTo(1).Because(position);
            await Assert.That(errors.Count(d => d.Code == DomainModelDiagnosticCodes.EffectBinding && d.Node is LinkEffect or UnlinkEffect))
                .IsEqualTo(0).Because(position);
        }
    }

    [Test]
    public async Task LinkAndUnlink_InEntryExitAndWhenBodies_RoundTrip() {
        foreach (var (position, bare, inFor) in LinkPositions()) {
            foreach (var effect in new[] { bare, inFor }) {
                var printed = new DomainDslPrinter().Print(ApplyUnanalyzed(PositionDslFor(position, effect)));

                await Assert.That(printed).Contains(effect.Split(' ')[0] == "for" ? "for t in Backlog" : effect).Because(position);
                await Assert.That(new DomainDslPrinter().Print(ApplyUnanalyzed(printed))).IsEqualTo(printed).Because(position);
            }
        }
    }

    /// <summary>
    /// F-16 reserves lowercase <c>link</c> and <c>unlink</c> as hard keywords, so they cannot name
    /// anything. G-21 (contextual keywords) flips these to parse cleanly; delete or invert this pin then.
    /// </summary>
    [Test]
    [Arguments("Task: entity { link: Text }", "link", 1, 16)]
    [Arguments("Task: entity { unlink: Text }", "unlink", 1, 16)]
    [Arguments("Task: entity { Name: Text }\nBoard: entity { link: many Task }", "link", 2, 17)]
    [Arguments("Task: entity { Name: Text }\nBoard: entity { Open: stage { }\n  unlink: stage { } }", "unlink", 3, 3)]
    [Arguments("Task: entity { link: action { } }", "link", 1, 16)]
    [Arguments("Task: entity { Go: action (link: Text) { } }", "link", 1, 28)]
    public async Task LinkAndUnlink_AsNames_FailToParseWithAPositionedError(string body, string token, int line, int column) {
        var ex = ParseFails("domain D\n" + body);

        await Assert.That(ex.Message).Contains(token);
        await Assert.That(ex.Line).IsEqualTo(line + 1);
        await Assert.That(ex.Column).IsEqualTo(column);
    }

    [Test]
    [Arguments("domain link\nTask: entity { Name: Text }")]
    [Arguments("domain D\nlink: entity { Name: Text }")]
    [Arguments("domain D\nKind: enum { link, other }")]
    public async Task LinkAsEntityDomainOrEnumValueName_FailsToParse(string dsl) {
        var ex = ParseFails(dsl);

        await Assert.That(ex.Message).Contains("link");
        await Assert.That(ex.Line).IsGreaterThan(0);
    }
}