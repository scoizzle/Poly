using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Effects;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #444: a possibly-null source copied into a required target is DMEFF014 unless a provable
/// guard clears it. Four guards: a dominating <c>if</c>, an action <c>require</c>, an entry
/// policy on every transition into the stage, and a stage <c>require</c> (the invariant).
/// </summary>
public class ProvableGuardTests {
    private const string Head = """
        domain Notes
        Doc: entity {
          Note: Text optional
          Other: Text optional
          Memo: Text
          HasNote: policy { Note is not null }
        """;

    private static string Doc(string body) => Head + "\n" + body + "\n}\n";

    private static (DomainSession Session, Domain Domain, AnalysisResult Analysis) Run(
        string poly,
        IReadOnlyList<ObservedFlow>? flows = null,
        IReadOnlyList<DiagnosticAllowance>? allowances = null) {
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
            .WithObservedFlows(flows)
            .WithDiagnosticAllowances(allowances)
            .Build();
        var changes = new PolyDslParser(poly, session).Parse();
        var domain = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session).Root;
        return (session, domain, session.Analyze(domain));
    }

    private static IEnumerable<Poly.Analysis.Diagnostic> Of(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Where(d => d.Code == code);

    private static async Task AssertNoCopyFinding(string body) {
        var (_, _, analysis) = Run(Doc(body));
        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(0);
    }

    private static async Task AssertCopyFinding(string body) {
        var (_, _, analysis) = Run(Doc(body));
        var finding = Of(analysis, "DMEFF014").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(finding.Node).IsTypeOf<AssignEffect>();
    }

    [Test]
    public async Task IfPresence_DominatesCopy_ClearsDMEFF014() {
        await AssertNoCopyFinding("Copy: action { if (Note is not null) { assign Memo to Note } }");
    }

    [Test]
    public async Task IfPresence_Forms_ClearDMEFF014() {
        foreach (var body in new[] {
            "Copy: action { if (Note != null) { assign Memo to Note } }",
            "Copy: action { if (null is not Note) { assign Memo to Note } }",
            "Copy: action { if (Note exists) { assign Memo to Note } }",
            "Copy: action { if (not (Note is null)) { assign Memo to Note } }",
            "Copy: action { if (Note is null) { } else { assign Memo to Note } }",
            "Copy: action { if (Note is not null and Memo is not null) { assign Memo to Note } }",
            "Copy: action { if (Note is not null) { if (Other is not null) { assign Memo to Note } } }",
        }) {
            var (_, _, analysis) = Run(Doc(body));
            await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(0);
        }
    }

    [Test]
    public async Task IfPresence_DoesNotDominate_IsDMEFF014() {
        foreach (var body in new[] {
            "Copy: action { if (Note is not null) { } assign Memo to Note }",
            "Copy: action { if (Note is not null) { assign Memo to Note } else { assign Memo to Note } }",
            "Copy: action { if (Note is null) { assign Memo to Note } }",
            "Copy: action { if (Other is not null) { assign Memo to Note } }",
            "Copy: action { if (Note is not null or Memo is not null) { assign Memo to Note } }",
            "Copy: action { if (Note is not null) { assign Memo to Note } assign Memo to Note }",
        }) {
            var (_, _, analysis) = Run(Doc(body));
            await Assert.That(Of(analysis, "DMEFF014").Count()).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task IfPresence_InterveningNull_DoesNotClear() {
        await AssertCopyFinding("""
            Copy: action {
              if (Note is not null) {
                assign Note to null
                assign Memo to Note
              }
            }
            """);
    }

    [Test]
    public async Task ActionRequire_PresencePolicy_ClearsDMEFF014() {
        await AssertNoCopyFinding("Copy: action require HasNote { assign Memo to Note }");
    }

    [Test]
    public async Task EntityPolicy_NotRequired_DoesNotClear() {
        await AssertCopyFinding("""
            Copy: action { assign Memo to Note }
            Other2: action require HasNote { }
            """);
        await AssertCopyFinding("""
            HasMemo: policy { Memo is not null }
            Copy: action require HasMemo { assign Memo to Note }
            """);
        await AssertCopyFinding("Copy: action require not HasNote { assign Memo to Note }");
    }

    [Test]
    public async Task OrPolicy_DoesNotProvePresence() {
        await AssertCopyFinding("""
            EitherNote: policy { Note is not null or Other is not null }
            Copy: action require EitherNote { assign Memo to Note }
            """);
    }

    [Test]
    public async Task StageRequire_ClearsDMEFF014() {
        const string stage = """
            Open: stage require HasNote {
              Copy: action { assign Memo to Note }
            }
            """;
        var (session, domain, analysis) = Run(Doc(stage));
        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(0);
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);

        var open = domain.Types.OfType<Entity>().Single().Stages.Single();
        await Assert.That(open.Policies.Select(p => p.Name).ToList()).IsEquivalentTo(["HasNote"]);
        await Assert.That(ReferenceEquals(
            open.Policies.Single().Expression,
            domain.Types.OfType<Entity>().Single().Policies.Single(p => p.Name == "HasNote").Expression)).IsTrue();

        var printed = new DomainDslPrinter().Print(domain);
        await Assert.That(printed).Contains("require HasNote");
        var reparsed = Run(printed).Domain;
        await Assert.That(new DomainDslPrinter().Print(reparsed)).IsEqualTo(printed);
    }

    [Test]
    public async Task StageRequireNot_RoundTrips() {
        var (_, domain, analysis) = Run(Doc("""
            Open: stage require not HasNote {
              Copy: action { assign Memo to "x" }
            }
            """));
        await Assert.That(analysis.HasErrors).IsFalse();

        var printed = new DomainDslPrinter().Print(domain);
        await Assert.That(printed).Contains("require not HasNote");
        var reparsed = Run(printed).Domain;
        await Assert.That(new DomainDslPrinter().Print(reparsed)).IsEqualTo(printed);
    }

    [Test]
    public async Task StageRequireNot_Lowers_ToCompilableCSharp() {
        var (_, domain, analysis) = Run(Doc("""
            Open: stage require not HasNote {
              Copy: action { assign Memo to "x" }
            }
            """));
        await Assert.That(analysis.HasErrors).IsFalse();
        var cs = new Poly.Interpretation.CSharp.CSharpGenerator().Generate(
            Poly.DomainModeling.Lowering.DomainProgramProjection.ToSyntax(domain, analysis));
        await Assert.That(cs).DoesNotContain("not_HasNote");
        Poly.Tests.TestHelpers.ExportedCSharp.CompileAndLoad(cs);
    }

    [Test]
    public async Task InvalidEntryGuard_DoesNotMaskStageInvariantViolation() {
        var (session, domain, analysis) = Run(Doc("""
            HasMemo: policy { Memo is not null }
            Draft: stage {
              Start: action require HasNote { transition to Open }
            }
            Open: stage require HasMemo {
              Wipe: action { assign Note to null }
              Copy: action { assign Memo to Note }
            }
            """));
        await Assert.That(Of(analysis, "DMEFF015").Count()).IsGreaterThan(0);
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task TransitionOut_DropsStageFacts_IsDMEFF014() {
        var (session, domain, analysis) = Run(Doc("""
            Draft: stage {
              Start: action require HasNote { transition to Open }
            }
            Open: stage {
              Late: action {
                transition to Closed
                assign Memo to Note
              }
            }
            Closed: stage {
              entry { assign Note to null }
            }
            """));
        var finding = Of(analysis, "DMEFF014").Single();
        await Assert.That(finding.Node).IsTypeOf<AssignEffect>();
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task EntryPolicy_RenullAfterTransition_DoesNotClear() {
        await AssertCopyFinding("""
            Draft: stage {
              Start: action require HasNote {
                transition to Open
                assign Note to null
              }
            }
            Open: stage {
              Copy: action { assign Memo to Note }
            }
            """);
    }

    [Test]
    public async Task StageRequire_AssignFromProvenOptional_IsNotDMEFF015() {
        var (_, _, analysis) = Run("""
            domain Notes
            Doc: entity {
              Note: Text optional
              Memo: Text optional
              HasNote: policy { Note is not null }
              HasMemo: policy { Memo is not null }
              Open: stage require HasNote require HasMemo {
                Copy: action { assign Note to Memo }
                Refresh: action { assign Note to Note }
              }
            }
            """);
        await Assert.That(Of(analysis, "DMEFF015").Count()).IsEqualTo(0);
        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(0);
    }

    [Test]
    public async Task EntryPolicy_SelfAssignSibling_StillClears() {
        await AssertNoCopyFinding("""
            Draft: stage {
              Start: action require HasNote { transition to Open }
            }
            Open: stage {
              Refresh: action { assign Note to Note }
              Copy: action { assign Memo to Note }
            }
            """);
    }

    [Test]
    public async Task StageRequire_SuppressedByRequireNot_IsDMEFF014() {
        await AssertCopyFinding("""
            Open: stage require HasNote {
              Copy: action require not HasNote { assign Memo to Note }
            }
            """);
        await AssertCopyFinding("""
            Open: stage require HasNote {
            }
            Copy: action require not HasNote { assign Memo to Note }
            """);
    }

    [Test]
    public async Task Printer_ApiStagePolicy_NotAnEntityRequire_Throws() {
        var result = new DomainEvolution(DomainFactory.Create("D")).Evolve()
            .AddEntity("Item")
            .AddStage("Item", "Open")
            .AddPolicyToStage("Item", "Open", "Ready", DomainExpression.Literal(true))
            .Apply();
        await Assert.That(result.Succeeded).IsTrue();
        Assert.Throws<InvalidOperationException>(() => new DomainDslPrinter().Print(result.Root));
    }

    [Test]
    public async Task StageRequire_HeaderForms_RoundTrip() {
        var (_, domain, _) = Run(Doc("""
            HasMemo: policy { Memo is not null }
            IsBlocked: policy { Other is not null }
            Open: stage require HasNote, HasMemo
              require HasMemo
              require not IsBlocked {
              Copy: action { assign Memo to Note }
            }
            """));
        var open = domain.Types.OfType<Entity>().Single().Stages.Single();
        await Assert.That(open.Policies.Select(p => p.Name).Count(n => n == "HasNote")).IsEqualTo(1);
        await Assert.That(open.Policies.Count).IsGreaterThanOrEqualTo(3);

        var printed = new DomainDslPrinter().Print(domain);
        await Assert.That(printed).Contains("Open: stage\n    require HasNote, HasMemo".Replace("\n", Environment.NewLine));
        await Assert.That(printed).Contains("require not IsBlocked {");
        await Assert.That(printed).DoesNotContain("stage {" + Environment.NewLine + "    require");
        var reparsed = Run(printed).Domain;
        await Assert.That(new DomainDslPrinter().Print(reparsed)).IsEqualTo(printed);
    }

    [Test]
    public async Task InBodyStageRequire_IsPositionedError() {
        var ex = Assert.Throws<FormatException>(() => Run(Doc("""
            Open: stage {
              require HasNote
              Copy: action { assign Memo to Note }
            }
            """)));
        await Assert.That(ex.Message).Contains("'require' must appear before the stage's '{'");
        await Assert.That(ex.Message).Contains("(line 8, col 3)");
    }

    [Test]
    public async Task StageRequire_UnknownPolicy_IsPositionedError() {
        var ex = Assert.Throws<FormatException>(() => Run(Doc("""
            Open: stage require Nope {
            }
            """)));
        await Assert.That(ex.Message).Contains("Open");
        await Assert.That(ex.Message).Contains("Nope");
        await Assert.That(ex.Message).Contains("not defined");
        await Assert.That(ex.Message).Contains("(line 7, col 13)");
    }

    [Test]
    public async Task StagePolicy_OnEntryCopy_StaysDMEFF014() {
        await AssertCopyFinding("""
            Open: stage require HasNote {
              entry { assign Memo to Note }
            }
            """);
    }

    [Test]
    public async Task StagePolicy_InStageNullAssign_IsDMEFF015() {
        var (session, domain, analysis) = Run(Doc("""
            Open: stage require HasNote {
              Copy: action { assign Memo to Note }
              Clear: action { assign Note to null }
            }
            """));

        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(0);
        var finding = Of(analysis, "DMEFF015").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(finding.Node).IsTypeOf<AssignEffect>();
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task GuardedAssign_IsNotARennull() {
        var (_, _, analysis) = Run(Doc("""
            Open: stage require HasNote {
              Copy: action { assign Memo to Note }
              Refill: action { if (Other is not null) { assign Note to Other } }
              Fill: action { assign Note to "text" }
              Move: action { assign Note to Memo }
            }
            """));
        await Assert.That(Of(analysis, "DMEFF015").Count()).IsEqualTo(0);

        var (_, _, bad) = Run(Doc("""
            Open: stage require HasNote {
              Refill: action { assign Note to Other }
            }
            """));
        await Assert.That(Of(bad, "DMEFF015").Count()).IsEqualTo(1);
    }

    private const string EntryFixture = """
        Draft: stage {
          Start: action require HasNote { transition to Open }
        }
        Open: stage {
          Copy: action { assign Memo to Note }
        %%
        }
        """;

    [Test]
    public async Task EntryPolicy_NoSiblingInvalidator_ClearsDMEFF014() {
        await AssertNoCopyFinding(EntryFixture.Replace("%%", ""));
    }

    [Test]
    public async Task EntryPolicy_SiblingCanNull_DoesNotClear() {
        var (_, _, analysis) = Run(Doc(EntryFixture.Replace("%%", "Clear: action { assign Note to null }")));
        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(1);
        await Assert.That(Of(analysis, "DMEFF015").Count()).IsEqualTo(0);
    }

    [Test]
    public async Task EntryPolicy_EntityLevelNull_DoesNotClear() {
        var wipe = EntryFixture.Replace("%%", "") + "\nWipe: action { assign Note to null }";
        var (_, _, analysis) = Run(Doc(wipe));
        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(1);
        await Assert.That(Of(analysis, "DMEFF015").Count()).IsEqualTo(0);

        var withRequire = wipe.Replace("Open: stage {", "Open: stage require HasNote {");
        var (_, _, required) = Run(Doc(withRequire));
        await Assert.That(Of(required, "DMEFF015").Count()).IsEqualTo(1);
        await Assert.That(Of(required, "DMEFF014").Count()).IsEqualTo(0);
    }

    [Test]
    public async Task EntryPolicy_EntryActionNullsBeforeTransition_DoesNotClear() {
        await AssertCopyFinding("""
            Draft: stage {
              Start: action require HasNote {
                assign Note to null
                transition to Open
              }
            }
            Open: stage {
              Copy: action { assign Memo to Note }
            }
            """);
    }

    [Test]
    public async Task EntryPolicy_InitialStage_IsNotCovered() {
        await AssertCopyFinding("""
            Draft: stage {
              Copy: action { assign Memo to Note }
              Start: action require HasNote { transition to Open }
            }
            Open: stage {
              Back: action require HasNote { transition to Draft }
            }
            """);
    }

    [Test]
    public async Task EntryPolicy_UnguardedTransitionAlongsideGuardedOne_DoesNotClear() {
        await AssertCopyFinding("""
            Draft: stage {
              Start: action require HasNote { transition to Open }
              Skip: action { transition to Open }
            }
            Open: stage {
              Copy: action { assign Memo to Note }
            }
            """);
    }

    [Test]
    public async Task EntityLevelCopy_ClearedOnlyWhenEveryStageProvesPresence() {
        const string stages = """
            Draft: stage %A%{
            }
            Open: stage %B%{
            }
            Copy: action { assign Memo to Note }
            """;
        var (_, _, one) = Run(Doc(stages.Replace("%A%", "require HasNote ").Replace("%B%", "")));
        await Assert.That(Of(one, "DMEFF014").Count()).IsEqualTo(1);

        var (_, _, both) = Run(Doc(stages.Replace("%A%", "require HasNote ").Replace("%B%", "require HasNote ")));
        await Assert.That(Of(both, "DMEFF014").Count()).IsEqualTo(0);
    }

    [Test]
    public async Task EntryPolicy_TransitionInSubscription_DoesNotCount() {
        var (_, _, analysis) = Run("""
            domain Notes
            Doc: entity {
              Note: Text optional
              Memo: Text
              HasNote: policy { Note is not null }
              Tracks: one Peer
              Draft: stage {
                when Tracks Active { transition to Open }
                Start: action require HasNote { transition to Open }
              }
              Open: stage {
                Copy: action { assign Memo to Note }
              }
            }
            Peer: entity {
              Idle: stage { }
              Active: stage { }
            }
            """);
        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(1);

        var (_, _, onEntry) = Run(Doc("""
            Draft: stage {
              entry { transition to Open }
              Start: action require HasNote { transition to Open }
            }
            Open: stage {
              Copy: action { assign Memo to Note }
            }
            """));
        await Assert.That(Of(onEntry, "DMEFF014").Count()).IsEqualTo(1);
    }

    [Test]
    public async Task UnguardedCopy_IsDMEFF014_AndLowerRefuses() {
        var (session, domain, analysis) = Run(Doc("Copy: action { assign Memo to Note }"));
        var finding = Of(analysis, "DMEFF014").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        var lower = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(lower.Message).IsEqualTo(finding.Message);
    }

    [Test]
    public async Task EntryAndExitAndSubscriptionCopies_AreNotCleared() {
        await AssertCopyFinding("""
            Open: stage {
              entry { assign Memo to Note }
            }
            """);
        await AssertCopyFinding("""
            Open: stage {
              exit { assign Memo to Note }
            }
            """);
    }

    private const string NullabilityDomain = """
        domain Orders
        Customer: entity {
          Code: Text
          Note: Text optional length(0, 50)
        }
        Order: entity {
          Note: Text length(0, 50)
          Memo: Text optional
        }
        """;

    private const string WidthAndNullabilityDomain = """
        domain Orders
        Customer: entity {
          Note: Text optional length(0, 200)
        }
        Order: entity {
          Note: Text length(0, 50)
        }
        """;

    private const string OptionalTargetDomain = """
        domain Orders
        Customer: entity {
          Note: Text optional length(0, 200)
        }
        Order: entity {
          Note: Text optional length(0, 50)
        }
        """;

    private const string NotePath = "Orders/Order/Note";
    private static readonly string[] Ids = ["plsql-1", "csharp-2"];
    private static readonly ObservedFlow NotesFlow = new("Customer.Note", "Order.Note");

    [Test]
    public async Task Allowance_MatchingDMEFF014_DoesNotDowngrade_IsDMALW002() {
        var (session, domain, analysis) = Run(
            NullabilityDomain, [NotesFlow], [new DiagnosticAllowance("DMEFF014", NotePath, Ids)]);

        await Assert.That(Of(analysis, "DMEFF014").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        var refused = Of(analysis, "DMALW002").Single();
        await Assert.That(refused.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Of(analysis, "DMALW001").Count()).IsEqualTo(0);
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));

        var (_, _, both) = Run(
            WidthAndNullabilityDomain, [NotesFlow],
            [new DiagnosticAllowance("DMEFF008", NotePath, Ids), new DiagnosticAllowance("DMEFF014", NotePath, Ids)]);
        await Assert.That(Of(both, "DMEFF008").Single().Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Of(both, "DMEFF014").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Of(both, "DMALW002").Count()).IsEqualTo(1);
        await Assert.That(Of(both, "DMALW001").Count()).IsEqualTo(0);
    }

    [Test]
    public async Task NullabilityAllowance_NoFinding_StaysDMALW001() {
        var (_, _, analysis) = Run(
            OptionalTargetDomain, [NotesFlow], [new DiagnosticAllowance("DMEFF014", NotePath, Ids)]);

        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(0);
        await Assert.That(Of(analysis, "DMALW001").Count()).IsEqualTo(1);
        await Assert.That(Of(analysis, "DMALW002").Count()).IsEqualTo(0);
    }

    private static Property OptionalParameter() =>
        new("note", new DomainTypeReference("Text"), [new OptionalConstraint()]);

    private static Property OptionalNote() =>
        new("Note", new DomainTypeReference("Text"), [new OptionalConstraint()]);

    private static Property RequiredMemo() => new("Memo", new DomainTypeReference("Text"), []);

    private static Domain ParameterDomain(
        IReadOnlyList<Effect> effects, IReadOnlyList<Stage>? stages = null, bool inStage = false) {
        var set = new Action("Set", InvocationResult.Void, [OptionalParameter()], effects, []);
        var hasNote = new Policy("HasNote", DomainExpression.NotEqual(
            DomainExpression.Property("Note"), DomainExpression.Literal(null)));
        var entity = new Entity("Doc", [OptionalNote(), RequiredMemo()],
            inStage ? [] : [set], [hasNote],
            inStage ? [new Stage("Open", [set], [hasNote], [], [])] : stages ?? []);
        return DomainTestFactory.Create(
            "Notes", [new PrimitiveType("Text", PrimitiveSemantics.Text, []), entity], []);
    }

    private static AssignEffect CopyFromParameter() =>
        new(DomainExpression.Property("Memo"), DomainExpression.Parameter("note"));

    [Test]
    public async Task Parameter_Unguarded_IsDMEFF014_AndDominatingIfClears() {
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal().Build();

        var bare = session.Analyze(ParameterDomain([CopyFromParameter()]));
        await Assert.That(Of(bare, "DMEFF014").Count()).IsEqualTo(1);

        var guard = DomainExpression.NotEqual(DomainExpression.Parameter("note"), DomainExpression.Literal(null));
        var guarded = session.Analyze(ParameterDomain([new ConditionalEffect(guard, [CopyFromParameter()], null)]));
        await Assert.That(Of(guarded, "DMEFF014").Count()).IsEqualTo(0);

        var wrongName = DomainExpression.NotEqual(DomainExpression.Property("Note"), DomainExpression.Literal(null));
        var other = session.Analyze(ParameterDomain([new ConditionalEffect(wrongName, [CopyFromParameter()], null)]));
        await Assert.That(Of(other, "DMEFF014").Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Parameter_StageRequireOnProperty_DoesNotClearParameterCopy() {
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal().Build();
        var analysis = session.Analyze(ParameterDomain([CopyFromParameter()], inStage: true));
        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(1);
    }
}