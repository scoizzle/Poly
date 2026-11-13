using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Tests.Docs;
using Poly.Tests.TestHelpers;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T-05b's census and red tests (lock #679 6845700954). The census is derived by <see cref="StrictGateCensus"/>: every
/// grid on master, on the scratch flip and on the head; its delta is <see cref="AnalyzerClosingRoundPinTests.FlipsB"/>.
/// The residual grid is new here and pinned as master plus that delta; <see cref="MasterDoors"/> is its class evidence,
/// recorded on master through every door with a scenario step (A9).
/// </summary>
public class StrictGateCensusTests {
    private static List<(string Code, string Message)> Errors(string dsl) =>
        [.. GuideBlocks.Analyze(dsl).Diagnostics.Where(d => d.IsError).Select(d => (d.Code, d.Message))];

    private static string Codes(string dsl) => StrictGateCensus.Verdict(dsl);

    // ── the census ─────────────────────────────────────────────────────────

    [Test]
    public async Task ResidualGrid_IsMasterPlusTheFlips() =>
        await Assert.That(AnalyzerClosingRoundPinTests.GridDifferences(StrictGateCensus.ResidualRows(),
            AnalyzerClosingRoundPinTests.MasterPlusFlips("Residual", Expected.Residual))).IsEqualTo("");

    /// <summary>
    /// Every residual cell valid on master that T-05b rejects, with the doors that refused it ON MASTER (recorded by
    /// <see cref="StrictGateCensus.ResidualDoorRows"/> with <see cref="StrictGateCensus.ResidualSteps"/>). "none" is a
    /// cell every door accepted on master: three cells, each named in the PR body (A4's typeless pair as a call
    /// argument and as a function body, both computing <c>false</c>; an unknown name as a call argument the function
    /// never reads).
    /// </summary>
    internal static readonly (string Cell, string Refused)[] MasterDoors = [
        ("parts Qty (to-many hop) | assign", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | create", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | invoke", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | call", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | form ==", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | == form", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | match arm", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | entry", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | exit", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | subscription", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | form + N", "simulate+csharp+polyc"),
        ("Bogus (unknown name) | invoke", "simulate+csharp+polyc"),
        ("Bogus (unknown name) | call", "none"),
        ("Bogus (unknown name) | form ==", "simulate+csharp+polyc"),
        ("Bogus (unknown name) | == form", "simulate+csharp+polyc"),
        ("Bogus (unknown name) | form + N", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | assign", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | create", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | invoke", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | call", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | form ==", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | == form", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | match arm", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | entry", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | exit", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | subscription", "csharp+polyc"),
        ("sum parts Label (#846) | form + N", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | assign", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | create", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | invoke", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | call", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | form ==", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | == form", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | match arm", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | entry", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | exit", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | subscription", "csharp+polyc"),
        ("count parts where Label == 1 | form + N", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | assign", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | create", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | invoke", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | call", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | form ==", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | == form", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | match arm", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | entry", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | exit", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | subscription", "csharp+polyc"),
        ("any parts where Label == 1 | if form", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | not form", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | form and Flag", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | for where form", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | match { form -> 1 }", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | assign", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | create", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | invoke", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | call", "none"),
        ("5 == \"x\" (typeless pair) | return", "none"),
        ("5 == \"x\" (typeless pair) | match arm", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | entry", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | exit", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | subscription", "csharp+polyc"),
        ("5 == \"x\" (typeless pair) | if form", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | not form", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | form and Flag", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | for where form", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | match { form -> 1 }", "simulate+csharp+polyc")
    ];

    [Test]
    public async Task DeadGrid_IsMasterPlusTheFlips() =>
        await Assert.That(AnalyzerClosingRoundPinTests.GridDifferences(StrictGateCensus.DeadRows(),
            AnalyzerClosingRoundPinTests.MasterPlusFlips("Dead", Expected.Dead))).IsEqualTo("");

    /// <summary>
    /// C1: every dead cell valid on master that T-05b rejects, with the doors that refused it ON MASTER
    /// (<see cref="StrictGateCensus.DeadDoorRows"/>). The doors compile dead code, so a dead position is refused
    /// wherever the live one is; "none" is the one cell every door accepted on master.
    /// </summary>
    internal static readonly (string Cell, string Refused)[] DeadMasterDoors = [
        ("parts Qty (to-many hop) | pruned assign", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | pruned invoke", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | dead match arm", "simulate+csharp+polyc"),
        ("parts Qty (to-many hop) | action never invoked", "simulate+csharp+polyc"),
        ("Bogus (unknown name) | pruned invoke", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | pruned assign", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | pruned invoke", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | dead match arm", "simulate+csharp+polyc"),
        ("sum parts Label (#846) | action never invoked", "csharp+polyc"),
        ("count parts where Label == 1 | pruned assign", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | pruned invoke", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | dead match arm", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | unused policy", "csharp+polyc"),
        ("count parts where Label == 1 | action require", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | stage require", "simulate+csharp+polyc"),
        ("count parts where Label == 1 | action never invoked", "csharp+polyc"),
        ("any parts where Label == 1 | pruned assign", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | pruned invoke", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | dead match arm", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | unused policy", "csharp+polyc"),
        ("any parts where Label == 1 | action require", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | stage require", "simulate+csharp+polyc"),
        ("any parts where Label == 1 | action never invoked", "csharp+polyc"),
        ("5 == \"x\" (typeless pair) | pruned assign", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | pruned invoke", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | dead match arm", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | unused policy", "csharp+polyc"),
        ("5 == \"x\" (typeless pair) | action require", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | stage require", "simulate+csharp+polyc"),
        ("5 == \"x\" (typeless pair) | action never invoked", "csharp+polyc"),
        ("5 == \"x\" (typeless pair) | function called from an uncalled function", "none"),
        ("Stamp - Stamp (DateTime) | default", "csharp"),
        ("Day - Day (Date) | default", "csharp"),
        ("parts Qty (to-many hop) | default", "csharp"),
        ("part book Qty (to-one hops, control) | default", "csharp"),
        ("sum parts Label (#846) | default", "csharp"),
        ("sum parts Qty (control) | default", "csharp"),
        ("count parts where Label == 1 | default", "csharp"),
        ("any parts where Label == 1 | default", "csharp"),
        ("match with an untyped arm | default", "csharp"),
        ("5 == \"x\" (typeless pair) | default", "csharp"),
        ("count parts (function body) | default", "csharp")
    ];

    [Test]
    public async Task DeadCensus_EveryValidCellItRejects_HasItsMasterDoors() {
        var rejected = AnalyzerClosingRoundPinTests.FlipsDead.Concat(AnalyzerClosingRoundPinTests.FlipsA4).Concat(AnalyzerClosingRoundPinTests.FlipsDefault)
            .Where(f => f.Grid == "Dead" && f.Before == "ok").Select(f => f.Cell).Order(StringComparer.Ordinal);
        await Assert.That(string.Join('\n', rejected))
            .IsEqualTo(string.Join('\n', DeadMasterDoors.Select(d => d.Cell).Order(StringComparer.Ordinal)));
    }

    [Test]
    public async Task ResidualCensus_EveryValidCellItRejects_HasItsMasterDoors() {
        var rejected = AnalyzerClosingRoundPinTests.FlipsB.Concat(AnalyzerClosingRoundPinTests.FlipsA4)
            .Where(f => f.Grid == "Residual" && f.Before == "ok").Select(f => f.Cell).Order(StringComparer.Ordinal);
        await Assert.That(string.Join('\n', rejected))
            .IsEqualTo(string.Join('\n', MasterDoors.Select(d => d.Cell).Order(StringComparer.Ordinal)));
        await Assert.That(MasterDoors.Count(d => d.Refused == "none")).IsEqualTo(3);
    }

    /// <summary>
    /// B-entity in the DSL: every FlipsB B-entity row is API-built, so each rule shape is also written as a whole
    /// document (<see cref="EntityShapes"/>) where the DSL can express it, and run through every door ON MASTER with a
    /// create-and-invoke scenario: the doors that refused it there (for an entity-typed action parameter the
    /// simulator door is NOT CONSTRUCTIBLE and is not counted: a simulate step resolves an instance alias only in
    /// <c>instanceId</c>, <c>sourceInstanceId</c> and <c>targetInstanceId</c>, never inside <c>args</c>, and an instance's
    /// generated id is unknown before the run; tried in the closing round: create a Part as <c>pt</c>, pass <c>"pt"</c>,
    /// answer "instance 'pt' of 'Part' not found"), and the code the head gives. Not expressible in the DSL:
    /// an entity- or value-typed stored property (a DSL entity reference is a relationship; a value type cannot be
    /// stored, DMSTR006), so the FlipsB cells that assign into or read out of one are API-only.
    /// </summary>
    internal static readonly (string Shape, string RefusedOnMaster, string HeadCode)[] EntityShapeDoors = [
        ("entity parameter into a Number property", "csharp", "DMSEM003"),
        ("entity parameter compared with a Number", "csharp", "DMSEM003"),
        ("entity parameter compared with an entity of another type", "csharp", "DMSEM003"),
        ("to-one navigation read as a value, compared with a Number", "csharp", "DMSEM003"),
        ("value-type parameter into a Number property", "csharp", "DMSEM003"),
        ("value-type parameter compared with a Number", "csharp", "DMSEM003"),
        ("Number into a Binary property", "simulate+csharp+polyc", "DMSEM003"),
        ("Text literal into a Binary property", "simulate+csharp+polyc", "DMSEM003"),
        ("entity parameter as a Number function argument", "none", "DMFUN003"),
        ("entity-typed function parameter returned as Number (function never called)", "none", "DMFUN006"),
        ("entity-typed function parameter returned as Number (called)", "csharp", "DMFUN006"),
        ("to-one navigation as a Number function argument", "none", "DMFUN003"),
        ("to-one navigation into a Number property", "analysis (already rejected on master)", "DMSEM003")
    ];

    [Test]
    public async Task EntityShapes_InTheDsl_AreRejectedOnTheHead() {
        var head = StrictGateCensus.Verdict;
        var actual = EntityShapes.Shapes.Select(s => $"{s.Shape} = {head(EntityShapes.Document(s))}");
        await Assert.That(string.Join('\n', actual))
            .IsEqualTo(string.Join('\n', EntityShapeDoors.Select(d => $"{d.Shape} = {d.HeadCode}")));
    }

    /// <summary>
    /// The census class "regression" (runs on every door on master, rejected here), derived from the two door tables;
    /// the PR body's section "Runs on master, rejected here" is this list.
    /// </summary>
    internal static IEnumerable<string> RunsOnMasterRejectedHere() =>
        MasterDoors.Where(d => d.Refused == "none").Select(d => $"residual: {d.Cell}")
            .Concat(DeadMasterDoors.Where(d => d.Refused == "none").Select(d => $"dead: {d.Cell}"))
            .Concat(EntityShapeDoors.Where(d => d.RefusedOnMaster == "none").Select(d => $"B-entity DSL: {d.Shape} ({d.HeadCode})"));

    [Test]
    public async Task RunsOnMasterRejectedHere_IsTheRuledList() =>
        await Assert.That(string.Join('\n', RunsOnMasterRejectedHere())).IsEqualTo(string.Join('\n', [
            "residual: Bogus (unknown name) | call",
            "residual: 5 == \"x\" (typeless pair) | call",
            "residual: 5 == \"x\" (typeless pair) | return",
            "dead: 5 == \"x\" (typeless pair) | function called from an uncalled function",
            "B-entity DSL: entity parameter as a Number function argument (DMFUN003)",
            "B-entity DSL: entity-typed function parameter returned as Number (function never called) (DMFUN006)",
            "B-entity DSL: to-one navigation as a Number function argument (DMFUN003)",
        ]));

    // ── A8: the invariant, on the final analysis result ────────────────────

    /// <summary>The residual forms no rule types: every position that consumes one has an error (A8, I1).</summary>
    [Test]
    public async Task UntypedConsumer_AlwaysHasAnError_InTheFinalAnalysis() {
        string[] untyped = ["parts Qty (to-many hop)", "Bogus (unknown name)", "Nope(N) (undeclared function)", "match with an untyped arm"];
        var silent = StrictGateCensus.ResidualForms.Where(f => untyped.Contains(f.Label))
            .SelectMany(f => StrictGateCensus.ResidualPositions(f).Select(p => (f, p)))
            .Where(c => StrictGateCensus.ResidualDocument(c.f, c.p) is { } dsl && Codes(dsl) == "ok")
            .Select(c => $"{c.f.Label} | {c.p}");
        await Assert.That(string.Join('\n', silent)).IsEqualTo("");
        // Positive companion: the typed control in the same positions stays valid.
        var control = StrictGateCensus.ResidualForms.Single(f => f.Label.StartsWith("part book Qty", StringComparison.Ordinal));
        await Assert.That(Codes(StrictGateCensus.ResidualDocument(control, "assign")!)).IsEqualTo("ok");
    }

    private static Domain Hand(string propertyType, IReadOnlyList<DomainType> declared) =>
        DomainTestFactory.Create("T", [
            .. declared,
            new Entity("E", [new Property("A", new DomainTypeReference(propertyType), []), new Property("B", new DomainTypeReference(propertyType), []),
                new Property("N", new DomainTypeReference("Number"), [])],
                [new DmAction("Copy", InvocationResult.Void, [], [new AssignEffect(DomainExpression.Property("B"), DomainExpression.Property("A"))], []),
                 new DmAction("Wrong", InvocationResult.Void, [], [new AssignEffect(DomainExpression.Property("N"), DomainExpression.Property("A"))], [])],
                [], []),
        ], []);

    /// <summary>A3/A8: a hand-built Date, Time or Duration primitive is typed by its meaning without the temporal map.</summary>
    [Test]
    [Arguments("Date", PrimitiveSemantics.Date)]
    [Arguments("Time", PrimitiveSemantics.Time)]
    [Arguments("Duration", PrimitiveSemantics.Duration)]
    public async Task DeclaredTemporalPrimitive_WithoutTheTemporalMap_IsTypedByItsMeaning(string name, PrimitiveSemantics semantics) {
        var analysis = DomainModelAnalyzer.Analyze(Hand(name, [
            new DmPrimitive("Number", PrimitiveSemantics.Number, []), new DmPrimitive(name, semantics, [])]));
        var errors = analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        // Copy (same type) is clean; Wrong (into Number) is the typed mismatch, never DMSEM006 and never silent.
        await Assert.That(string.Join(",", errors.Select(e => e.Code).Distinct())).IsEqualTo(DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        await Assert.That(errors.Single().Message).Contains("cannot assign");
    }

    /// <summary>A8: a pack override onto a CLR type the model has no primitive for leaves the type untyped: DMSEM006 names it.</summary>
    [Test]
    public async Task PackOverrideToShort_IsDMSEM006_NamingTheMapping() {
        var builder = SessionBuilder.CreateEmpty().LoadCore();
        builder.TypeMaps.OverrideClrTypeName("Number", "short");
        var analysis = builder.Build().Analyze(Hand("Number", [new DmPrimitive("Number", PrimitiveSemantics.Number, [])]));
        var errors = analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        await Assert.That(errors.Count).IsGreaterThan(0);
        await Assert.That(errors.All(e => e.Code == "DMSEM006")).IsTrue();
        await Assert.That(errors[0].Message).Contains("maps to the CLR type 'short'");
    }

    /// <summary>The <c>_ => "unknown"</c> arm of <c>Describe</c> is gone: no residual message names an unknown type.</summary>
    [Test]
    public async Task Describe_NoMessageSaysUnknown() {
        var said = StrictGateCensus.ResidualForms
            .SelectMany(f => StrictGateCensus.ResidualPositions(f).Select(p => StrictGateCensus.ResidualDocument(f, p)))
            .OfType<string>()
            .SelectMany(Errors)
            .Where(e => e.Message.Contains("'unknown'", StringComparison.Ordinal))
            .Select(e => e.Message)
            .Distinct();
        await Assert.That(string.Join('\n', said)).IsEqualTo("");
    }

    // ── DMSEM006: the code, its precedence and its message ─────────────────

    private const string Order = """
        domain T
        Level: enum { Low, High }
        Part: entity {
          Qty: Number
          Label: Text
        }
        Order: entity {
          N: Number
          Name: Text
          Lvl: Level
          Flag: Boolean
          part: Part
          parts: many Part
          Go: action { BODY }
        }
        """;

    private static string With(string body) => Order.Replace("BODY", body, StringComparison.Ordinal);

    [Test]
    public async Task ManyHopRead_IsDMSEM006_SayingWhy() {
        var errors = Errors(With("assign N to parts Qty"));
        await Assert.That(string.Join(",", errors.Select(e => e.Code).Distinct())).IsEqualTo("DMSEM006");
        await Assert.That(errors[0].Message).IsEqualTo(
            "cannot determine the type of 'parts Qty': the path does not reach a property through to-one relationships");
        // C5 (closing round): placed, never 1:1 and never unplaced: on line 14, at the element that holds it (the `Go`
        // action, column 3), as the compile layer places every analysis diagnostic (PolyCompiler.PlaceAnalysis resolves
        // the node's element path to its outline node). A DMSEM003 in the same place sits at the same position.
        var untyped = GuideBlocks.Analyze(With("assign N to parts Qty")).Diagnostics.Single(d => d.IsError);
        await Assert.That($"{untyped.Code} {untyped.Line}:{untyped.Column}").IsEqualTo("DMSEM006 14:3");
        var mismatch = GuideBlocks.Analyze(With("assign N to Name")).Diagnostics.Single(d => d.IsError);
        await Assert.That($"{mismatch.Code} {mismatch.Line}:{mismatch.Column}").IsEqualTo("DMSEM003 14:3");
    }

    /// <summary>A1 precedence: a policy's unknown name is the policy pass's DMSEM005 alone, not doubled by DMSEM006.</summary>
    [Test]
    public async Task UnknownNameInAPolicy_IsDMSEM005Alone() {
        var dsl = With("assign N to 1").Replace("  Go:", "  P: policy { Bogus > 1 }\n  Go:", StringComparison.Ordinal);
        await Assert.That(Codes(dsl)).IsEqualTo("DMSEM005");
        // Positive companion: the same comparison over a declared property is valid.
        await Assert.That(Codes(dsl.Replace("Bogus > 1", "N > 1", StringComparison.Ordinal))).IsEqualTo("ok");
    }

    /// <summary>A1 precedence: an undeclared function is DMFUN001 alone.</summary>
    /// <summary>
    /// A4 (lock): typed operands of different families do not compare, even when neither has a domain name. The C#
    /// a host prints for <c>5 == "x"</c> does not compile (CS0019); <c>5 == 5</c> stays valid.
    /// </summary>
    [Test]
    public async Task TypelessPairOfDifferentFamilies_IsDMSEM003() {
        var errors = Errors(With("if (5 == \"x\") { assign N to 1 }"));
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo("comparison between incompatible types 'Number' and 'Text'");
        await Assert.That(Codes(With("if (5 == 5) { assign N to 1 }"))).IsEqualTo("ok");
    }

    private static string WithDefault(string type, string value) =>
        With("assign N to 1").Replace("  Go:", $"  D: {type} optional default({value})\n  Go:", StringComparison.Ordinal);

    /// <summary>
    /// C2 (closing round): <c>default(...)</c> is a consumer like any other. A default the analyzer cannot type is
    /// DMSEM006 (or the more specific error), a typed default of another type is DMSEM003, and the default's own
    /// expression is walked (a comparison inside it meets the comparison rule).
    /// </summary>
    [Test]
    public async Task Default_IsTypeChecked_LikeEveryOtherConsumer() {
        await Assert.That(Codes(WithDefault("Number", "parts Qty"))).IsEqualTo("DMSEM006");
        // A call in a default is DMFUN008 (master already says so, on the property): not doubled by DMSEM006.
        await Assert.That(Codes(WithDefault("Number", "Nope(1)"))).IsEqualTo("DMFUN008");
        await Assert.That(Codes(WithDefault("Boolean", "5 == \"x\""))).IsEqualTo("DMSEM003");
        await Assert.That(Codes(WithDefault("Number", "5 == 5"))).IsEqualTo("DMSEM003");
        await Assert.That(Codes(WithDefault("Text", "1 + 2"))).IsEqualTo("DMSEM003");
        // Positive companions: a typed default of the property's type stays valid.
        await Assert.That(Codes(WithDefault("Number", "1 + 2"))).IsEqualTo("ok");
        await Assert.That(Codes(WithDefault("Boolean", "5 == 5"))).IsEqualTo("ok");
    }

    /// <summary>
    /// C3 (closing round), one cause one id: a bare name that is no property, parameter or enum member is DMSEM005
    /// (the reference error the guide names for an unknown property) wherever it is consumed; DMSEM006 stays for an
    /// expression that is known but cannot be typed.
    /// </summary>
    [Test]
    public async Task UnknownBareName_IsDMSEM005_InEveryPosition() {
        foreach (var body in new[] {
            "if (Bogus == 1) { assign N to 2 }",
            "assign N to Bogus + 1",
            "if (F(Bogus) == 1) { assign N to 2 }",
        }) {
            var dsl = With(body).Replace("  Go:", "  Take: action (x: Number) { assign N to x }\n  Go:", StringComparison.Ordinal)
                + "\nfunction F(v: Number): Number = v";
            var errors = Errors(dsl);
            await Assert.That(string.Join(",", errors.Select(e => e.Code).Distinct())).IsEqualTo("DMSEM005");
            await Assert.That(errors[0].Message).Contains("'Bogus' is not a property, parameter or enum member");
        }
        var invoke = Order.Replace("Go: action { BODY }", "Take: action (x: Number) { assign N to x }\n  Go: action { invoke Take(x: Bogus) }", StringComparison.Ordinal);
        await Assert.That(Codes(invoke)).IsEqualTo("DMSEM005");
        // Positive companion: a known expression with no type stays DMSEM006.
        await Assert.That(Codes(With("assign N to parts Qty"))).IsEqualTo("DMSEM006");
    }

    [Test]
    public async Task UndeclaredFunction_IsDMFUN001Alone() =>
        await Assert.That(Codes(With("assign N to Nope(N)"))).IsEqualTo("DMFUN001");

    // ── the forms typed first (red on the lenient engine: a typed form meets an existing rule) ──

    [Test]
    public async Task BareEnumMemberArm_IsTypedAsItsEnum() {
        await Assert.That(Codes(With("assign N to match { Flag -> High, else -> Low }"))).IsEqualTo("DMSEM003");
        // Positive companion: into the enum it is valid.
        await Assert.That(Codes(With("assign Lvl to match { Flag -> High, else -> Low }"))).IsEqualTo("ok");
    }

    [Test]
    public async Task BareEnumMember_AgainstItsEnum_IsTyped() {
        await Assert.That(Codes(With("if (Lvl == High) { assign N to 1 }"))).IsEqualTo("ok");
        await Assert.That(Codes(With("if (N == High) { assign N to 1 }"))).IsEqualTo("DMSEM006");
    }

    [Test]
    public async Task GuidKeyword_IsTypedGuid() {
        await Assert.That(Codes(With("if (Guid == N) { assign N to 1 }"))).IsEqualTo("DMSEM003");
        // A5: a Text target still takes it.
        await Assert.That(Codes(With("assign Name to Guid"))).IsEqualTo("ok");
    }

    [Test]
    public async Task DateDifference_IsADuration() {
        const string dsl = """
            domain T
            uses temporal
            Order: entity {
              Stamp: DateTime
              Due: DateTime
              Gap: Duration optional
              N: Number optional
              Go: action { BODY }
            }
            """;
        await Assert.That(StrictGateCensus.Verdict(dsl.Replace("BODY", "assign Gap to Due - Stamp", StringComparison.Ordinal))).IsEqualTo("ok");
        var errors = Errors(dsl.Replace("BODY", "assign N to Due - Stamp", StringComparison.Ordinal));
        await Assert.That(errors.Single().Message).Contains("cannot assign 'duration' to 'Number'");
    }

    [Test]
    public async Task NavigationToAnExpression_IsTheExpressionsType() {
        await Assert.That(Codes(With("assign N to part Qty > 1"))).IsEqualTo("DMSEM003");
        await Assert.That(Codes(With("assign Flag to part Qty > 1"))).IsEqualTo("ok");
    }

    [Test]
    public async Task PathRootedAtAnEntityParameter_IsTyped() {
        var dsl = Order.Replace("Go: action { BODY }", "Go: action (p: Part) { assign Name to p Qty }", StringComparison.Ordinal);
        await Assert.That(Codes(dsl)).IsEqualTo("DMSEM003");
        await Assert.That(Codes(dsl.Replace("assign Name to p Qty", "assign N to p Qty", StringComparison.Ordinal))).IsEqualTo("ok");
    }

    // ── A10: quantifier bodies; #846: sum in every position ────────────────

    [Test]
    public async Task QuantifierBody_IsTypeChecked_InEveryPosition() {
        await Assert.That(Codes(With("if (any parts where Qty == \"x\") { assign N to 1 }"))).IsEqualTo("DMSEM003");
        await Assert.That(Codes(With("assign N to count parts where Label == 1"))).IsEqualTo("DMSEM003");
        await Assert.That(Codes(With("if (any parts where Qty == 1) { assign N to 1 }"))).IsEqualTo("ok");
    }

    [Test]
    public async Task SumOverText_IsDMSEM005_InEveryPosition_Once() {
        foreach (var body in new[] { "assign N to sum parts Label", "if (sum parts Label > 1) { assign N to 1 }", "create in parts { Qty: sum parts Label  Label: \"a\" }" }) {
            var errors = Errors(With(body));
            await Assert.That(string.Join(",", errors.Select(e => e.Code).Distinct())).IsEqualTo(DomainModelDiagnosticCodes.SemanticReferenceResolution);
            await Assert.That(errors[0].Message).IsEqualTo("Sum property 'Label' on entity 'Part' has type 'Text', which is not numeric.");
        }
        // A policy reports it once (the expression analyzer's marker), with the policy message unchanged.
        var policy = With("assign N to 1").Replace("  Go:", "  Big: policy { sum parts Label > 1 }\n  Go:", StringComparison.Ordinal);
        await Assert.That(Errors(policy).Count).IsEqualTo(1);
        await Assert.That(Codes(With("assign N to sum parts Qty"))).IsEqualTo("ok");
    }

    [Test]
    public async Task EntityReadAsAValue_IsTypedAsItsEntity() {
        var dsl = Order.Replace("Go: action { BODY }", "Take: action (x: Number) { assign N to x }\n  Go: action { invoke Take(x: part) }", StringComparison.Ordinal);
        await Assert.That(Codes(dsl)).IsEqualTo("DMSEM003");
    }

    /// <summary>
    /// Writes every grid, and the residual grid through every door, to <c>POLY_CENSUS_DUMP</c> (a directory). Run by
    /// hand on master, on the scratch flip and on the head to derive the census; it asserts nothing else.
    /// </summary>
    [Test]
    [Explicit]
    public async Task Dump_EveryGridAndTheResidualDoors() {
        var directory = Environment.GetEnvironmentVariable("POLY_CENSUS_DUMP")
            ?? throw new InvalidOperationException("Set POLY_CENSUS_DUMP to a directory.");
        Directory.CreateDirectory(directory);
        foreach (var (name, rows) in StrictGateCensus.Grids())
            File.WriteAllText(Path.Combine(directory, $"{name}.txt"), rows());
        if (Environment.GetEnvironmentVariable("POLY_CENSUS_DOORS") is "1")
            File.WriteAllText(Path.Combine(directory, "ResidualDoors.txt"), StrictGateCensus.ResidualDoorRows());
        if (Environment.GetEnvironmentVariable("POLY_CENSUS_DOORS") is "1")
            File.WriteAllText(Path.Combine(directory, "DeadDoors.txt"), StrictGateCensus.DeadDoorRows());
        await Assert.That(Directory.EnumerateFiles(directory).Any()).IsTrue();
    }

    private static class Expected {
        public const string Dead = """
residual form | dead position = verdict
High (bare enum member) | pruned assign = ok
High (bare enum member) | pruned invoke = ok
High (bare enum member) | dead match arm = ok
High (bare enum member) | unused policy = ok
High (bare enum member) | action require = ok
High (bare enum member) | stage require = ok
High (bare enum member) | default = ok
High (bare enum member) | action never invoked = ok
High (bare enum member) | function called from an uncalled function = DMFUN005
Guid (keyword) | pruned assign = ok
Guid (keyword) | pruned invoke = ok
Guid (keyword) | dead match arm = ok
Guid (keyword) | unused policy = DMSEM005
Guid (keyword) | action require = DMSEM005
Guid (keyword) | stage require = DMSEM005
Guid (keyword) | default = ok
Guid (keyword) | action never invoked = ok
Guid (keyword) | function called from an uncalled function = n/a
Stamp - Stamp (DateTime) | pruned assign = ok
Stamp - Stamp (DateTime) | pruned invoke = ok
Stamp - Stamp (DateTime) | dead match arm = ok
Stamp - Stamp (DateTime) | unused policy = ok
Stamp - Stamp (DateTime) | action require = ok
Stamp - Stamp (DateTime) | stage require = ok
Stamp - Stamp (DateTime) | default = ok
Stamp - Stamp (DateTime) | action never invoked = ok
Stamp - Stamp (DateTime) | function called from an uncalled function = n/a
Day - Day (Date) | pruned assign = ok
Day - Day (Date) | pruned invoke = ok
Day - Day (Date) | dead match arm = ok
Day - Day (Date) | unused policy = ok
Day - Day (Date) | action require = ok
Day - Day (Date) | stage require = ok
Day - Day (Date) | default = ok
Day - Day (Date) | action never invoked = ok
Day - Day (Date) | function called from an uncalled function = n/a
parts Qty (to-many hop) | pruned assign = ok
parts Qty (to-many hop) | pruned invoke = ok
parts Qty (to-many hop) | dead match arm = ok
parts Qty (to-many hop) | unused policy = DMREL001
parts Qty (to-many hop) | action require = DMREL001
parts Qty (to-many hop) | stage require = DMREL001
parts Qty (to-many hop) | default = ok
parts Qty (to-many hop) | action never invoked = ok
parts Qty (to-many hop) | function called from an uncalled function = n/a
part book Qty (to-one hops, control) | pruned assign = ok
part book Qty (to-one hops, control) | pruned invoke = ok
part book Qty (to-one hops, control) | dead match arm = ok
part book Qty (to-one hops, control) | unused policy = ok
part book Qty (to-one hops, control) | action require = ok
part book Qty (to-one hops, control) | stage require = ok
part book Qty (to-one hops, control) | default = ok
part book Qty (to-one hops, control) | action never invoked = ok
part book Qty (to-one hops, control) | function called from an uncalled function = n/a
p book Qty (binder hops) | pruned assign = ok
p book Qty (binder hops) | pruned invoke = ok
p book Qty (binder hops) | dead match arm = n/a
p book Qty (binder hops) | unused policy = n/a
p book Qty (binder hops) | action require = n/a
p book Qty (binder hops) | stage require = n/a
p book Qty (binder hops) | default = n/a
p book Qty (binder hops) | action never invoked = ok
p book Qty (binder hops) | function called from an uncalled function = n/a
Bogus (unknown name) | pruned assign = DMSEM003
Bogus (unknown name) | pruned invoke = ok
Bogus (unknown name) | dead match arm = DMMAT004
Bogus (unknown name) | unused policy = DMSEM005
Bogus (unknown name) | action require = DMSEM005
Bogus (unknown name) | stage require = DMSEM005
Bogus (unknown name) | default = DMSEM003
Bogus (unknown name) | action never invoked = DMSEM003
Bogus (unknown name) | function called from an uncalled function = DMFUN005
sum parts Label (#846) | pruned assign = ok
sum parts Label (#846) | pruned invoke = ok
sum parts Label (#846) | dead match arm = ok
sum parts Label (#846) | unused policy = DMSEM005
sum parts Label (#846) | action require = DMSEM005
sum parts Label (#846) | stage require = DMSEM005
sum parts Label (#846) | default = ok
sum parts Label (#846) | action never invoked = ok
sum parts Label (#846) | function called from an uncalled function = n/a
sum parts Qty (control) | pruned assign = ok
sum parts Qty (control) | pruned invoke = ok
sum parts Qty (control) | dead match arm = ok
sum parts Qty (control) | unused policy = ok
sum parts Qty (control) | action require = ok
sum parts Qty (control) | stage require = ok
sum parts Qty (control) | default = ok
sum parts Qty (control) | action never invoked = ok
sum parts Qty (control) | function called from an uncalled function = n/a
count parts where Label == 1 | pruned assign = ok
count parts where Label == 1 | pruned invoke = ok
count parts where Label == 1 | dead match arm = ok
count parts where Label == 1 | unused policy = ok
count parts where Label == 1 | action require = ok
count parts where Label == 1 | stage require = ok
count parts where Label == 1 | default = ok
count parts where Label == 1 | action never invoked = ok
count parts where Label == 1 | function called from an uncalled function = n/a
any parts where Label == 1 | pruned assign = ok
any parts where Label == 1 | pruned invoke = ok
any parts where Label == 1 | dead match arm = ok
any parts where Label == 1 | unused policy = ok
any parts where Label == 1 | action require = ok
any parts where Label == 1 | stage require = ok
any parts where Label == 1 | default = ok
any parts where Label == 1 | action never invoked = ok
any parts where Label == 1 | function called from an uncalled function = n/a
Nope(N) (undeclared function) | pruned assign = DMFUN001
Nope(N) (undeclared function) | pruned invoke = DMFUN001
Nope(N) (undeclared function) | dead match arm = DMFUN001
Nope(N) (undeclared function) | unused policy = DMFUN001
Nope(N) (undeclared function) | action require = DMFUN001
Nope(N) (undeclared function) | stage require = DMFUN001
Nope(N) (undeclared function) | default = DMFUN008
Nope(N) (undeclared function) | action never invoked = DMFUN001
Nope(N) (undeclared function) | function called from an uncalled function = DMFUN001
match with an untyped arm | pruned assign = DMMAT004
match with an untyped arm | pruned invoke = DMMAT004
match with an untyped arm | dead match arm = DMMAT004
match with an untyped arm | unused policy = DMMAT004+DMSEM005
match with an untyped arm | action require = DMMAT004+DMSEM005
match with an untyped arm | stage require = DMMAT004+DMSEM005
match with an untyped arm | default = ok
match with an untyped arm | action never invoked = DMMAT004
match with an untyped arm | function called from an uncalled function = DMFUN005+DMMAT004
5 == "x" (typeless pair) | pruned assign = ok
5 == "x" (typeless pair) | pruned invoke = ok
5 == "x" (typeless pair) | dead match arm = ok
5 == "x" (typeless pair) | unused policy = ok
5 == "x" (typeless pair) | action require = ok
5 == "x" (typeless pair) | stage require = ok
5 == "x" (typeless pair) | default = ok
5 == "x" (typeless pair) | action never invoked = ok
5 == "x" (typeless pair) | function called from an uncalled function = ok
count parts (function body) | pruned assign = ok
count parts (function body) | pruned invoke = ok
count parts (function body) | dead match arm = ok
count parts (function body) | unused policy = ok
count parts (function body) | action require = ok
count parts (function body) | stage require = ok
count parts (function body) | default = ok
count parts (function body) | action never invoked = ok
count parts (function body) | function called from an uncalled function = DMFUN005
""";

        public const string Residual = """
residual form | position = verdict
High (bare enum member) | assign = ok
High (bare enum member) | create = ok
High (bare enum member) | invoke = ok
High (bare enum member) | call = ok
High (bare enum member) | return = DMFUN005
High (bare enum member) | form == = ok
High (bare enum member) | == form = ok
High (bare enum member) | match arm = ok
High (bare enum member) | entry = ok
High (bare enum member) | exit = ok
High (bare enum member) | subscription = ok
Guid (keyword) | assign = ok
Guid (keyword) | create = ok
Guid (keyword) | invoke = ok
Guid (keyword) | call = ok
Guid (keyword) | return = n/a
Guid (keyword) | form == = ok
Guid (keyword) | == form = ok
Guid (keyword) | match arm = ok
Guid (keyword) | entry = ok
Guid (keyword) | exit = ok
Guid (keyword) | subscription = ok
Stamp - Stamp (DateTime) | assign = ok
Stamp - Stamp (DateTime) | create = ok
Stamp - Stamp (DateTime) | invoke = ok
Stamp - Stamp (DateTime) | call = ok
Stamp - Stamp (DateTime) | return = n/a
Stamp - Stamp (DateTime) | form == = ok
Stamp - Stamp (DateTime) | == form = ok
Stamp - Stamp (DateTime) | match arm = ok
Stamp - Stamp (DateTime) | entry = ok
Stamp - Stamp (DateTime) | exit = ok
Stamp - Stamp (DateTime) | subscription = ok
Day - Day (Date) | assign = ok
Day - Day (Date) | create = ok
Day - Day (Date) | invoke = ok
Day - Day (Date) | call = ok
Day - Day (Date) | return = n/a
Day - Day (Date) | form == = ok
Day - Day (Date) | == form = ok
Day - Day (Date) | match arm = ok
Day - Day (Date) | entry = ok
Day - Day (Date) | exit = ok
Day - Day (Date) | subscription = ok
parts Qty (to-many hop) | assign = ok
parts Qty (to-many hop) | create = ok
parts Qty (to-many hop) | invoke = ok
parts Qty (to-many hop) | call = ok
parts Qty (to-many hop) | return = n/a
parts Qty (to-many hop) | form == = ok
parts Qty (to-many hop) | == form = ok
parts Qty (to-many hop) | match arm = ok
parts Qty (to-many hop) | entry = ok
parts Qty (to-many hop) | exit = ok
parts Qty (to-many hop) | subscription = ok
parts Qty (to-many hop) | form + N = ok
part book Qty (to-one hops, control) | assign = ok
part book Qty (to-one hops, control) | create = ok
part book Qty (to-one hops, control) | invoke = ok
part book Qty (to-one hops, control) | call = ok
part book Qty (to-one hops, control) | return = n/a
part book Qty (to-one hops, control) | form == = ok
part book Qty (to-one hops, control) | == form = ok
part book Qty (to-one hops, control) | match arm = ok
part book Qty (to-one hops, control) | entry = ok
part book Qty (to-one hops, control) | exit = ok
part book Qty (to-one hops, control) | subscription = DMSS004
part book Qty (to-one hops, control) | form + N = ok
p book Qty (binder hops) | assign = ok
p book Qty (binder hops) | create = ok
p book Qty (binder hops) | invoke = ok
p book Qty (binder hops) | call = ok
p book Qty (binder hops) | return = n/a
p book Qty (binder hops) | form == = ok
p book Qty (binder hops) | == form = ok
p book Qty (binder hops) | match arm = ok
p book Qty (binder hops) | entry = n/a
p book Qty (binder hops) | exit = n/a
p book Qty (binder hops) | subscription = n/a
p book Qty (binder hops) | form + N = ok
Bogus (unknown name) | assign = DMSEM003
Bogus (unknown name) | create = DMSEM003
Bogus (unknown name) | invoke = ok
Bogus (unknown name) | call = ok
Bogus (unknown name) | return = DMFUN005
Bogus (unknown name) | form == = ok
Bogus (unknown name) | == form = ok
Bogus (unknown name) | match arm = DMMAT004
Bogus (unknown name) | entry = DMSEM003
Bogus (unknown name) | exit = DMSEM003
Bogus (unknown name) | subscription = DMSEM003
Bogus (unknown name) | form + N = ok
sum parts Label (#846) | assign = ok
sum parts Label (#846) | create = ok
sum parts Label (#846) | invoke = ok
sum parts Label (#846) | call = ok
sum parts Label (#846) | return = n/a
sum parts Label (#846) | form == = ok
sum parts Label (#846) | == form = ok
sum parts Label (#846) | match arm = ok
sum parts Label (#846) | entry = ok
sum parts Label (#846) | exit = ok
sum parts Label (#846) | subscription = ok
sum parts Label (#846) | form + N = ok
sum parts Qty (control) | assign = ok
sum parts Qty (control) | create = ok
sum parts Qty (control) | invoke = ok
sum parts Qty (control) | call = ok
sum parts Qty (control) | return = n/a
sum parts Qty (control) | form == = ok
sum parts Qty (control) | == form = ok
sum parts Qty (control) | match arm = ok
sum parts Qty (control) | entry = ok
sum parts Qty (control) | exit = ok
sum parts Qty (control) | subscription = ok
sum parts Qty (control) | form + N = ok
count parts where Label == 1 | assign = ok
count parts where Label == 1 | create = ok
count parts where Label == 1 | invoke = ok
count parts where Label == 1 | call = ok
count parts where Label == 1 | return = n/a
count parts where Label == 1 | form == = ok
count parts where Label == 1 | == form = ok
count parts where Label == 1 | match arm = ok
count parts where Label == 1 | entry = ok
count parts where Label == 1 | exit = ok
count parts where Label == 1 | subscription = ok
count parts where Label == 1 | form + N = ok
any parts where Label == 1 | assign = ok
any parts where Label == 1 | create = ok
any parts where Label == 1 | invoke = ok
any parts where Label == 1 | call = ok
any parts where Label == 1 | return = n/a
any parts where Label == 1 | form == = ok
any parts where Label == 1 | == form = ok
any parts where Label == 1 | match arm = ok
any parts where Label == 1 | entry = ok
any parts where Label == 1 | exit = ok
any parts where Label == 1 | subscription = ok
any parts where Label == 1 | if form = ok
any parts where Label == 1 | not form = ok
any parts where Label == 1 | form and Flag = ok
any parts where Label == 1 | for where form = ok
any parts where Label == 1 | match { form -> 1 } = ok
Nope(N) (undeclared function) | assign = DMFUN001
Nope(N) (undeclared function) | create = DMFUN001
Nope(N) (undeclared function) | invoke = DMFUN001
Nope(N) (undeclared function) | call = DMFUN001
Nope(N) (undeclared function) | return = DMFUN001
Nope(N) (undeclared function) | form == = DMFUN001
Nope(N) (undeclared function) | == form = DMFUN001
Nope(N) (undeclared function) | match arm = DMFUN001
Nope(N) (undeclared function) | entry = DMFUN001
Nope(N) (undeclared function) | exit = DMFUN001
Nope(N) (undeclared function) | subscription = DMFUN001
Nope(N) (undeclared function) | form + N = DMFUN001
match with an untyped arm | assign = DMMAT004
match with an untyped arm | create = DMMAT004
match with an untyped arm | invoke = DMMAT004
match with an untyped arm | call = DMMAT004
match with an untyped arm | return = DMFUN005+DMMAT004
match with an untyped arm | form == = DMMAT004
match with an untyped arm | == form = DMMAT004
match with an untyped arm | match arm = DMMAT004
match with an untyped arm | entry = DMMAT004
match with an untyped arm | exit = DMMAT004
match with an untyped arm | subscription = DMMAT004
match with an untyped arm | form + N = DMMAT004
5 == "x" (typeless pair) | assign = ok
5 == "x" (typeless pair) | create = ok
5 == "x" (typeless pair) | invoke = ok
5 == "x" (typeless pair) | call = ok
5 == "x" (typeless pair) | return = ok
5 == "x" (typeless pair) | form == = DSL0001
5 == "x" (typeless pair) | == form = DSL0001
5 == "x" (typeless pair) | match arm = ok
5 == "x" (typeless pair) | entry = ok
5 == "x" (typeless pair) | exit = ok
5 == "x" (typeless pair) | subscription = ok
5 == "x" (typeless pair) | if form = ok
5 == "x" (typeless pair) | not form = ok
5 == "x" (typeless pair) | form and Flag = ok
5 == "x" (typeless pair) | for where form = ok
5 == "x" (typeless pair) | match { form -> 1 } = ok
count parts (function body) | assign = ok
count parts (function body) | create = ok
count parts (function body) | invoke = ok
count parts (function body) | call = ok
count parts (function body) | return = DMFUN005
count parts (function body) | form == = ok
count parts (function body) | == form = ok
count parts (function body) | match arm = ok
count parts (function body) | entry = ok
count parts (function body) | exit = ok
count parts (function body) | subscription = ok
count parts (function body) | form + N = ok
""";
    }
}