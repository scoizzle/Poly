using System.Text.RegularExpressions;

using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Row G-25 (#835): statement nesting. Depth is the number of enclosing <c>if</c> and <c>for</c> statements, the
/// statement itself included, and each <c>else if</c> rung is one more level (its then-body sits one level below the
/// previous rung's). The shapes are shared with <see cref="Poly.Tests.Polyc.RecursivePassDepthTests"/>.
/// </summary>
public partial class StatementLimitsTests {
    /// <summary>
    /// A document whose one action nests statements <paramref name="depth"/> deep. <c>nestif</c>: that many nested
    /// <c>if</c> blocks. <c>elseif</c>: one <c>if</c> and <paramref name="depth"/> - 1 <c>else if</c> rungs.
    /// <c>forif</c>: one <c>for</c> holding <paramref name="depth"/> - 1 nested <c>if</c> blocks. <c>combined</c>:
    /// <c>nestif</c> whose innermost condition is 256 nested <c>not</c>s (the expression limit under the statement limit).
    /// </summary>
    internal static string StatementDocument(string shape, int depth) {
        const string head = "domain D\nL: entity { N: Text }\nE: entity {\n  Flag: Boolean\n  Note: Text\n  Lines: many L\n";
        return shape switch {
            "nestif" => head + "  Go: action {\n    " + Ifs(depth) + "assign Note to \"x\"\n" + Closes(depth) + "\n  }\n}\n",
            "elseif" => head + "  Go: action {\n    if (Flag) { assign Note to \"x\" }"
                + string.Concat(Enumerable.Repeat(" else if (Flag) { assign Note to \"x\" }", depth - 1)) + "\n  }\n}\n",
            "forif" => head + "  Go: action {\n    for l in Lines { " + Ifs(depth - 1) + "assign Note to \"x\"\n"
                + Closes(depth - 1) + "}\n  }\n}\n",
            "combined" => head + "  Go: action {\n    " + Ifs(depth - 1) + "if (" + ParserLimitsTests.Nots(DslLimits.MaxExpressionDepth)
                + ") { assign Note to \"x\" }\n" + Closes(depth - 1) + "\n  }\n}\n",
            _ => throw new InvalidOperationException(shape),
        };

        static string Ifs(int n) => string.Concat(Enumerable.Repeat("if (Flag) { ", n));
        static string Closes(int n) => string.Concat(Enumerable.Repeat("} ", n));
    }

    [GeneratedRegex(@"\b(if|for) ")]
    private static partial Regex StatementHead();

    /// <summary>The zero-based offset of the <paramref name="ordinal"/>th (1-based) <c>if</c> or <c>for</c> head in document order.</summary>
    internal static int HeadOffset(string document, int ordinal) => StatementHead().Matches(document)[ordinal - 1].Index;

    /// <summary>The nesting depth of an effect: 1 per <c>if</c> or <c>for</c>, plus the deepest body under it.</summary>
    internal static int Depth(Effect effect) => effect switch {
        ConditionalEffect c => 1 + c.ThenEffects.Concat(c.ElseEffects ?? []).Select(Depth).DefaultIfEmpty(0).Max(),
        ForEachEffect f => 1 + f.Body.Select(Depth).DefaultIfEmpty(0).Max(),
        _ => 0,
    };

    private static IReadOnlyList<Effect> ActionEffects(List<DomainChange> changes) =>
        [.. changes.OfType<AddEffectToActionChange>().Select(c => c.Effect)];

    private static string ParseAndPrint(string document) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(document).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException("did not analyze: " + result.FailureSummary);
        return new DomainDslPrinter().Print(result.Root!);
    }

    // ── characterisation: what the reader does today at 64, 65 and 200 ──────────────────

    [Test]
    [Arguments("nestif", 64)]
    [Arguments("elseif", 64)]
    [Arguments("forif", 64)]
    [Arguments("nestif", 65)]
    [Arguments("elseif", 65)]
    [Arguments("forif", 65)]
    [Arguments("nestif", 200)]
    [Arguments("elseif", 200)]
    [Arguments("forif", 200)]
    public async Task Shape_AtDepth_ParsesToANestingOfThatDepth(string shape, int depth) {
        var effects = ActionEffects(new PolyDslParser(StatementDocument(shape, depth)).Parse());

        await Assert.That(effects.Count).IsEqualTo(1);
        await Assert.That(Depth(effects[0])).IsEqualTo(depth);
    }

    // ── P2: the else-if ladder's shape, which the parse loop must keep ──────────────────

    private const string LadderHead = "domain D\nE: entity {\n  Score: Number\n  Note: Text\n  Grade: action {\n";

    [Test]
    public async Task ElseIfLadder_ParsesToNestedConditionals_AndRoundTripsThroughPrinter() {
        var document = LadderHead
            + "    if (Score >= 90) { assign Note to \"A\" } else if (Score >= 80) { assign Note to \"B\" }"
            + " else if (Score >= 70) { assign Note to \"C\" } else if (Score >= 60) { assign Note to \"D\" }"
            + " else if (Score >= 50) { assign Note to \"E\" }\n  }\n}\n";

        var effects = ActionEffects(new PolyDslParser(document).Parse());
        await Assert.That(effects.Count).IsEqualTo(1);
        var rung = (ConditionalEffect)effects[0];
        var rungs = 1;
        while (rung.ElseEffects is { Count: 1 } next && next[0] is ConditionalEffect inner) {
            await Assert.That(rung.ThenEffects.Count).IsEqualTo(1);
            rung = inner;
            rungs++;
        }
        await Assert.That(rungs).IsEqualTo(5);
        await Assert.That(rung.ElseEffects).IsNull();

        var printed = ParseAndPrint(document);
        await Assert.That(Regex.Count(printed, @"\belse if \(")).IsEqualTo(4);
        var reprinted = ParseAndPrint(printed);
        await Assert.That(reprinted).IsEqualTo(printed);
    }

    /// <summary>Lock amendment 6: a ladder ending in a plain <c>else</c>, and a ladder inside a then-body of another rung.</summary>
    [Test]
    public async Task ElseIfLadder_WithFinalElse_AndLadderInsideARungBody_KeepsItsShape() {
        var document = LadderHead
            + "    if (Score >= 90) { assign Note to \"A\" }"
            + " else if (Score >= 80) {\n      if (Score >= 85) { assign Note to \"B+\" } else if (Score >= 82) { assign Note to \"B\" } else { assign Note to \"B-\" }\n    }"
            + " else { assign Note to \"C\" }\n  }\n}\n";

        var outer = (ConditionalEffect)ActionEffects(new PolyDslParser(document).Parse()).Single();
        var second = (ConditionalEffect)outer.ElseEffects!.Single();
        await Assert.That(second.ElseEffects!.Single()).IsTypeOf<AssignEffect>();
        var nested = (ConditionalEffect)second.ThenEffects.Single();
        var nestedSecond = (ConditionalEffect)nested.ElseEffects!.Single();
        await Assert.That(nestedSecond.ElseEffects!.Single()).IsTypeOf<AssignEffect>();
        await Assert.That(Depth(outer)).IsEqualTo(4);

        var printed = ParseAndPrint(document);
        await Assert.That(Regex.Count(printed, @"\belse if \(")).IsEqualTo(2);
        await Assert.That(Regex.Count(printed, @"\belse \{")).IsEqualTo(2);
        var reprinted = ParseAndPrint(printed);
        await Assert.That(reprinted).IsEqualTo(printed);
    }

    // ── P3: a 'for' inside a 'for' stays rejected, even with an 'if' between them ───────

    [Test]
    [Arguments("for l in Lines { for k in Lines { assign Note to \"x\" } }")]
    [Arguments("for l in Lines { if (Flag) { for k in Lines { assign Note to \"x\" } } }")]
    public async Task ForInsideFor_StillRejected(string body) {
        var document = "domain D\nL: entity { N: Text }\nE: entity {\n  Flag: Boolean\n  Note: Text\n  Lines: many L\n  Go: action {\n    "
            + body + "\n  }\n}\n";

        GrammarException? caught = null;
        try {
            new PolyDslParser(document).Parse();
        }
        catch (GrammarException ex) {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Detail).IsEqualTo("A 'for' inside a 'for' body is not supported");
        await Assert.That(caught.Line).IsEqualTo(8);
        await Assert.That(caught.Column).IsEqualTo(5 + body.IndexOf("for k", StringComparison.Ordinal));
    }
}