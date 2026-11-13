using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// G-28: match failures reported by the table. <see cref="Matcher{TToken,TTokenKind}.Match"/> reports the furthest
/// failure and what was expected there, by label, in declaration order; a <see cref="Cut{TToken,TTokenKind}"/>
/// commits a row once its head has matched (a longer sibling rescues it, nothing else does, and an unrescued commit
/// aborts the call); a <see cref="Refuse{TToken,TTokenKind}"/> row matches and yields its message. Synthetic grammars
/// over the shared <see cref="TestTokenizer"/>.
/// </summary>
public sealed class GrammarFailureTests {
    private static GrammarBuilder<TestToken, TestKind> Table() => new();

    private static MatchOutcome<TestToken, TestKind> Match(Grammar<TestToken, TestKind> grammar, string rule, string text) =>
        new Matcher<TestToken, TestKind>(grammar, new TestTokenizer(text)).Match(rule);

    private static MatchResult<TestToken, TestKind>? TryMatch(Grammar<TestToken, TestKind> grammar, string rule, string text) =>
        new Matcher<TestToken, TestKind>(grammar, new TestTokenizer(text)).TryMatch(rule);

    private static string Expected(MatchFailure<TestToken, TestKind> failure) =>
        string.Join(" | ", failure.Expected.Select(e => e.Label ?? e.Kind.ToString()));

    private static bool IsWord(TestToken t, string text) => t.Kind == TestKind.Identifier && t.Text == text;

    private static string Canonical(TestKind kind) => kind switch {
        TestKind.Colon => ":",
        TestKind.Comma => ",",
        TestKind.LBrace => "{",
        TestKind.RBrace => "}",
        TestKind.Entity => "entity",
        TestKind.Stage => "stage",
        _ => kind.ToString(),
    };

    // ── furthest failure and the expected set ────────────────────────────────────────

    [Test]
    public async Task Failure_FurthestOffset_Wins() {
        var grammar = Table().Define("r")
            .Pattern("deep").Kind(TestKind.Entity).Kind(TestKind.Colon).Kind(TestKind.Identifier).Commit()
            .Pattern("shallow").Kind(TestKind.Entity).Kind(TestKind.Stage).Commit()
            .Build();

        var outcome = Match(grammar, "r", "entity : 5");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(outcome.Result).IsNull();
        await Assert.That(outcome.Failure!.Offset).IsEqualTo(2);
        await Assert.That(outcome.Failure.Token).IsEqualTo(new TestToken(TestKind.Number, "5"));
        await Assert.That(Expected(outcome.Failure)).IsEqualTo("Identifier");
        await Assert.That(outcome.Failure.Committed).IsFalse();
        await Assert.That((outcome.Failure.Rule, outcome.Failure.Pattern, outcome.Failure.Element)).IsEqualTo(("r", "deep", 2));
    }

    [Test]
    public async Task Failure_SameOffset_UnionsInDeclarationOrder_NotEvaluationOrder() {
        var grammar = Table().Define("r")
            .Pattern("short").Kind(TestKind.Entity).Kind(TestKind.Stage).Expect("'stage'").Commit()
            .Pattern("long").Kind(TestKind.Entity).Kind(TestKind.Action).Expect("'action'").Kind(TestKind.Identifier).Commit()
            .Build();

        // The matcher evaluates the longer row first (SortPatterns), so evaluation order is the reverse of declaration order.
        await Assert.That(string.Join(",", grammar.GetPatterns("r").Select(p => p.Name))).IsEqualTo("long,short");

        var outcome = Match(grammar, "r", "entity 5");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(Expected(outcome.Failure!)).IsEqualTo("'stage' | 'action'");
        await Assert.That(outcome.Failure.Pattern).IsEqualTo("short");
    }

    [Test]
    public async Task Failure_NoLabel_UsesKind() {
        var grammar = Table().Define("r").Pattern("p").Kind(TestKind.Entity).Kind(TestKind.Stage).Commit().Build();

        var outcome = Match(grammar, "r", "entity x");

        await Assert.That(outcome.Failure).IsNotNull();
        var expectation = outcome.Failure!.Expected.Single();
        await Assert.That(expectation.Label).IsNull();
        await Assert.That(expectation.Kind).IsEqualTo(TestKind.Stage);
        await Assert.That(expectation.IsContent).IsFalse();
    }

    [Test]
    public async Task Failure_AtAnOptionalAndTheNextElement_UnionsInElementOrder() {
        var grammar = Table().Define("r")
            .Pattern("p").Kind(TestKind.Entity).Optional(TestKind.Colon).Value(TestKind.Identifier, "name").Expect("entity name").Commit()
            .Build();

        var outcome = Match(grammar, "r", "entity 5");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(Expected(outcome.Failure!)).IsEqualTo("Colon | entity name");
        await Assert.That(string.Join(",", outcome.Failure.Expected.Select(e => e.IsContent))).IsEqualTo("False,True");
    }

    [Test]
    public async Task Failure_WhereTheFurthestFailureNamesNothing_HasAnEmptyExpectedSet() {
        var lookahead = Table().Define("r").Pattern("p").Kind(TestKind.Entity).NotFollowedBy(TestKind.Colon).Kind(TestKind.Identifier).Commit().Build();
        var predicate = Table().Define("r").Pattern("p").Kind(TestKind.Entity).Predicate(_ => false, "x").Commit().Build();

        var afterLookahead = Match(lookahead, "r", "entity :");
        var afterPredicate = Match(predicate, "r", "entity a");

        await Assert.That(afterLookahead.Failure).IsNotNull();
        await Assert.That(afterLookahead.Failure!.Offset).IsEqualTo(1);
        await Assert.That(afterLookahead.Failure.Expected).IsEmpty();
        await Assert.That(afterPredicate.Failure).IsNotNull();
        await Assert.That(afterPredicate.Failure!.Offset).IsEqualTo(1);
        await Assert.That(afterPredicate.Failure.Expected).IsEmpty();
    }

    [Test]
    public async Task Failure_ThroughMemoHit_StillContributes() {
        var evaluations = 0;
        var grammar = Table()
            .Define("inner")
                .Pattern("p").Predicate(t => { evaluations++; return t.Kind == TestKind.Entity; }, "head")
                .Value(TestKind.Identifier, "name").Expect("inner name").Commit()
            .Define("top")
                .Pattern("comma").Ref("inner").Kind(TestKind.Comma).Commit()
                .Pattern("colon").Ref("inner").Kind(TestKind.Colon).Commit()
            .Build();

        var outcome = Match(grammar, "top", "entity 5");

        // Both rows reach `inner` at offset 0; the second is a memo hit (the predicate runs once).
        await Assert.That(evaluations).IsEqualTo(1);
        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(outcome.Failure!.Offset).IsEqualTo(1);
        await Assert.That(Expected(outcome.Failure)).IsEqualTo("inner name");
        await Assert.That(outcome.Failure.Rule).IsEqualTo("inner");
    }

    [Test]
    public async Task Match_OnACutFreeGrammar_MatchesWhatTryMatchMatches() {
        var grammar = Table().Define("r")
            .Pattern("a").Kind(TestKind.Entity).Value(TestKind.Identifier, "name").Commit()
            .Pattern("b").Kind(TestKind.Entity).Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Commit()
            .Build();

        var matched = Match(grammar, "r", "entity x : y");
        var tried = TryMatch(grammar, "r", "entity x : y");

        await Assert.That(matched.Failure).IsNull();
        await Assert.That(matched.Result!.PatternName).IsEqualTo(tried!.PatternName);
        await Assert.That(matched.Result.Consumed).IsEqualTo(tried.Consumed);
    }

    // ── Cut ──────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Cut_TailFailure_ReturnsACommittedFailure() {
        var cut = Table().Define("r").Pattern("p").Kind(TestKind.Entity).Cut().Value(TestKind.Identifier, "name").Expect("entity name").Commit().Build();
        var uncut = Table().Define("r").Pattern("p").Kind(TestKind.Entity).Value(TestKind.Identifier, "name").Expect("entity name").Commit().Build();

        var outcome = Match(cut, "r", "entity 5");

        // Without the table's report, a failed match carries nothing.
        await Assert.That(TryMatch(uncut, "r", "entity 5")).IsNull();
        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(outcome.Failure!.Committed).IsTrue();
        await Assert.That(outcome.Failure.Offset).IsEqualTo(1);
        await Assert.That(Expected(outcome.Failure)).IsEqualTo("entity name");
    }

    [Test]
    public async Task Cut_LongerSiblingMatch_RescuesTheCommit() {
        var grammar = Table().Define("r")
            .Pattern("committed").Kind(TestKind.Entity).Cut().Kind(TestKind.Stage).Commit()
            .Pattern("longer").Kind(TestKind.Entity).Kind(TestKind.Identifier).Kind(TestKind.Colon).Commit()
            .Build();

        var outcome = Match(grammar, "r", "entity x :");

        await Assert.That(outcome.Failure).IsNull();
        await Assert.That(outcome.Result!.PatternName).IsEqualTo("longer");
    }

    [Test]
    public async Task Cut_ShorterCatchAllSibling_DoesNotRescue() {
        var grammar = Table().Define("r")
            .Pattern("committed").Kind(TestKind.Entity).Kind(TestKind.Colon).Cut().Value(TestKind.Identifier, "name").Expect("name").Commit()
            .Pattern("catch-all").Kind(TestKind.Entity).Commit()
            .Pattern("as-long").Kind(TestKind.Entity).Kind(TestKind.Colon).Commit()
            .Build();

        var outcome = Match(grammar, "r", "entity : 5");

        // The committed row had matched two tokens; a sibling that matches one, or exactly two, does not rescue it.
        await Assert.That(TryMatch(Table().Define("r").Pattern("as-long").Kind(TestKind.Entity).Kind(TestKind.Colon).Commit().Build(), "r", "entity : 5")!.Consumed).IsEqualTo(2);
        await Assert.That(outcome.Result).IsNull();
        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(outcome.Failure!.Committed).IsTrue();
        await Assert.That(Expected(outcome.Failure)).IsEqualTo("name");
    }

    [Test]
    public async Task Cut_ACommittedFailureEscapingItsRule_AbortsTheCall_OuterSiblingsAreNotTried() {
        var siblingTried = 0;
        var grammar = Table()
            .Define("inner").Pattern("p").Kind(TestKind.Entity).Cut().Kind(TestKind.Stage).Commit()
            .Define("top")
                .Pattern("via-inner").Ref("inner").Kind(TestKind.Colon).Kind(TestKind.Colon).Commit()
                .Pattern("sibling").Predicate(t => { siblingTried++; return t.Kind == TestKind.Entity; }, "head").Kind(TestKind.Identifier).Commit()
            .Build();

        // The row through `inner` is evaluated first (more elements), so an abort there stops the rule.
        await Assert.That(string.Join(",", grammar.GetPatterns("top").Select(p => p.Name))).IsEqualTo("via-inner,sibling");

        var outcome = Match(grammar, "top", "entity x");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(outcome.Result).IsNull();
        await Assert.That(siblingTried).IsEqualTo(0);
        await Assert.That(outcome.Failure!.Committed).IsTrue();
        await Assert.That((outcome.Failure.Rule, Expected(outcome.Failure))).IsEqualTo(("inner", "Stage"));
    }

    [Test]
    public async Task Cut_InsideRepeatAndOptional_Propagates_WhereAPlainFailureStopsTheLoop() {
        static Grammar<TestToken, TestKind> List(bool cut) {
            var item = new GrammarBuilder<TestToken, TestKind>().Define("item").Pattern("p").Kind(TestKind.Identifier);
            if (cut)
                item = item.Cut();
            return item.Kind(TestKind.Colon).Kind(TestKind.Number).Commit()
                .Define("list").Pattern("p").Kind(TestKind.LBrace).Repeat("item").Kind(TestKind.RBrace).Commit()
                .Define("opt").Pattern("p").Kind(TestKind.LBrace).Optional(new Ref<TestToken, TestKind>("item")).Kind(TestKind.RBrace).Commit()
                .Build();
        }

        var plainRepeat = Match(List(cut: false), "list", "{ a : 1 b }");
        var cutRepeat = Match(List(cut: true), "list", "{ a : 1 b }");
        var plainOptional = Match(List(cut: false), "opt", "{ b }");
        var cutOptional = Match(List(cut: true), "opt", "{ b }");

        await Assert.That(plainRepeat.Failure).IsNotNull();
        await Assert.That(cutRepeat.Failure).IsNotNull();
        await Assert.That(plainOptional.Failure).IsNotNull();
        await Assert.That(cutOptional.Failure).IsNotNull();
        // Plain: the loop stops before `b`, then `}` is expected at `b` (offset 4) and the item's `:` at offset 5: the furthest wins.
        await Assert.That((plainRepeat.Failure!.Committed, plainRepeat.Failure.Offset, Expected(plainRepeat.Failure))).IsEqualTo((false, 5, "Colon"));
        await Assert.That((cutRepeat.Failure!.Committed, cutRepeat.Failure.Offset, Expected(cutRepeat.Failure))).IsEqualTo((true, 5, "Colon"));
        await Assert.That((plainOptional.Failure!.Committed, plainOptional.Failure.Offset)).IsEqualTo((false, 2));
        await Assert.That((cutOptional.Failure!.Committed, cutOptional.Failure.Offset, Expected(cutOptional.Failure))).IsEqualTo((true, 2, "Colon"));
    }

    [Test]
    public async Task Cut_PostCutRefFailure_ThroughAMemoHit_ReportsTheFailureInsideTheRef() {
        var grammar = Table()
            .Define("type").Pattern("p").Kind(TestKind.Identifier).Kind(TestKind.Colon).Expect("':'").Commit()
            .Define("top")
                .Pattern("plain").Kind(TestKind.Entity).Ref("type").Kind(TestKind.Comma).Kind(TestKind.Comma).Commit()
                .Pattern("committed").Kind(TestKind.Entity).Cut().Ref("type").Commit()
            .Build();

        // The plain row is evaluated first and memoises `type` at offset 1, so the committed row's Ref is a memo hit.
        await Assert.That(grammar.GetPatterns("top")[0].Name).IsEqualTo("plain");

        var outcome = Match(grammar, "top", "entity a 5");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(outcome.Failure!.Committed).IsTrue();
        await Assert.That(outcome.Failure.Offset).IsEqualTo(2);
        await Assert.That(Expected(outcome.Failure)).IsEqualTo("':'");
    }

    [Test]
    public async Task Cut_ThenLeftAssoc_ATrailingOperatorIsACommittedFailureAtTheMissingOperand() {
        var grammar = Table()
            .Define("atom").Pattern("p").Kind(TestKind.Identifier).Commit()
            .Define("r").Pattern("p").Kind(TestKind.Entity).Cut().LeftAssoc("atom", TestKind.Plus).Commit()
            .Build();

        var outcome = Match(grammar, "r", "entity a +");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That((outcome.Failure!.Committed, outcome.Failure.Offset, Expected(outcome.Failure))).IsEqualTo((true, 3, "Identifier"));
    }

    [Test]
    public async Task Cut_ThenBalanced_ExpectsTheOpenOrTheCloseKind() {
        var grammar = Table().Define("r").Pattern("p").Kind(TestKind.Entity).Cut().Balanced(TestKind.LBrace, TestKind.RBrace).Commit().Build();

        var noOpen = Match(grammar, "r", "entity a");
        var noClose = Match(grammar, "r", "entity { a");

        await Assert.That(noOpen.Failure).IsNotNull();
        await Assert.That(noClose.Failure).IsNotNull();
        await Assert.That((noOpen.Failure!.Committed, noOpen.Failure.Offset, Expected(noOpen.Failure))).IsEqualTo((true, 1, "LBrace"));
        await Assert.That((noClose.Failure!.Committed, noClose.Failure.Offset, Expected(noClose.Failure))).IsEqualTo((true, 3, "RBrace"));
    }

    [Test]
    public async Task Cut_ThenNotFollowedBy_IsACommittedFailureThatNamesNothing() {
        var grammar = Table().Define("r").Pattern("p").Kind(TestKind.Entity).Cut().NotFollowedBy(TestKind.Colon).Kind(TestKind.Identifier).Commit().Build();

        var outcome = Match(grammar, "r", "entity :");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That((outcome.Failure!.Committed, outcome.Failure.Offset)).IsEqualTo((true, 1));
        await Assert.That(outcome.Failure.Expected).IsEmpty();
    }

    [Test]
    public async Task Failure_CommittedAndPlainMixed_ReportsTheCommittedFailureOnly() {
        var grammar = Table()
            .Define("inner").Pattern("p").Kind(TestKind.Entity).Cut().Kind(TestKind.Stage).Commit()
            .Define("top")
                .Pattern("deep-plain").Kind(TestKind.Entity).Kind(TestKind.Identifier).Kind(TestKind.Colon).Kind(TestKind.Number).Commit()
                .Pattern("committed").Ref("inner").Commit()
            .Build();

        // `deep-plain` fails at offset 3 (a further plain failure); `inner` commits at offset 1 and nothing rescues it.
        var outcome = Match(grammar, "top", "entity x : y");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That((outcome.Failure!.Committed, outcome.Failure.Offset, Expected(outcome.Failure))).IsEqualTo((true, 1, "Stage"));
    }

    // ── Refuse ───────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Refuse_RowYieldsItsMessageAtTheLastMatchedToken() {
        var grammar = Table().Define("r")
            .Pattern("actor").Value(TestKind.Identifier, "name").Kind(TestKind.Colon)
            .Word(TestKind.Identifier, "actor", t => IsWord(t, "actor")).Refuse("'{got}' is not supported (used by '{name}')").Commit()
            .Build();

        var outcome = Match(grammar, "r", "Who : actor");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That((outcome.Failure!.Committed, outcome.Failure.Refused, outcome.Failure.Offset)).IsEqualTo((true, true, 2));
        await Assert.That(outcome.Failure.Token).IsEqualTo(new TestToken(TestKind.Identifier, "actor"));
        await Assert.That(outcome.Failure.Expand("", t => t.Text)).IsEqualTo("'actor' is not supported (used by 'Who')");
    }

    [Test]
    public async Task Refuse_PolicyExternal_BeatsPolicySibling() {
        // G-05's shape: a plain three-token row and a refusal row one word longer.
        var grammar = Table().Define("entity-body")
            .Pattern("policy").Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Word(TestKind.Identifier, "policy", t => IsWord(t, "policy")).Commit()
            .Pattern("policy-external").Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Word(TestKind.Identifier, "policy", t => IsWord(t, "policy"))
                .Word(TestKind.Identifier, "external", t => IsWord(t, "external")).Refuse("'policy external' is not supported").Commit()
            .Build();

        var refused = Match(grammar, "entity-body", "X : policy external");
        var plain = Match(grammar, "entity-body", "X : policy");

        await Assert.That(refused.Failure).IsNotNull();
        await Assert.That((refused.Failure!.Refused, refused.Failure.Template)).IsEqualTo((true, "'policy external' is not supported"));
        await Assert.That(plain.Result?.PatternName).IsEqualTo("policy");
    }

    [Test]
    public async Task Refuse_Print_Throws_And_Cut_PrintsNothing() {
        var grammar = Table()
            .Define("cut").Pattern("p").Kind(TestKind.Entity).Cut().Kind(TestKind.Colon).Commit()
            .Define("plain").Pattern("p").Kind(TestKind.Entity).Kind(TestKind.Colon).Commit()
            .Define("refuse").Pattern("p").Kind(TestKind.Entity).Refuse("no").Commit()
            .Build();
        var printer = new Printer<TestToken, TestKind>(grammar, Canonical);

        await Assert.That(printer.Print("cut", "p")).IsEqualTo(printer.Print("plain", "p"));
        await Assert.That(() => printer.Print("refuse", "p")).Throws<InvalidOperationException>();
    }

    // ── labels and templates ─────────────────────────────────────────────────────────

    [Test]
    public async Task Expect_LabelsAKindAValueAndAPredicate() {
        var grammar = Table()
            .Define("kind").Pattern("p").Kind(TestKind.Entity).Kind(TestKind.Colon).Expect("':'").Commit()
            .Define("value").Pattern("p").Kind(TestKind.Entity).Value(TestKind.Identifier, "name").Expect("entity name").Commit()
            .Define("predicate").Pattern("p").Kind(TestKind.Entity).Predicate(t => t.Kind == TestKind.Number, "amount").Expect("an amount").Commit()
            .Build();

        var kind = Match(grammar, "kind", "entity x").Failure;
        var value = Match(grammar, "value", "entity 5").Failure;
        var predicate = Match(grammar, "predicate", "entity x").Failure;

        await Assert.That(kind).IsNotNull();
        await Assert.That((Expected(kind!), kind!.Expected[0].IsContent)).IsEqualTo(("':'", false));
        await Assert.That((Expected(value!), value!.Expected[0].IsContent)).IsEqualTo(("entity name", true));
        await Assert.That((Expected(predicate!), predicate!.Expected[0].IsContent)).IsEqualTo(("an amount", true));
    }

    [Test]
    public async Task Expect_WordIsItsQuotedText_RunIsItsName() {
        var grammar = Table()
            .Define("word").Pattern("p").Kind(TestKind.Entity).Word(TestKind.Identifier, "via", t => IsWord(t, "via")).Commit()
            .Define("run").Pattern("p").Kind(TestKind.Entity).Run((_, _, _) => false, "pin").Commit()
            .Build();

        var word = Match(grammar, "word", "entity x").Failure;
        var run = Match(grammar, "run", "entity x").Failure;

        await Assert.That(word).IsNotNull();
        await Assert.That((Expected(word!), word!.Expected[0].IsContent)).IsEqualTo(("'via'", false));
        await Assert.That((Expected(run!), run!.Expected[0].IsContent)).IsEqualTo(("pin", true));
    }

    [Test]
    public async Task Expect_OnACompositeOrZeroWidthElement_Throws() {
        await Assert.That(() => Table().Define("r").Pattern("p").Ref("x").Expect("y")).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Optional(TestKind.Colon).Expect("y")).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Repeat("x").Expect("y")).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Cut().Expect("y")).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Expect("y")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Template_ComesFromTheFirstExpectedElementInDeclarationOrder_AndExpandsItsPlaceholders() {
        var grammar = Table().Define("r")
            .Pattern("entity").Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Cut()
                .Kind(TestKind.Entity).Expect("'entity'", "Expected {expected} after '{name}:', got '{got}'").Commit()
            .Pattern("stage").Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Cut().Kind(TestKind.Stage).Expect("'stage'")
                .Optional(TestKind.Comma).Commit()
            .Build();

        // `stage` is evaluated first (more elements); the template still comes from `entity`, declared first.
        await Assert.That(grammar.GetPatterns("r")[0].Name).IsEqualTo("stage");

        var outcome = Match(grammar, "r", "X : 5");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(Expected(outcome.Failure!)).IsEqualTo("'entity' | 'stage'");
        await Assert.That(outcome.Failure.Expand("'entity' or 'stage'", t => t.Text)).IsEqualTo("Expected 'entity' or 'stage' after 'X:', got '5'");
    }

    [Test]
    public async Task Template_APlaceholderThatNamesNoEarlierRequiredCapture_IsRejected() {
        // A name the row does not capture is an enclosing row's; with no row reaching `r` there is none (rejected at Build).
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Kind(TestKind.Colon).Expect("x", "{nope}").Commit().Build())
            .Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Optional(new Value<TestToken, TestKind>(TestKind.Identifier, "name"))
            .Kind(TestKind.Colon).Expect("x", "{name}").Commit()).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Refuse("'{later}'").Commit().Build())
            .Throws<InvalidOperationException>();
    }

    // ── placement ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Builder_RejectsACutFirstOrLast_TwoCuts_AndAnOptionalCut() {
        await Assert.That(() => Table().Define("r").Pattern("p").Cut().Kind(TestKind.Entity).Commit()).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Optional(TestKind.Colon).Cut().Kind(TestKind.Entity).Commit()).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Cut().Commit()).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Cut().Kind(TestKind.Colon).Cut().Kind(TestKind.Stage).Commit())
            .Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Optional(new Cut<TestToken, TestKind>()).Kind(TestKind.Colon).Commit())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Builder_RejectsARefuseThatIsNotLast_OrAfterOnlyOptionalElements_OrOptional() {
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Refuse("no").Kind(TestKind.Colon).Commit()).Throws<InvalidOperationException>();
        // A Refuse after only optional elements has no position to stand on (a Refuse-only row is the catch-all: see below).
        await Assert.That(() => Table().Define("r").Pattern("p").Optional(TestKind.Colon).Refuse("no").Commit()).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Optional(new Refuse<TestToken, TestKind>("no")).Commit())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Builder_RejectsASlotOnlyRow() {
        await Assert.That(() => Table().Define("r").Pattern("p").Slot("rest").Commit()).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Optional(TestKind.Colon).Slot("rest").Commit()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Order_IsKeptByAttachPayload_AndContinuedByExtend() {
        var builder = Table().Define("r")
            .Pattern("first").Kind(TestKind.Identifier).Commit()
            .Pattern("a").Kind(TestKind.Entity).Kind(TestKind.Stage).Expect("'stage'").Commit();
        var baseGrammar = builder.Build();
        var orderOfA = baseGrammar.GetPattern("r", "a").Order;
        var extended = baseGrammar.Extend(b => b
            .AttachPayload("r", "a", "payload")
            .Define("r").Pattern("b").Kind(TestKind.Entity).Kind(TestKind.Action).Expect("'action'").Kind(TestKind.Colon).Commit());

        var outcome = Match(extended, "r", "entity 5");

        await Assert.That(orderOfA).IsEqualTo(1);
        await Assert.That(extended.GetPattern("r", "a").Order).IsEqualTo(orderOfA);
        await Assert.That(extended.GetPattern("r", "b").Order).IsEqualTo(2);
        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(Expected(outcome.Failure!)).IsEqualTo("'stage' | 'action'");
    }

    // ── TryMatch on a committing rule (fail closed) ──────────────────────────────────

    [Test]
    public async Task TryMatch_OnARuleThatHoldsACut_ThrowsEvenWhenTheCutIsNotReached() {
        var grammar = Table()
            .Define("r")
                .Pattern("committed").Kind(TestKind.Entity).Cut().Kind(TestKind.Stage).Commit()
                .Pattern("other").Kind(TestKind.Identifier).Commit()
            .Define("outer").Pattern("p").Kind(TestKind.Colon).Ref("r").Commit()
            .Define("refusing").Pattern("p").Kind(TestKind.Colon).Refuse("no").Commit()
            .Define("free").Pattern("p").Kind(TestKind.Identifier).Commit()
            .Build();

        // `x` fails the committed row before its Cut and matches `other`.
        await Assert.That(Match(grammar, "r", "x").Result?.PatternName).IsEqualTo("other");
        await Assert.That(() => TryMatch(grammar, "r", "x")).Throws<InvalidOperationException>();
        await Assert.That(() => TryMatch(grammar, "outer", "x")).Throws<InvalidOperationException>();
        await Assert.That(() => TryMatch(grammar, "refusing", "x")).Throws<InvalidOperationException>();
        await Assert.That(TryMatch(grammar, "free", "x")?.PatternName).IsEqualTo("p");
    }

    // ── follow-ups for the family rows (#856 row G-28 'also' lines, 2026-11-13) ──────────

    [Test]
    public async Task Refuse_InsideARepeatItem_EndsTheWholeMatch() {
        var grammar = Table()
            .Define("item")
                .Pattern("ok").Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Kind(TestKind.Number).Commit()
                .Pattern("actor").Value(TestKind.Identifier, "name").Kind(TestKind.Colon)
                    .Word(TestKind.Identifier, "actor", t => IsWord(t, "actor")).Refuse("'actor' is not supported").Commit()
            .Define("list").Pattern("p").Kind(TestKind.LBrace).Repeat("item").Kind(TestKind.RBrace).Commit()
            .Build();

        var outcome = Match(grammar, "list", "{ a : 1 b : actor }");

        await Assert.That(outcome.Result).IsNull();
        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That((outcome.Failure!.Refused, outcome.Failure.Offset, outcome.Failure.Template)).IsEqualTo((true, 6, "'actor' is not supported"));
    }

    [Test]
    public async Task Failure_NamesTheCommittedPattern_TheCutRowOrTheRefusingRow_AndNullWhenPlain() {
        var grammar = Table()
            .Define("inner").Pattern("typed").Kind(TestKind.Entity).Cut().Ref("type").Commit()
            .Define("type").Pattern("p").Kind(TestKind.Identifier).Kind(TestKind.Colon).Expect("':'").Commit()
            .Define("plain").Pattern("p").Kind(TestKind.Entity).Kind(TestKind.Stage).Commit()
            .Define("refusing").Pattern("no").Kind(TestKind.Entity).Refuse("no").Commit()
            .Build();

        var committed = Match(grammar, "inner", "entity a 5").Failure;
        var plain = Match(grammar, "plain", "entity 5").Failure;
        var refused = Match(grammar, "refusing", "entity").Failure;

        await Assert.That(committed).IsNotNull();
        // The failure is inside `type`; the row whose Cut passed is inner/typed.
        await Assert.That((committed!.Pattern, committed.CommittedPattern)).IsEqualTo(("p", "typed"));
        await Assert.That(plain!.CommittedPattern).IsNull();
        await Assert.That(refused!.CommittedPattern).IsEqualTo("no");
    }

    [Test]
    public async Task Refuse_ACatchAllRowOfNoTokens_ReportsAtTheHeadToken_AndAnySiblingThatMatchesRescuesIt() {
        var grammar = Table().Define("member")
            .Pattern("property").Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Cut().Value(TestKind.Identifier, "type").Expect("type name").Commit()
            .Pattern("stage").Value(TestKind.Identifier, "name").Kind(TestKind.Stage).Commit()
            .Pattern("catch-all").Refuse("Expected property, stage, action, or policy, got '{got}'").Commit()
            .Build();

        var noRow = Match(grammar, "member", "5 : x");
        var deeper = Match(grammar, "member", "a : 5");
        var matched = Match(grammar, "member", "a stage");

        await Assert.That(noRow.Failure).IsNotNull();
        await Assert.That((noRow.Failure!.Refused, noRow.Failure.Offset, noRow.Failure.CommittedPattern)).IsEqualTo((true, 0, "catch-all"));
        await Assert.That(noRow.Failure.Expand("", t => t.Text)).IsEqualTo("Expected property, stage, action, or policy, got '5'");
        // A committed row that got further outranks the catch-all, which matched nothing.
        await Assert.That((deeper.Failure!.Refused, deeper.Failure.Offset, Expected(deeper.Failure))).IsEqualTo((false, 2, "type name"));
        await Assert.That(matched.Result?.PatternName).IsEqualTo("stage");
    }

    [Test]
    public async Task Refuse_AtANamedCaptureOrWord_StandsOnThatToken_NotTheLastOne() {
        var grammar = Table()
            .Define("keyword").Pattern("p").Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Value(TestKind.Identifier, "keyword")
                .Kind(TestKind.Comma).Refuse("'{keyword}' is not supported (used as type for '{name}')", at: "keyword").Commit()
            .Define("via").Pattern("p").Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Word(TestKind.Identifier, "via", t => IsWord(t, "via"))
                .Kind(TestKind.Comma).Refuse("'via' requires the inverse navigation name", at: "via").Commit()
            .Build();

        var keyword = Match(grammar, "keyword", "Who : actor ,").Failure;
        var via = Match(grammar, "via", "Kids : via ,").Failure;

        await Assert.That(keyword).IsNotNull();
        await Assert.That((keyword!.Offset, keyword.Token.Text)).IsEqualTo((2, "actor"));
        await Assert.That(keyword.Expand("", t => t.Text)).IsEqualTo("'actor' is not supported (used as type for 'Who')");
        await Assert.That((via!.Offset, via.Token.Text)).IsEqualTo((2, "via"));
    }

    [Test]
    public async Task Refuse_AtAPositionTheRowDoesNotHaveOrThatIsOptional_IsRejected() {
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity).Refuse("no", at: "nothing").Commit()).Throws<InvalidOperationException>();
        await Assert.That(() => Table().Define("r").Pattern("p").Kind(TestKind.Entity)
            .Optional(new Value<TestToken, TestKind>(TestKind.Identifier, "name")).Refuse("no", at: "name").Commit()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Template_NamesACaptureOfAnEnclosingRow_InnermostFirst_AndTokenIsTheFailingToken() {
        var grammar = Table()
            .Define("param").Pattern("p").Value(TestKind.Identifier, "param").Kind(TestKind.Colon).Cut()
                .Value(TestKind.Identifier, "type").Expect("parameter type", "Expected a type for '{param}' in function '{name}', got '{token}'").Commit()
            .Define("function").Pattern("p").Kind(TestKind.Action).Value(TestKind.Identifier, "name").Kind(TestKind.LBrace).Cut()
                .Repeat("param").Kind(TestKind.RBrace).Commit()
            .Build();

        var outcome = Match(grammar, "function", "action Go { a : 5 }");

        await Assert.That(outcome.Failure).IsNotNull();
        await Assert.That(outcome.Failure!.Expand("parameter type", t => t.Text)).IsEqualTo("Expected a type for 'a' in function 'Go', got '5'");
    }

    [Test]
    public async Task Template_ACaptureNoRowProvides_IsRejectedAtBuild() {
        // `{name}` is not param's own capture, and the one row that references `param` does not capture it before the reference.
        await Assert.That(() => Table()
            .Define("param").Pattern("p").Value(TestKind.Identifier, "param").Kind(TestKind.Colon).Expect("':'", "in '{name}'").Commit()
            .Define("function").Pattern("p").Kind(TestKind.Action).Kind(TestKind.LBrace).Repeat("param").Value(TestKind.Identifier, "name").Commit()
            .Build()).Throws<InvalidOperationException>();
    }
}