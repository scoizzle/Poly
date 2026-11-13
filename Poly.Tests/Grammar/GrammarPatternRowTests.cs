using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Grammar;

/// <summary>
/// Self-tests of the change-protocol machinery (G-23): the pure <see cref="PatternRowChecker"/> fed
/// synthetic observations against grammar copies, and the <see cref="PrintTrace"/> observer on the real
/// <see cref="Printer{TToken,TTokenKind}"/>. A mutated copy is built from <c>Grammar.Rules</c> and the
/// internal <c>Grammar(Dictionary)</c> constructor; the shared tables are never mutated. Each mutation test
/// carries its control: the same observation against the real table passes that rule.
/// </summary>
public sealed class GrammarPatternRowTests {
    private static readonly Grammar<DslToken, DslTokenKind> Dsl = DslGrammar.Core;
    private static readonly Grammar<CSharpToken, CSharpTokenKind> CSharp = CSharpGrammar.Table;
    private const string DslKey = PatternRowAllowList.Dsl;
    private const string CSharpKey = PatternRowAllowList.CSharp;
    private const string Permanent = "none: permanent — a self-test entry";

    // ── mutated copies and synthetic observations ─────────────────────────────

    /// <summary>A copy of <paramref name="grammar"/> with one row replaced. The row must exist.</summary>
    private static Grammar<TToken, TTokenKind> Replace<TToken, TTokenKind>(
        Grammar<TToken, TTokenKind> grammar,
        string rule,
        string pattern,
        Func<Pattern<TToken, TTokenKind>, Pattern<TToken, TTokenKind>> replace)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct {
        var found = false;
        var rules = new Dictionary<string, List<Pattern<TToken, TTokenKind>>>(StringComparer.Ordinal);
        foreach (var (name, patterns) in grammar.Rules) {
            rules[name] = [.. patterns.Select(row => {
                if (!string.Equals(name, rule, StringComparison.Ordinal) || !string.Equals(row.Name, pattern, StringComparison.Ordinal))
                    return row;
                found = true;
                return replace(row);
            })];
        }
        if (!found)
            throw new InvalidOperationException($"No row {rule}/{pattern} to replace.");
        return new Grammar<TToken, TTokenKind>(rules);
    }

    private static Pattern<TToken, TTokenKind> WithElements<TToken, TTokenKind>(
        Pattern<TToken, TTokenKind> row,
        Func<List<IPatternElement<TToken, TTokenKind>>, IEnumerable<IPatternElement<TToken, TTokenKind>>> edit,
        string? name = null)
        where TToken : struct, IToken<TTokenKind>
        where TTokenKind : struct =>
        new(name ?? row.Name, [.. edit([.. row.Elements])], row.Priority, row.Payload);

    private static PrintObservation Observed(
        object grammar, string rule, string pattern, string text,
        string[]? fills = null, bool[]? emitted = null, int? supplied = null) =>
        new(grammar, rule, pattern, fills ?? [], emitted ?? [], supplied, text);

    private static IReadOnlyList<PatternRowViolation> CheckDsl(
        Grammar<DslToken, DslTokenKind> grammar,
        IReadOnlyList<PrintObservation> observations,
        IReadOnlyList<PatternRowEntry>? entries = null,
        IReadOnlyList<PatternRowExample>? examples = null) =>
        PatternRowChecker.Check(PatternRowCorpus.DslSurface(grammar), observations, entries ?? [], examples ?? []);

    private static IReadOnlyList<PatternRowViolation> CheckCSharp(
        Grammar<CSharpToken, CSharpTokenKind> grammar,
        IReadOnlyList<PrintObservation> observations,
        IReadOnlyList<PatternRowEntry>? entries = null) =>
        PatternRowChecker.Check(PatternRowCorpus.CSharpSurface(grammar), observations, entries ?? [], []);

    private static List<PatternRowViolation> On(
        IEnumerable<PatternRowViolation> violations, PatternRowCheck check, string rule, string? pattern) =>
        [.. violations.Where(v => v.Check == check && v.Rule == rule && v.Pattern == pattern)];

    private static PrintObservation ClassWithGenericsAndBase(object grammar) =>
        Observed(grammar, "type-decl", "class", "class Box<T> : Base\n{\n}",
            fills: ["name"], emitted: [false, false, true, true, true], supplied: 5);

    private static PrintObservation EmptyReturn(object grammar) =>
        Observed(grammar, "statement", "return", "return;", emitted: [false], supplied: 1);

    // ── classification and unknown elements ───────────────────────────────────

    private sealed record AlienElement : IPatternElement<DslToken, DslTokenKind>;

    [Test]
    public async Task Classify_UnknownElementType_Throws() {
        var error = Assert.Throws<InvalidOperationException>(() =>
            PatternRowChecker.Classify<DslToken, DslTokenKind>(new AlienElement()));
        await Assert.That(error.Message).Contains("AlienElement");
    }

    [Test]
    public async Task Check_RowWithAnUnknownElementType_Throws() {
        var rules = new Dictionary<string, List<Pattern<DslToken, DslTokenKind>>>(StringComparer.Ordinal) {
            ["alien"] = [new Pattern<DslToken, DslTokenKind>("row", [new AlienElement()])],
        };
        var grammar = new Grammar<DslToken, DslTokenKind>(rules);

        var error = Assert.Throws<InvalidOperationException>(() =>
            CheckDsl(grammar, [Observed(grammar, "alien", "row", "x")]));
        await Assert.That(error.Message).Contains("AlienElement");
    }

    // ── G-27 elements: Word is fixed text ─────────────────────────────────────

    [Test]
    public async Task Classify_Word_IsFixed() {
        var word = new Word<DslToken, DslTokenKind>(DslTokenKind.Identifier, "via", _ => true);

        await Assert.That(PatternRowChecker.Classify(word)).IsEqualTo(PatternRowChecker.ElementRole.Fixed);
    }

    [Test]
    public async Task Check_RowWithAWord_ReadsBackAndNeedsNoFillForTheWord() {
        var grammar = Dsl.ToBuilder()
            .Define("synthetic-nav")
                .Pattern("via")
                    .Value(DslTokenKind.Identifier, "name").Word("via").Value(DslTokenKind.Identifier, "target").Commit()
            .Build();

        var good = CheckDsl(grammar, [Observed(grammar, "synthetic-nav", "via", "Owner via Account", fills: ["name", "target"])]);
        var bad = CheckDsl(grammar, [Observed(grammar, "synthetic-nav", "via", "Owner vial Account", fills: ["name", "target"])]);

        await Assert.That(good.Where(v => v.Rule == "synthetic-nav")).IsEmpty();
        await Assert.That(On(bad, PatternRowCheck.ReadBack, "synthetic-nav", "via")).Count().IsEqualTo(1);
    }

    // ── G-27 elements: Slot is a named hole that ends the row ─────────────────

    [Test]
    public async Task Classify_Slot_IsAHole() {
        await Assert.That(PatternRowChecker.Classify(new Slot<DslToken, DslTokenKind>("expr")))
            .IsEqualTo(PatternRowChecker.ElementRole.Hole);
    }

    [Test]
    public async Task Check_RowEndingInASlot_NeedsItsFill_AndReadsBackItsHead() {
        var grammar = Dsl.ToBuilder()
            .Define("synthetic-effect")
                .Pattern("assign")
                    .Kind(DslTokenKind.Assign).Value(DslTokenKind.Identifier, "property").Kind(DslTokenKind.To)
                    .Slot("expr").Commit()
            .Build();

        var good = CheckDsl(grammar, [Observed(grammar, "synthetic-effect", "assign", "assign Total to Price * 2", fills: ["expr", "property"])]);
        var noFill = CheckDsl(grammar, [Observed(grammar, "synthetic-effect", "assign", "assign Total to Price", fills: ["property"])]);
        var badHead = CheckDsl(grammar, [Observed(grammar, "synthetic-effect", "assign", "assign Total Price", fills: ["expr", "property"])]);

        await Assert.That(good.Where(v => v.Rule == "synthetic-effect")).IsEmpty();
        await Assert.That(On(noFill, PatternRowCheck.Holes, "synthetic-effect", "assign")).Count().IsEqualTo(1);
        await Assert.That(On(badHead, PatternRowCheck.ReadBack, "synthetic-effect", "assign")).Count().IsEqualTo(1);
    }

    // ── coverage (A3a) ────────────────────────────────────────────────────────

    [Test]
    public async Task Check_ZeroObservations_ReportsTheSurfaceAndEveryRowUnobserved() {
        var violations = CheckDsl(Dsl, []);

        var rowCount = Dsl.Rules.Sum(rule => rule.Value.Count);
        await Assert.That(violations.Count(v => v.Check == PatternRowCheck.Unobserved)).IsEqualTo(rowCount + 1);
        await Assert.That(On(violations, PatternRowCheck.Unobserved, "*", null)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_SyntheticRowWithNoFill_IsUnobserved() {
        var grammar = Dsl.ToBuilder()
            .Define("synthetic-row").Pattern("no-fill").Kind(DslTokenKind.Domain).Commit()
            .Build();

        var violations = CheckDsl(grammar, []);

        await Assert.That(On(violations, PatternRowCheck.Unobserved, "synthetic-row", "no-fill")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_ObservationOfARowTheTableDoesNotHave_IsAnUnknownRow() {
        var violations = CheckDsl(Dsl, [
            Observed(Dsl, "uses", "nope", "uses x"),
            new PrintObservation(Dsl, null, "orphan", [], [], null, "x"),
        ]);

        await Assert.That(On(violations, PatternRowCheck.UnknownRow, "uses", "nope")).Count().IsEqualTo(1);
        await Assert.That(violations.Count(v => v.Check == PatternRowCheck.UnknownRow && v.Pattern == "orphan")).IsEqualTo(1);
    }

    // ── holes and slots (A1) ──────────────────────────────────────────────────

    [Test]
    public async Task Check_RenamedHole_ReportsTheSurplusFillAndTheMissingHole() {
        var renamed = Replace(Dsl, "uses", "id", row => WithElements(row, elements =>
            [elements[0], new Value<DslToken, DslTokenKind>(DslTokenKind.Identifier, "ident")]));
        static PrintObservation Uses(object grammar) => Observed(grammar, "uses", "id", "uses x", fills: ["id"]);

        var control = On(CheckDsl(Dsl, [Uses(Dsl)]), PatternRowCheck.Holes, "uses", "id");
        var mutated = On(CheckDsl(renamed, [Uses(renamed)]), PatternRowCheck.Holes, "uses", "id");

        await Assert.That(control).IsEmpty();
        await Assert.That(mutated.Any(v => v.Message.Contains("'id'"))).IsTrue();
        await Assert.That(mutated.Any(v => v.Message.Contains("'ident'"))).IsTrue();
    }

    [Test]
    public async Task Check_ExtraOptionalSlot_FailsTheSlotCount() {
        var widened = Replace(CSharp, "statement", "while", row => WithElements(row, elements =>
            [.. elements, new Optional<CSharpToken, CSharpTokenKind>(new Ref<CSharpToken, CSharpTokenKind>("else"))]));
        static PrintObservation While(object grammar, int slots) =>
            Observed(grammar, "statement", "while", "while (true)\n{\n}", emitted: [.. Enumerable.Repeat(true, slots)], supplied: 2);

        var control = On(CheckCSharp(CSharp, [While(CSharp, 2)]), PatternRowCheck.Arity, "statement", "while");
        var mutated = On(CheckCSharp(widened, [While(widened, 3)]), PatternRowCheck.Arity, "statement", "while");

        await Assert.That(control).IsEmpty();
        await Assert.That(mutated).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_SpareContentValue_FailsTheSlotCount() {
        static PrintObservation Bare(int supplied) =>
            Observed(CSharp, "attribute", "bare", "[Obsolete]", fills: ["close", "open"], emitted: [true], supplied: supplied);

        await Assert.That(On(CheckCSharp(CSharp, [Bare(1)]), PatternRowCheck.Arity, "attribute", "bare")).IsEmpty();
        await Assert.That(On(CheckCSharp(CSharp, [Bare(2)]), PatternRowCheck.Arity, "attribute", "bare")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_UnreportedSuppliedCount_FailsTheSlotCountOnASurfaceThatReportsIt() {
        var observation = Observed(CSharp, "attribute", "bare", "[Obsolete]", fills: ["close", "open"], emitted: [true]);

        await Assert.That(On(CheckCSharp(CSharp, [observation]), PatternRowCheck.Arity, "attribute", "bare")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_ObserverSlotCountDisagreeingWithThePrinter_IsReported() {
        var observation = Observed(Dsl, "uses", "id", "uses x", fills: ["id"], emitted: [true]);

        await Assert.That(On(CheckDsl(Dsl, [observation]), PatternRowCheck.Arity, "uses", "id")).Count().IsEqualTo(1);
    }

    // ── read-back (A2) ────────────────────────────────────────────────────────

    [Test]
    public async Task Check_SwappedGenericsAndLineage_WithBothSlotsFilled_FailsReadBack() {
        var swapped = Replace(CSharp, "type-decl", "class", row => WithElements(row, elements =>
            [elements[0], elements[1], elements[2], elements[3], elements[5], elements[4], elements[6]]));

        var control = On(CheckCSharp(CSharp, [ClassWithGenericsAndBase(CSharp)]), PatternRowCheck.ReadBack, "type-decl", "class");
        var mutated = On(CheckCSharp(swapped, [ClassWithGenericsAndBase(swapped)]), PatternRowCheck.ReadBack, "type-decl", "class");

        await Assert.That(control).IsEmpty();
        await Assert.That(mutated).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_RemovedOperatorKind_FailsReadBack() {
        var narrowed = Replace(CSharp, "expr-additive", "additive", row => WithElements(row, _ =>
            [new LeftAssoc<CSharpToken, CSharpTokenKind>("expr-multiplicative", [CSharpTokenKind.Minus])]));
        static PrintObservation Sum(object grammar) =>
            Observed(grammar, "expr-additive", "additive", "a + b", emitted: [true], supplied: 1);

        var control = On(CheckCSharp(CSharp, [Sum(CSharp)]), PatternRowCheck.ReadBack, "expr-additive", "additive");
        var mutated = On(CheckCSharp(narrowed, [Sum(narrowed)]), PatternRowCheck.ReadBack, "expr-additive", "additive");

        await Assert.That(control).IsEmpty();
        await Assert.That(mutated).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_TextAnotherRowReads_FailsReadBack() {
        var observation = Observed(Dsl, "uses", "id", "uses acme 1.2.3", fills: ["id"]);

        await Assert.That(On(CheckDsl(Dsl, [observation]), PatternRowCheck.ReadBack, "uses", "id")).Count().IsEqualTo(1);
    }

    // ── per-slot coverage (A3b) ───────────────────────────────────────────────

    [Test]
    public async Task Check_AlwaysNullSlot_FailsWithoutItsEntry_AndPassesWithIt() {
        var observations = new[] { EmptyReturn(CSharp) };
        var keyed = PatternRowEntry.AlwaysNull(CSharpKey, "statement", "return", 0, "self-test", Permanent);

        var without = CheckCSharp(CSharp, observations);
        var with = CheckCSharp(CSharp, observations, [keyed]);

        await Assert.That(On(without, PatternRowCheck.SlotNeverFilled, "statement", "return")).Count().IsEqualTo(1);
        await Assert.That(On(with, PatternRowCheck.SlotNeverFilled, "statement", "return")).IsEmpty();
        await Assert.That(On(with, PatternRowCheck.Stale, "statement", "return")).IsEmpty();
    }

    [Test]
    public async Task Check_AlwaysNullEntryForTheWrongSlot_DoesNotCoverTheSlot() {
        var wrongSlot = PatternRowEntry.AlwaysNull(CSharpKey, "statement", "return", 1, "self-test", Permanent);

        var violations = CheckCSharp(CSharp, [EmptyReturn(CSharp)], [wrongSlot]);

        await Assert.That(On(violations, PatternRowCheck.SlotNeverFilled, "statement", "return")).Count().IsEqualTo(1);
        await Assert.That(violations.Any(v => v.Rule == "statement" && v.Pattern == "return"
            && v.Check is PatternRowCheck.Stale or PatternRowCheck.Schema)).IsTrue();
    }

    // ── staleness, both ways ──────────────────────────────────────────────────

    [Test]
    public async Task Check_UnobservedEntryForAnObservedRow_IsStale() {
        var entry = PatternRowEntry.Row(CSharpKey, PatternRowEntryKind.ReaderOnly, "statement", "return", "self-test", Permanent);

        var violations = CheckCSharp(CSharp, [EmptyReturn(CSharp)], [entry]);

        await Assert.That(On(violations, PatternRowCheck.Stale, "statement", "return")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_AlwaysNullEntryForAFilledSlot_IsStale() {
        var entry = PatternRowEntry.AlwaysNull(CSharpKey, "statement", "return", 0, "self-test", Permanent);
        var filled = Observed(CSharp, "statement", "return", "return 1;", emitted: [true], supplied: 1);

        var violations = CheckCSharp(CSharp, [filled], [entry]);

        await Assert.That(On(violations, PatternRowCheck.Stale, "statement", "return")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_DefectEntryWhoseWitnessReadsBack_IsStale() {
        var entry = PatternRowEntry.Defect(CSharpKey, "type-decl", "interface", "interface ", "self-test", "G-23b (#848)");
        var braced = Observed(CSharp, "type-decl", "interface", "interface I\n{\n}",
            fills: ["name"], emitted: [false, false, false, false, true], supplied: 5);

        var violations = CheckCSharp(CSharp, [braced], [entry]);

        await Assert.That(On(violations, PatternRowCheck.Stale, "type-decl", "interface")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_AsymmetryEntryWhoseRowReadsBack_IsStale() {
        var entry = PatternRowEntry.Row(DslKey, PatternRowEntryKind.PrefixOnly, "uses", "pinned", "self-test", Permanent);
        var simplePin = Observed(Dsl, "uses", "pinned", "uses acme v1", fills: ["id", "version"]);

        var violations = CheckDsl(Dsl, [simplePin], [entry]);

        await Assert.That(On(violations, PatternRowCheck.Stale, "uses", "pinned")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_DefectEntry_ExemptsOnlyObservationsCarryingItsWitness() {
        var entry = PatternRowEntry.Defect(CSharpKey, "type-decl", "interface", "interface IShape", "self-test", "G-23b (#848)");
        static PrintObservation Unbraced(string name) => Observed(CSharp, "type-decl", "interface",
            $"interface {name}\n    int Sides {{ get; }}\n", fills: ["name"], emitted: [false, false, false, false, true], supplied: 5);

        var violations = CheckCSharp(CSharp, [Unbraced("IShape"), Unbraced("IOther")], [entry]);

        var readBack = On(violations, PatternRowCheck.ReadBack, "type-decl", "interface");
        await Assert.That(readBack).Count().IsEqualTo(1);
        await Assert.That(readBack[0].Message).Contains("IOther");
        await Assert.That(On(violations, PatternRowCheck.Stale, "type-decl", "interface")).IsEmpty();
    }

    [Test]
    public async Task Check_DefectEntry_DoesNotExemptASlotCountFailure() {
        var entry = PatternRowEntry.Defect(CSharpKey, "attribute", "bare", "[Obsolete]", "self-test", "G-23b (#848)");
        var spare = Observed(CSharp, "attribute", "bare", "[Obsolete]", fills: ["close", "open"], emitted: [true], supplied: 2);

        var violations = CheckCSharp(CSharp, [spare], [entry]);

        await Assert.That(On(violations, PatternRowCheck.Arity, "attribute", "bare")).Count().IsEqualTo(1);
        await Assert.That(On(violations, PatternRowCheck.Stale, "attribute", "bare")).Count().IsEqualTo(1);
    }

    // ── allow-list schema ─────────────────────────────────────────────────────

    [Test]
    public async Task Check_MalformedEntries_FailTheSchema() {
        PatternRowEntry[] entries = [
            PatternRowEntry.Row(CSharpKey, PatternRowEntryKind.ReaderOnly, "name", "simple", "self-test", "someone"),
            PatternRowEntry.Row(CSharpKey, PatternRowEntryKind.ReaderOnly, "name", "qualified", "", Permanent),
            PatternRowEntry.WholeRule(CSharpKey, PatternRowEntryKind.ReaderOnly, "type-list", 2, "self-test", Permanent),
            PatternRowEntry.Row(CSharpKey, PatternRowEntryKind.ReaderOnly, "type", "missing-row", "self-test", Permanent),
            PatternRowEntry.Defect(CSharpKey, "type-decl", "enum", "enum Empty", "self-test", Permanent),
            PatternRowEntry.Row(CSharpKey, PatternRowEntryKind.Defect, "type-decl", "record", "self-test", "G-23b (#848)"),
            PatternRowEntry.Row(CSharpKey, PatternRowEntryKind.ReaderOnly, "type-tail", "tail", "self-test", Permanent),
            PatternRowEntry.Row(CSharpKey, PatternRowEntryKind.ReaderOnly, "type-tail", "tail", "self-test", Permanent),
        ];

        var schema = CheckCSharp(CSharp, [], entries).Where(v => v.Check == PatternRowCheck.Schema).ToList();

        await Assert.That(schema.Any(v => v.Rule == "name" && v.Pattern == "simple")).IsTrue();
        await Assert.That(schema.Any(v => v.Rule == "name" && v.Pattern == "qualified")).IsTrue();
        await Assert.That(schema.Any(v => v.Rule == "type-list")).IsTrue();
        await Assert.That(schema.Any(v => v.Rule == "type" && v.Pattern == "missing-row")).IsTrue();
        await Assert.That(schema.Any(v => v.Rule == "type-decl" && v.Pattern == "enum")).IsTrue();
        await Assert.That(schema.Any(v => v.Rule == "type-decl" && v.Pattern == "record")).IsTrue();
        await Assert.That(schema.Any(v => v.Rule == "type-tail" && v.Pattern == "tail")).IsTrue();
    }

    [Test]
    public async Task AllowListAndExamples_UnknownSurface_Throw() {
        var entries = Assert.Throws<ArgumentException>(() => PatternRowAllowList.For("dls"));
        var examples = Assert.Throws<ArgumentException>(() => PatternRowExamples.For("dls"));

        await Assert.That(entries.Message).Contains("'dls'");
        await Assert.That(examples.Message).Contains("'dls'");
        await Assert.That(PatternRowAllowList.For(DslKey)).IsNotEmpty();
        await Assert.That(PatternRowAllowList.For(CSharpKey)).IsNotEmpty();
    }

    [Test]
    public async Task Check_PendingOrchestratorOwner_FailsTheSchema() {
        var entry = PatternRowEntry.Row(CSharpKey, PatternRowEntryKind.ReaderOnly, "name", "simple", "self-test", "pending orchestrator");

        var schema = On(CheckCSharp(CSharp, [], [entry]), PatternRowCheck.Schema, "name", "simple");

        await Assert.That(schema).Count().IsEqualTo(1);
        await Assert.That(schema[0].Message).Contains("malformed owner");
    }

    // ── examples (A3c) ────────────────────────────────────────────────────────

    private static readonly PatternRowEntry TopEnumHead =
        PatternRowEntry.Row(DslKey, PatternRowEntryKind.ReaderHead, "top", "enum", "self-test", Permanent);

    [Test]
    public async Task Check_ReaderHeadWithoutAnExample_Fails() {
        var violations = CheckDsl(Dsl, [], [TopEnumHead], []);

        await Assert.That(On(violations, PatternRowCheck.Example, "top", "enum")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_ExampleTheMatcherReadsAsAnotherRow_Fails() {
        var control = CheckDsl(Dsl, [], [TopEnumHead], [new(DslKey, "top", "enum", "Status: enum")]);
        var stolen = CheckDsl(Dsl, [], [TopEnumHead], [new(DslKey, "top", "enum", "Order: entity")]);

        await Assert.That(On(control, PatternRowCheck.Example, "top", "enum")).IsEmpty();
        await Assert.That(On(stolen, PatternRowCheck.Example, "top", "enum")).Count().IsEqualTo(1);
    }

    private static readonly PatternRowEntry LegacyActionReaderOnly =
        PatternRowEntry.Row(DslKey, PatternRowEntryKind.ReaderOnly, "entity-body", "legacy-action", "self-test", Permanent);

    [Test]
    public async Task Check_ReaderOnlyRowWithoutAnExample_Fails() {
        var violations = CheckDsl(Dsl, [], [LegacyActionReaderOnly], []);

        await Assert.That(On(violations, PatternRowCheck.Example, "entity-body", "legacy-action")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Check_ReaderOnlyRowWithAnExampleTheMatcherSelects_Passes() {
        var violations = CheckDsl(Dsl, [], [LegacyActionReaderOnly], [new(DslKey, "entity-body", "legacy-action", "Submit(")]);

        await Assert.That(On(violations, PatternRowCheck.Example, "entity-body", "legacy-action")).IsEmpty();
    }

    [Test]
    public async Task Check_ExampleForARowThatIsNotAReaderHead_Fails() {
        var violations = CheckDsl(Dsl, [], [], [new(DslKey, "document", "header", "domain Shop")]);

        await Assert.That(On(violations, PatternRowCheck.Example, "document", "header")).Count().IsEqualTo(1);
    }

    // ── floors ────────────────────────────────────────────────────────────────

    [Test]
    public async Task CheckFloor_RenamedFloorRow_FailsLoudly() {
        var renamed = Replace(Dsl, "uses", "id", row => WithElements(row, elements => elements, name: "ident"));
        (string, string)[] floor = [("uses", "id")];

        var violations = PatternRowChecker.CheckFloor(PatternRowCorpus.DslSurface(renamed), [], floor);

        await Assert.That(violations).Count().IsEqualTo(1);
        await Assert.That(violations[0].Message).Contains("not a row of the table");
    }

    [Test]
    public async Task CheckFloor_UnobservedFloorRow_Fails_AndAnObservedOnePasses() {
        (string, string)[] floor = [("uses", "id")];
        var surface = PatternRowCorpus.DslSurface(Dsl);

        var unobserved = PatternRowChecker.CheckFloor(surface, [], floor);
        var observed = PatternRowChecker.CheckFloor(surface, [Observed(Dsl, "uses", "id", "uses x", fills: ["id"])], floor);

        await Assert.That(unobserved).Count().IsEqualTo(1);
        await Assert.That(observed).IsEmpty();
    }

    // ── refusals ──────────────────────────────────────────────────────────────

    private static readonly PatternRowRefusal Refused =
        new("unit a.cs: f();", "CSharpWriter rejected Lambda (NodeId 01a1d3): no delegate type.\nmore detail");

    private static readonly PatternRowExpectedRefusal Expected =
        new("unit a.cs: f();", "CSharpWriter rejected Lambda (NodeId …): no delegate type.", "G-23b");

    [Test]
    public async Task CheckRefusals_ListedRefusal_Passes() =>
        await Assert.That(PatternRowChecker.CheckRefusals([Refused], [Expected])).IsEmpty();

    [Test]
    public async Task CheckRefusals_UnlistedRefusal_Fails() {
        var violations = PatternRowChecker.CheckRefusals([Refused, new("unit b.cs: g();", "Other refusal.")], [Expected]);

        await Assert.That(violations).Count().IsEqualTo(1);
        await Assert.That(violations[0]).Contains("unit b.cs");
    }

    [Test]
    public async Task CheckRefusals_ListedRefusalThatNoLongerOccurs_IsStale() {
        var violations = PatternRowChecker.CheckRefusals([], [Expected]);

        await Assert.That(violations).Count().IsEqualTo(1);
        await Assert.That(violations[0]).Contains("stale");
    }

    [Test]
    public async Task CheckRefusals_SameInputWithAnotherMessage_FailsBothWays() {
        var changed = PatternRowChecker.CheckRefusals([new("unit a.cs: f();", "A different refusal.")], [Expected]);

        await Assert.That(changed).Count().IsEqualTo(2);
    }

    [Test]
    public async Task CheckRefusals_MalformedOwner_Fails() {
        var violations = PatternRowChecker.CheckRefusals([Refused], [Expected with { Owner = "pending orchestrator" }]);

        await Assert.That(violations).Count().IsEqualTo(1);
        await Assert.That(violations[0]).Contains("owner");
    }

    // ── the observer on the real printer ──────────────────────────────────────

    private static Printer<DslToken, DslTokenKind> DslPrinter() =>
        new(Dsl, DslGrammar.CanonicalText, () => new DslTokenWriter());

    private static readonly Dictionary<string, string> UsesFill = new(StringComparer.Ordinal) { ["id"] = "acme" };

    [Test]
    public async Task PrintTrace_RecordsATopLevelPrint_WithItsRuleFillsAndText() {
        var printer = DslPrinter();
        using var scope = PrintTrace.Begin();

        var text = printer.Print("uses", "id", fills: UsesFill);
        printer.Print(Dsl.GetPattern("uses", "id"), fills: UsesFill);

        var observations = scope.Observations;
        await Assert.That(observations).Count().IsEqualTo(2);
        foreach (var observation in observations) {
            await Assert.That(observation.Grammar).IsSameReferenceAs(Dsl);
            await Assert.That(observation.Rule).IsEqualTo("uses");
            await Assert.That(observation.Pattern).IsEqualTo("id");
            await Assert.That(observation.FillKeys).IsEquivalentTo(new[] { "id" });
            await Assert.That(observation.ContentEmitted).IsEmpty();
            await Assert.That(observation.SuppliedContentCount is null).IsTrue();
            await Assert.That(observation.Text).IsEqualTo(text);
        }
    }

    [Test]
    public async Task PrintTrace_RecordsWhetherEachSlotWroteAnything() {
        var printer = DslPrinter();
        var fills = new Dictionary<string, string>(StringComparer.Ordinal) { ["amount"] = "14", ["unit"] = "Days" };
        using var scope = PrintTrace.Begin();

        printer.Print("date-operation", "add", ctx => ctx.Emit("DueDate"), fills);
        printer.Print("date-operation", "add", _ => { }, fills);

        var observations = scope.Observations;
        await Assert.That(observations).Count().IsEqualTo(2);
        await Assert.That(observations[0].ContentEmitted).IsEquivalentTo(new[] { true });
        await Assert.That(observations[1].ContentEmitted).IsEquivalentTo(new[] { false });
    }

    [Test]
    public async Task PrintTrace_DoesNotRecordANestedPrintRule() {
        var printer = DslPrinter();
        var fills = new Dictionary<string, string>(StringComparer.Ordinal) { ["amount"] = "14", ["unit"] = "Days" };
        using var scope = PrintTrace.Begin();

        printer.Print("date-operation", "add", ctx => ctx.PrintRule("expr-primary", "true"), fills);

        await Assert.That(scope.Observations).Count().IsEqualTo(1);
        await Assert.That(scope.Observations[0].ContentEmitted).IsEquivalentTo(new[] { true });
    }

    [Test]
    public async Task PrintTrace_OutsideAScope_RecordsNothing() {
        var printer = DslPrinter();
        var scope = PrintTrace.Begin();
        scope.Dispose();

        printer.Print("uses", "id", fills: UsesFill);

        await Assert.That(scope.Observations).IsEmpty();
        await Assert.That(PrintTrace.IsActive).IsFalse();
    }

    [Test]
    public async Task PrintTrace_LeavesThePrintedTextUnchanged_AndTheCSharpWriterReportsWhatItSupplies() {
        const string source = "class Box<T> : Base { long Count { get; set; } long Twice(long x) { return x * 2; } }";
        var unit = CSharpLanguage.Read(source);
        var outside = CSharpLanguage.Write(unit);

        string inside;
        IReadOnlyList<PrintObservation> observations;
        using (var scope = PrintTrace.Begin()) {
            inside = CSharpLanguage.Write(unit);
            observations = scope.Observations;
        }

        await Assert.That(inside).IsEqualTo(outside);
        await Assert.That(observations).IsNotEmpty();
        await Assert.That(observations.All(o => ReferenceEquals(o.Grammar, CSharp))).IsTrue();
        await Assert.That(observations.All(o => o.SuppliedContentCount is not null)).IsTrue();
    }
}