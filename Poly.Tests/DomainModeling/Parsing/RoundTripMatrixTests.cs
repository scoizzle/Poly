using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>The class of a cell's failure; <see cref="Ok"/> when the cell passes.</summary>
internal sealed record CellOutcome(string Class, string? ExceptionType) {
    internal const string Ok = "ok";

    public bool Passed => Class == Ok;

    public override string ToString() => ExceptionType is null ? Class : Class + ":" + ExceptionType;
}

/// <summary>
/// An expected-failure entry: a cell whose result is KNOWN WRONG, the row that owns the fix, and the class of failure.
/// It records the cell, the class and the exception type only, never a message (amendment 10): the wording of an
/// error is the diagnostics baseline's (G-26b).
/// </summary>
internal sealed record ExpectedFailure(string CellId, string Owner, string Issue, string Class, string? ExceptionType);

/// <summary>The closed set of reasons a member of an enumeration has no matrix cell.</summary>
internal enum SkipReason {
    /// <summary>"not expressible in the DSL: analysis-only": the member exists for analysis passes and has no authored form.</summary>
    AnalysisOnly,

    /// <summary>"not expressible in the DSL: the printer has no form for it" (<c>ConstraintKind.Print</c> is null).</summary>
    NotPrinted,

    /// <summary>"not a distinct DSL form: the printer writes its parts one after another" (a composite effect).</summary>
    Flattened,
}

/// <summary>
/// The round-trip matrix (G-26a, amendment 10 of the G-26 lock): every cell builds a <see cref="Domain"/> in code, prints it,
/// parses the text, prints the reparsed model, and requires (1) the two prints equal and (2) the reparsed model equal to the built
/// one under <see cref="DomainGraphCanon"/>. A cell whose result is KNOWN WRONG is an explicit <see cref="ExpectedFailure"/> with its
/// owner row; the test is stale-checked both ways: an entry whose cell passes fails (delete the entry), and a failing cell with no
/// entry fails. <c>RoundTripKnownFailures</c> must be empty when G-36 lands.
///
/// <para><b>The reflection guard.</b> Every member of every enumeration the model has, every effect, constraint and annotation kind, has a
/// cell (<see cref="MatrixCell.Covers"/>) or an explicit skip from the closed <see cref="SkipReason"/> set; a skip is asserted to name a member
/// that still exists and is not covered; the number of skips is ratcheted by <see cref="MaxSkips"/> (raising it is a visible edit);
/// a new enumeration member with neither a cell nor a skip FAILS.</para>
///
/// <para><b>Self-tests.</b> <see cref="Verdict"/> and <see cref="GuardProblems"/> are pure; the <c>SelfTest_*</c> tests mutate a COPY (a reparse
/// that loses one attribute; a stale entry; an unowned failure; a member with neither cell nor skip) and assert the oracle fails.</para>
///
/// <para><b>For a later row.</b> To add a cell, add it in <see cref="RoundTripMatrixCells"/> with its <c>Covers</c>. To delete an
/// expected-failure entry, fix the defect and delete its rule in <see cref="Rules"/>; the stale check names the entries that now pass.
/// The matrix prints through the public printer and the parser only: it never moves with an internal rename.</para>
/// </summary>
public sealed class RoundTripMatrixTests {
    /// <summary>Ratchet on the skips. Raising it is a visible edit; the skips are listed in <see cref="Skips"/>.</summary>
    internal const int MaxSkips = 11;

    /// <summary>Ratchet on the expected-failure cells. It can only go down; G-36 deletes the table and asserts it empty.</summary>
    internal const int MaxExpectedFailureCells = 116;

    // ── running one cell ─────────────────────────────────────────────────────────────

    private static DomainSession SessionFor(CellSession session) => session switch {
        CellSession.Bare => DomainSession.ForExtensions([]),
        CellSession.Authoring => ExtensionCatalog.Core.Authoring,
        CellSession.Temporal => DomainSession.ForExtensions([ExtensionCatalog.TemporalId]),
        _ => throw new ArgumentOutOfRangeException(nameof(session)),
    };

    /// <summary>The model with its seeded primitive types removed: parsing seeds the ten primitives, a built domain does not.</summary>
    private static Domain WithoutPrimitives(Domain domain) =>
        domain with { Types = [.. domain.Types.Where(t => t is not PrimitiveType)] };

    /// <summary>
    /// Print, parse, print again, compare. <paramref name="mutateReparsed"/> lets a self-test lose an attribute in the reparsed model;
    /// production use passes null.
    /// </summary>
    internal static CellOutcome Run(MatrixCell cell, Func<Domain, Domain>? mutateReparsed = null) {
        var session = SessionFor(cell.Session);
        var built = cell.Build();
        string first;
        try {
            first = new DomainDslPrinter(session).Print(built);
        }
        catch (Exception ex) {
            return new CellOutcome("print", ex.GetType().Name);
        }

        Domain reparsed;
        try {
            var changes = new PolyDslParser(first, session).Parse();
            var result = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
            if (!result.Succeeded)
                return new CellOutcome("parse", "MutationRejected");
            reparsed = result.Root;
        }
        catch (Exception ex) {
            return new CellOutcome("parse", ex.GetType().Name);
        }

        if (mutateReparsed is not null)
            reparsed = mutateReparsed(reparsed);

        string second;
        try {
            second = new DomainDslPrinter(session).Print(reparsed);
        }
        catch (Exception ex) {
            return new CellOutcome("reprint", ex.GetType().Name);
        }

        if (!string.Equals(first, second, StringComparison.Ordinal))
            return new CellOutcome("reprint", null);
        return string.Equals(DomainGraphCanon.Of(WithoutPrimitives(built)), DomainGraphCanon.Of(WithoutPrimitives(reparsed)), StringComparison.Ordinal)
            ? new CellOutcome(CellOutcome.Ok, null)
            : new CellOutcome("compare", null);
    }

    private static readonly Lazy<IReadOnlyDictionary<string, CellOutcome>> Outcomes = new(() =>
        RoundTripMatrixCells.All.ToDictionary(c => c.Id, c => Run(c), StringComparer.Ordinal));

    // ── the expected failures ────────────────────────────────────────────────────────

    /// <summary>
    /// The known-wrong results, by rule over cell ids. Every cell a rule matches is one expected-failure entry, stale-checked on its own.
    /// All are owned by G-30 (navigations, #850 and #799): a code-built <c>ManyToOne</c> prints bare and reads back as one-to-one; a code-built
    /// <c>ManyToMany</c> prints <c>many T</c> and reads back as one-to-many; a navigation to an entity named like an unsupported keyword
    /// (<c>Schedule</c>) prints text the parser refuses. No shipped surface can create the first two: only the code API.
    /// </summary>
    internal static readonly IReadOnlyList<(string Name, Func<string, bool> Matches, string Owner, string Issue, string Class, string? ExceptionType)> Rules = [
        ("a bare many-to-one reads back as one-to-one, so the second print differs",
            id => id.StartsWith("nav/ManyToOne/", StringComparison.Ordinal) || IsKeywordTarget(id, "ManyToOne", unsupported: false),
            "G-30", "#850", "reprint", null),
        ("many-to-many prints `many T` and reads back as one-to-many, so the model differs",
            id => id.StartsWith("nav/ManyToMany/", StringComparison.Ordinal) || IsKeywordTarget(id, "ManyToMany", unsupported: null),
            "G-30", "#850", "compare", null),
        ("a bare many-to-one to a target named like an unsupported keyword prints text the reader refuses",
            id => IsKeywordTarget(id, "ManyToOne", unsupported: true),
            "G-30", "#799", "parse", nameof(FormatException)),
        ("a Duration expression has no print binder, so `default(12 Days)` cannot be printed (proposed owner: G-34, expression printing; no row names it)",
            id => id.StartsWith("duration/", StringComparison.Ordinal),
            "G-34", "none", "print", nameof(InvalidOperationException)),
    ];

    /// <summary>Whether a <c>keyword-target/&lt;name&gt;/&lt;cardinality&gt;</c> cell has that cardinality and (when asked) the target is, or is not, an unsupported keyword.</summary>
    private static bool IsKeywordTarget(string id, string cardinality, bool? unsupported) {
        var parts = id.Split('/');
        if (parts is not ["keyword-target", var name, var card] || card != cardinality)
            return false;
        var isUnsupported = RoundTripMatrixCells.UnsupportedKeywords.Contains(name, StringComparer.OrdinalIgnoreCase);
        return unsupported is null || unsupported == isUnsupported;
    }

    internal static IReadOnlyList<ExpectedFailure> ExpectedFailures() =>
        [.. RoundTripMatrixCells.All.SelectMany(cell => Rules.Where(r => r.Matches(cell.Id))
            .Select(r => new ExpectedFailure(cell.Id, r.Owner, r.Issue, r.Class, r.ExceptionType)))];

    /// <summary>The pure oracle: the problems of a run against an expected-failure table (empty when the run is exactly as expected).</summary>
    internal static List<string> Verdict(IReadOnlyDictionary<string, CellOutcome> outcomes, IReadOnlyList<ExpectedFailure> table) {
        var problems = new List<string>();
        var entries = table.ToDictionary(e => e.CellId, StringComparer.Ordinal);
        foreach (var entry in table.Where(e => !outcomes.ContainsKey(e.CellId)))
            problems.Add($"expected-failure entry for unknown cell {entry.CellId}: delete it");
        foreach (var (id, outcome) in outcomes.OrderBy(o => o.Key, StringComparer.Ordinal)) {
            var has = entries.TryGetValue(id, out var entry);
            if (outcome.Passed && has)
                problems.Add($"stale: cell {id} passes but has an expected-failure entry (owner {entry!.Owner}): delete the entry");
            else if (!outcome.Passed && !has)
                problems.Add($"cell {id} fails ({outcome}) and has no expected-failure entry");
            else if (!outcome.Passed && has && (entry!.Class != outcome.Class || entry.ExceptionType != outcome.ExceptionType))
                problems.Add($"cell {id} fails as {outcome} but its entry (owner {entry.Owner}) records {entry.Class}{(entry.ExceptionType is null ? "" : ":" + entry.ExceptionType)}");
        }

        return problems;
    }

    // ── the reflection guard ─────────────────────────────────────────────────────────

    /// <summary>The skips: a member with no cell, its reason from the closed set. Listed in the PR body.</summary>
    internal static readonly IReadOnlyDictionary<string, SkipReason> Skips = new Dictionary<string, SkipReason>(StringComparer.Ordinal) {
        ["ConstraintMergeKind.Net"] = SkipReason.AnalysisOnly,
        ["ConstraintMergeKind.Unmergeable"] = SkipReason.AnalysisOnly,
        ["ConstraintMergeKind.Empty"] = SkipReason.AnalysisOnly,
        ["RelationalOrdering.None"] = SkipReason.NotPrinted,
        ["RelationalOrdering.Less"] = SkipReason.NotPrinted,
        ["RelationalOrdering.Equal"] = SkipReason.NotPrinted,
        ["RelationalOrdering.Greater"] = SkipReason.NotPrinted,
        ["RelationalOrdering.All"] = SkipReason.NotPrinted,
        ["Effect.CompositeEffect"] = SkipReason.Flattened,
        ["Constraint.RequiredConstraint"] = SkipReason.NotPrinted,
        ["Constraint.RelationalConstraint"] = SkipReason.NotPrinted,
    };

    /// <summary>Every member the DSL can have a cell for: enum members, effect kinds, constraint kinds and annotation keywords.</summary>
    internal static IReadOnlySet<string> Universe() {
        var universe = new SortedSet<string>(StringComparer.Ordinal);
        var assembly = typeof(Domain).Assembly;
        foreach (var type in assembly.GetTypes().Where(t => t.IsEnum && (t.Namespace ?? "").StartsWith("Poly.DomainModeling.Ontology", StringComparison.Ordinal))
                     .Append(typeof(DurationUnit)))
            foreach (var name in Enum.GetNames(type))
                universe.Add(type.Name + "." + name);
        foreach (var type in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(Effect).IsAssignableFrom(t)))
            universe.Add("Effect." + type.Name);
        foreach (var kind in ConstraintKinds.All)
            universe.Add("Constraint." + kind.Type.Name);
        foreach (var keyword in AnnotationKeywords())
            universe.Add("Annotation." + keyword);
        return universe;
    }

    /// <summary>The annotation keywords: the term keywords and every keyword an <see cref="IAnnotationSyntax"/> with a parameterless constructor declares.</summary>
    internal static IReadOnlyList<string> AnnotationKeywords() {
        var keywords = new SortedSet<string>(TermFacets.Keywords, StringComparer.Ordinal);
        foreach (var type in typeof(IAnnotationSyntax).Assembly.GetTypes()
                     .Where(t => !t.IsAbstract && typeof(IAnnotationSyntax).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null))
            keywords.Add(((IAnnotationSyntax)Activator.CreateInstance(type)!).Keyword);
        return [.. keywords];
    }

    /// <summary>The pure guard: the problems of a universe, the covered members, the skips and the ratchet.</summary>
    internal static List<string> GuardProblems(
        IReadOnlySet<string> universe, IReadOnlySet<string> covered, IReadOnlyDictionary<string, SkipReason> skips, int maxSkips) {
        var problems = new List<string>();
        foreach (var member in universe.Where(m => !covered.Contains(m) && !skips.ContainsKey(m)))
            problems.Add($"{member} has no matrix cell and no skip");
        foreach (var member in skips.Keys.Where(m => !universe.Contains(m)))
            problems.Add($"skip {member} names a member that no longer exists: delete the skip");
        foreach (var member in skips.Keys.Where(covered.Contains))
            problems.Add($"stale skip: {member} now has a cell; delete the skip");
        foreach (var member in covered.Where(m => !universe.Contains(m)))
            problems.Add($"a cell covers {member}, which is not a member of any enumeration");
        if (skips.Count > maxSkips)
            problems.Add($"{skips.Count} skips exceed the ratchet of {maxSkips}");
        return problems;
    }

    private static IReadOnlySet<string> Covered() =>
        RoundTripMatrixCells.All.SelectMany(c => c.Covers).ToHashSet(StringComparer.Ordinal);

    // ── tests ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Cells_RoundTrip_OrAreExpectedToFail() {
        var problems = Verdict(Outcomes.Value, ExpectedFailures());

        await Assert.That(problems).IsEmpty().Because(string.Join("\n", problems));
    }

    [Test]
    public async Task ExpectedFailures_AreRatcheted() {
        await Assert.That(ExpectedFailures().Count).IsLessThanOrEqualTo(MaxExpectedFailureCells);
    }

    [Test]
    public async Task ReflectionGuard_EveryMemberHasACellOrASkip() {
        var problems = GuardProblems(Universe(), Covered(), Skips, MaxSkips);

        await Assert.That(problems).IsEmpty().Because(string.Join("\n", problems));
    }

    [Test]
    public async Task Skips_AreRatcheted_AndExactlyTheCurrentList() {
        await Assert.That(Skips.Count).IsEqualTo(MaxSkips);
    }

    [Test]
    public async Task Matrix_IsLarge_AndEveryDimensionHasCells() {
        var cells = RoundTripMatrixCells.All;
        await Assert.That(cells.Count).IsGreaterThan(300);
        await Assert.That(cells.Select(c => c.Id).Distinct().Count()).IsEqualTo(cells.Count);
        foreach (var dimension in new[] { "nav", "keyword-target", "visibility", "require", "quantifier", "subscription", "effect", "constraint", "annotation", "optionality", "designation", "stage", "comparison", "contract", "duration", "primitive", "declaration", "entity" })
            await Assert.That(cells.Count(c => c.Dimension == dimension)).IsGreaterThan(0).Because(dimension);
        await Assert.That(cells.Count(c => c.Dimension == "nav")).IsEqualTo(16);
        await Assert.That(RoundTripMatrixCells.KeywordTargets).Contains("Schedule");
        await Assert.That(RoundTripMatrixCells.KeywordTargets).Contains("actor");
    }

    // ── self-tests: the oracle fails on a mutated copy ───────────────────────────────

    private static MatrixCell CellById(string id) => RoundTripMatrixCells.All.Single(c => c.Id == id);

    private static Entity OnlyEntity(Domain domain) => domain.Types.OfType<Entity>().Single(e => e.Name == "A");

    private static Domain MapEntity(Domain domain, Func<Entity, Entity> map) =>
        domain with { Types = [.. domain.Types.Select(t => t is Entity { Name: "A" } e ? map(e) : t)] };

    [Test]
    public async Task SelfTest_UnmutatedReparse_Passes() {
        await Assert.That(Run(CellById("optionality/optional")).Passed).IsTrue();
        await Assert.That(Run(CellById("nav/OneToMany/plain/via")).Passed).IsTrue();
        await Assert.That(Run(CellById("constraint/Length/range")).Passed).IsTrue();
    }

    [Test]
    public async Task SelfTest_ReparseThatLosesOptionality_Fails() {
        var outcome = Run(CellById("optionality/optional"), d => MapEntity(d, e =>
            e with { Properties = [.. e.Properties.Select(p => p with { Constraints = [.. p.Constraints.Where(c => c is not OptionalConstraint)] })] }));

        await Assert.That(outcome.Class).IsNotEqualTo(CellOutcome.Ok);
    }

    [Test]
    public async Task SelfTest_ReparseThatLosesVia_FailsTheCompare() {
        var outcome = Run(CellById("nav/OneToMany/plain/via"), d => MapEntity(d, e =>
            e with { Navigations = [.. e.Navigations.Select(n => n with { Inverse = null })] }));

        await Assert.That(outcome.Class).IsNotEqualTo(CellOutcome.Ok);
    }

    [Test]
    public async Task SelfTest_ReparseThatLosesAConstraint_Fails() {
        var outcome = Run(CellById("constraint/Length/range"), d => MapEntity(d, e =>
            e with { Properties = [.. e.Properties.Select(p => p with { Constraints = [.. p.Constraints.Where(c => c is not LengthConstraint)] })] }));

        await Assert.That(outcome.Class).IsNotEqualTo(CellOutcome.Ok);
    }

    /// <summary>
    /// A loss the PRINTED TEXT cannot show: entity-level constraints have no DSL form, so the reparse lacks them and both prints are equal.
    /// Only the structural compare sees it; with the compare neutered this test is red.
    /// </summary>
    [Test]
    public async Task SelfTest_AttributeTheTextDoesNotShow_FailsTheStructuralCompareOnly() {
        var cell = new MatrixCell("self-test/entity-constraint", "self-test", CellSession.Bare,
            () => ModelKit.Dom(ModelKit.Ent("A", [ModelKit.Prop("Name")]) with { Constraints = [new UniqueConstraint()] }), []);

        var outcome = Run(cell);

        await Assert.That(outcome.Class).IsEqualTo("compare");
    }

    [Test]
    public async Task SelfTest_ReparseThatLosesAnInternalAttribute_Fails() {
        // KeyProperties is internal: the compare reads non-public attributes too.
        var outcome = Run(CellById("designation/key/single"), d => MapEntity(d, e => e with { KeyProperties = [] }));

        await Assert.That(outcome.Class).IsNotEqualTo(CellOutcome.Ok);
    }

    [Test]
    public async Task SelfTest_AnEntryForACellThatPasses_IsStale() {
        var outcomes = new Dictionary<string, CellOutcome> { ["x/passes"] = new(CellOutcome.Ok, null) };
        var table = new[] { new ExpectedFailure("x/passes", "G-30", "#850", "compare", null) };

        var problems = Verdict(outcomes, table);

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).Contains("stale");
    }

    [Test]
    public async Task SelfTest_AFailingCellWithNoEntry_Fails() {
        var outcomes = new Dictionary<string, CellOutcome> { ["x/fails"] = new("compare", null) };

        var problems = Verdict(outcomes, []);

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(problems[0]).Contains("no expected-failure entry");
    }

    [Test]
    public async Task SelfTest_AnEntryWhoseClassDiffers_Fails() {
        var outcomes = new Dictionary<string, CellOutcome> { ["x/fails"] = new("parse", "GrammarException") };
        var table = new[] { new ExpectedFailure("x/fails", "G-30", "#799", "compare", null) };

        await Assert.That(Verdict(outcomes, table).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SelfTest_AnEntryForAnUnknownCell_Fails() {
        var table = new[] { new ExpectedFailure("x/gone", "G-30", "#799", "compare", null) };

        await Assert.That(Verdict(new Dictionary<string, CellOutcome>(), table).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SelfTest_AMemberWithNeitherCellNorSkip_Fails() {
        var universe = new SortedSet<string>(StringComparer.Ordinal) { "Visibility.Public", "Visibility.NewMember" };
        var covered = new HashSet<string>(StringComparer.Ordinal) { "Visibility.Public" };

        var problems = GuardProblems(universe, covered, new Dictionary<string, SkipReason>(), maxSkips: 0);

        await Assert.That(problems).Contains("Visibility.NewMember has no matrix cell and no skip");
    }

    [Test]
    public async Task SelfTest_AStaleOrOrphanSkip_AndTheRatchet_Fail() {
        var universe = new SortedSet<string>(StringComparer.Ordinal) { "A.X", "A.Y" };
        var covered = new HashSet<string>(StringComparer.Ordinal) { "A.X" };
        var skips = new Dictionary<string, SkipReason> { ["A.X"] = SkipReason.AnalysisOnly, ["A.Gone"] = SkipReason.AnalysisOnly, ["A.Y"] = SkipReason.AnalysisOnly };

        var problems = GuardProblems(universe, covered, skips, maxSkips: 2);

        await Assert.That(problems.Any(p => p.StartsWith("stale skip: A.X", StringComparison.Ordinal))).IsTrue();
        await Assert.That(problems.Any(p => p.Contains("A.Gone", StringComparison.Ordinal))).IsTrue();
        await Assert.That(problems.Any(p => p.Contains("ratchet", StringComparison.Ordinal))).IsTrue();
    }

    // ── the compare itself ───────────────────────────────────────────────────────────

    [Test]
    public async Task Canon_SeesInternalAttributes_AndExcludesOnlyTheListedOnes() {
        var entity = typeof(Entity);
        var names = DomainGraphCanon.PropertiesOf(entity).Select(p => p.Name).ToList();

        await Assert.That(names).Contains("KeyProperties");
        await Assert.That(names).Contains("InitialStageName");
        await Assert.That(names).Contains("Visibility");
        await Assert.That(DomainGraphCanon.PropertiesOf(typeof(Relationship)).Select(p => p.Name)).Contains("Inverse");
        foreach (var excluded in DomainGraphCanon.Excluded.Keys)
            await Assert.That(names).DoesNotContain(excluded);
    }

    [Test]
    public async Task Canon_ThrowsOnACycle_AndPastTheDepthLimit() {
        var cyclic = new Cycle();
        cyclic.Next = cyclic;
        await Assert.That(() => DomainGraphCanon.Of(cyclic)).Throws<InvalidOperationException>();

        object deep = new Cycle();
        for (var i = 0; i < DomainGraphCanon.MaxDepth + 5; i++)
            deep = new Cycle { Next = deep };
        await Assert.That(() => DomainGraphCanon.Of(deep)).Throws<InvalidOperationException>();
    }

    private sealed class Cycle {
        public object? Next { get; set; }
    }
}