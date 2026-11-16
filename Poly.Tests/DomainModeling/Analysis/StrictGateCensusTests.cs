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
/// The strict gate's behaviour (T-05b, lock #679 6845700954): an expression the analyzer cannot type is never compatible,
/// and each consumer reports why. The residual and dead form grids (<see cref="StrictGateCensus"/>) are compared with the
/// recorded verdicts in <see cref="AnalyzerVerdictGridTests"/>.
/// </summary>
public class StrictGateCensusTests {
    private static List<(string Code, string Message)> Errors(string dsl) =>
        [.. GuideBlocks.Analyze(dsl).Diagnostics.Where(d => d.IsError).Select(d => (d.Code, d.Message))];

    private static string Codes(string dsl) => StrictGateCensus.Verdict(dsl);

    // ── the census ─────────────────────────────────────────────────────────

    /// <summary>
    /// B-entity in the DSL: the API-built B-entity rule shapes are each also written as a whole
    /// document (<see cref="EntityShapes"/>) where the DSL can express it, and run through every door ON MASTER with a
    /// create-and-invoke scenario: the doors that refused it there (for an entity-typed action parameter the
    /// simulator door is NOT CONSTRUCTIBLE and is not counted: a simulate step resolves an instance alias only in
    /// <c>instanceId</c>, <c>sourceInstanceId</c> and <c>targetInstanceId</c>, never inside <c>args</c>, and an instance's
    /// generated id is unknown before the run; tried in the closing round: create a Part as <c>pt</c>, pass <c>"pt"</c>,
    /// answer "instance 'pt' of 'Part' not found"), and the code the head gives. Not expressible in the DSL:
    /// an entity- or value-typed stored property (a DSL entity reference is a relationship; a value type cannot be
    /// stored, DMSTR006), so the cells that assign into or read out of one are API-only.
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

}