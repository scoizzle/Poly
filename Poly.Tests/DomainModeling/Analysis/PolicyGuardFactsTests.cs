using System.Globalization;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.DomainModeling.Runtime;
using Poly.Tests.TestHelpers;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// V-18 (#680, #809): a <c>require</c>'d policy narrows the abstract environment through one seam,
/// <c>PolicyGuardFacts</c>, and create and create-in initializers read the environment an assignment reads.
/// Characterisation pins first (recorded on master 26c3cb53 before any production edit; a pin V-18 changes on
/// purpose carries master's value and the reason), then the red tests of each plan step.
/// </summary>
public class PolicyGuardFactsTests {
    /// <summary>Parses and applies <paramref name="dsl"/> without analysis, then analyzes the applied tree, so a
    /// domain the analysis rejects is still the tree its facts are recorded on.</summary>
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl, params DomainChange[] extra) {
        var changes = new PolyDslParser(dsl).Parse().Concat(extra).ToList();
        var applied = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!applied.Succeeded)
            throw new InvalidOperationException("mutation failed: " + applied.FailureSummary);
        return (applied.Root, DomainModelAnalyzer.Analyze(applied.Root));
    }

    private static Entity EntityNamed(Domain domain, string name) =>
        domain.Types.OfType<Entity>().SingleOrDefault(e => e.Name == name)
        ?? throw new InvalidOperationException($"no entity '{name}'");

    private static Action ActionNamed(Entity entity, string name) =>
        entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions)).SingleOrDefault(a => a.Name == name)
        ?? throw new InvalidOperationException($"no action '{entity.Name}.{name}'");

    private static ActionStageInvariant Context(Domain domain, AnalysisResult analysis, string entity, string action) {
        var act = ActionNamed(EntityNamed(domain, entity), action);
        var invariants = analysis.GetActionInvariants(act)
            ?? throw new InvalidOperationException($"no invariant metadata on '{entity}.{action}'");
        return invariants.StageContexts().Single();
    }

    private static string Num(decimal? value) =>
        value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "";

    private static string Range(ValueRange? range) => range is null ? "none" : $"[{Num(range.Min)},{Num(range.Max)}]";

    private static string Narrowed(ActionStageInvariant context, string property) =>
        context.NarrowedRanges.TryGetValue(property, out var range)
            ? Range(range)
            : throw new InvalidOperationException($"no narrowed entry for '{property}'");

    /// <summary>The range findings (DMEFF008, DMCS003, DMCS001) as <c>CODE!</c> (error) or <c>CODE?</c> (warning)
    /// with their message, in report order; <c>clean</c> when there are none.</summary>
    private static string RangeFindings(AnalysisResult analysis) {
        var findings = analysis.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(d => d.Code is DomainModelDiagnosticCodes.EffectConstraintViolation
                or DomainModelDiagnosticCodes.ConstraintUndecided
                or DomainModelDiagnosticCodes.ConstraintSatisfiability)
            .Select(d => d.Code + (d.Severity == DiagnosticSeverity.Error ? "!" : "?") + " " + d.Message)
            .ToList();
        return findings.Count == 0 ? "clean" : string.Join(" | ", findings);
    }

    /// <summary>Every error code, sorted and distinct; <c>none</c> when there are none.</summary>
    private static string ErrorCodes(AnalysisResult analysis) {
        var codes = analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Code).Distinct().Order(StringComparer.Ordinal).ToList();
        return codes.Count == 0 ? "none" : string.Join(",", codes);
    }

    private static ModuleInstanceExecutor Simulator(string dsl, out Domain domain) {
        (domain, _) = EvolvedDomain.FromDsl(dsl);
        return ModuleExec.For(domain);
    }

    private static ModuleInstance Make(ModuleInstanceExecutor exec, string entity, params (string Name, object? Value)[] values) {
        var created = exec.Create(entity, values.ToDictionary(v => v.Name, v => v.Value));
        if (!created.Succeeded)
            throw new InvalidOperationException($"create {entity} failed: {created.Error}");
        return created.Instance!;
    }

    private static List<ModuleInstance> Items(ModuleInstanceExecutor exec) =>
        exec.Instances.Where(i => i.EntityName == "Item").ToList();

    private static string Outcome(ModuleOutcome outcome) =>
        outcome.Succeeded ? "ok" : "fail: " + outcome.Error;

    private const string PossibleOutside0To10 =
        "DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(0, 10) on property 'Qty'.";

    // ── Documents ───────────────────────────────────────────────

    /// <summary>#809's assignment control: an assign under <c>require Small</c> reads the narrowed environment.</summary>
    private const string AssignUnderRequire = """
        domain C
        Order: entity {
          Qty: Number range(0, 100)
          Local: Number range(0, 10) optional
          Small: policy { Qty <= 10 }
          Copy: action require Small { assign Local to Qty }
        }
        """;

    /// <summary>#809: the same narrowing, read by a create initializer.</summary>
    private const string CreateUnderRequire = """
        domain C
        Item: entity { Qty: Number range(0, 10) }
        Order: entity {
          Qty: Number range(0, 100)
          Small: policy { Qty <= 10 }
          Make: action require Small { create Item { Qty: Qty } }
        }
        """;

    private const string CreateInUnderRequire = """
        domain C
        Item: entity { Qty: Number range(0, 10) }
        Order: entity {
          Qty: Number range(0, 100)
          items: many Item
          Small: policy { Qty <= 10 }
          Make: action require Small { create in items { Qty: Qty } }
        }
        """;

    private static string CreateUnderRequireNot(string itemRange) => $$"""
        domain C
        Item: entity { Qty: Number {{itemRange}} }
        Order: entity {
          Qty: Number range(0, 100)
          Small: policy { Qty <= 10 }
          Make: action require not Small { create Item { Qty: Qty } }
        }
        """;

    /// <summary>A stage requires <c>Small</c> and its action requires <c>not Small</c>: the runtime suppresses the
    /// stage gate (<c>EmitStagePolicyGuards</c>), so the action runs exactly when <c>Small</c> is false.</summary>
    private static string StageSmallActionNotSmall(string seenRange) => $$"""
        domain S
        Order: entity {
          Qty: Number range(0, 100)
          Seen: Number {{seenRange}} optional
          Small: policy { Qty <= 10 }
          Open: stage require Small {
            Peek: action require not Small { assign Seen to Qty }
          }
        }
        """;

    /// <summary>Amendment 2's document: the loop body creates from <c>Qty</c>, then raises it, so the second
    /// iteration's create reads 15 although the guard held 10 at entry.</summary>
    private const string CreateInsideLoop = """
        domain L
        Item: entity { Qty: Number range(0, 10) }
        Line: entity { N: Number optional }
        Order: entity {
          Qty: Number range(0, 100)
          Lines: many Line
          Small: policy { Qty <= 10 }
          Go: action require Small { for l in Lines { create Item { Qty: Qty }  assign Qty to Qty + 5 } }
        }
        """;

    /// <summary>The loop raises <c>Qty</c> zero or more times, then the create reads it: with zero records it
    /// is the entry value (0 is possible), with three it is 25.</summary>
    private const string CreateAfterLoop = """
        domain L
        Item: entity { Qty: Number range(5, 15) }
        Line: entity { N: Number optional }
        Order: entity {
          Qty: Number range(0, 100)
          Lines: many Line
          Small: policy { Qty <= 10 }
          Go: action require Small { for l in Lines { assign Qty to Qty + 5 }  create Item { Qty: Qty } }
        }
        """;

    /// <summary>The transition runs <c>Busy</c>'s entry block, which rewrites <c>Qty</c>, before the create.</summary>
    private const string CreateAfterTransition = """
        domain T
        Item: entity { Qty: Number range(0, 10) }
        Order: entity {
          Qty: Number range(0, 100)
          Small: policy { Qty <= 10 }
          Open: stage {
            Go: action require Small { transition to Busy  create Item { Qty: Qty } }
          }
          Busy: stage {
            entry { assign Qty to 50 }
          }
        }
        """;

    /// <summary>The cross-entity callee invokes back into the caller, which rewrites <c>Qty</c> before the
    /// create.</summary>
    private const string CreateAfterCrossEntityInvoke = """
        domain X
        Item: entity { Qty: Number range(0, 10) }
        Order: entity {
          Qty: Number range(0, 100)
          line: one Line
          Small: policy { Qty <= 10 }
          Go: action require Small { invoke line.Poke  create Item { Qty: Qty } }
          Bump: action { assign Qty to 50 }
        }
        Line: entity {
          order: one Order
          Poke: action { invoke order.Bump }
        }
        """;

    /// <summary>Creating the line enters its initial stage, which fires the creator's subscription; the subscription
    /// rewrites <c>Qty</c> before the second create.</summary>
    private const string CreateAfterCreateInSubscription = """
        domain W
        Item: entity { Qty: Number range(0, 10) }
        Line: entity {
          N: Number optional
          Open: stage { }
        }
        Order: entity {
          Qty: Number range(0, 100)
          lines: many Line
          Small: policy { Qty <= 10 }
          when lines Open { assign Qty to 50 }
          Go: action require Small { create in lines { N: 1 }  create Item { Qty: Qty } }
        }
        """;

    /// <summary>A many hop: the analyzer cannot type <c>parts Qty</c> on master (T-05b makes it DMSEM006).</summary>
    private const string UntypedInitializer = """
        domain U
        Item: entity { Qty: Number range(0, 10) }
        Part: entity { Qty: Number range(0, 5) }
        Order: entity {
          Qty: Number range(0, 100)
          Local: Number range(0, 10) optional
          parts: many Part
          Small: policy { Qty <= 10 }
          Make: action require Small { create Item { Qty: parts Qty } }
          Copy: action require Small { assign Local to parts Qty }
        }
        """;

    /// <summary><c>not (A &gt; 5 and B &gt; 5)</c> is <c>A &lt;= 5 or B &lt;= 5</c>: A alone is not narrowed.</summary>
    private const string CreateUnderRequireNotAnd = """
        domain N
        Item: entity { Qty: Number range(0, 5) }
        Order: entity {
          A: Number range(0, 10)
          B: Number range(0, 10)
          Big: policy { A > 5 and B > 5 }
          Make: action require not Big { create Item { Qty: A } }
        }
        """;

    /// <summary>A self-invoked callee guarded by <c>require not Small</c>.</summary>
    private const string SelfInvokeRequireNot = """
        domain I
        Order: entity {
          Qty: Number range(0, 100)
          Local: Number range(11, 100) optional
          Small: policy { Qty <= 10 }
          Keep: action require not Small { assign Local to Qty }
          Go: action { invoke Keep }
        }
        """;

    private static readonly Dictionary<string, (string Dsl, string Entity, string Action)> Shapes = new(StringComparer.Ordinal) {
        ["assign-require"] = (AssignUnderRequire, "Order", "Copy"),
        ["create-require"] = (CreateUnderRequire, "Order", "Make"),
        ["create-in-require"] = (CreateInUnderRequire, "Order", "Make"),
        ["create-require-not-0-10"] = (CreateUnderRequireNot("range(0, 10)"), "Order", "Make"),
        ["create-require-not-11-100"] = (CreateUnderRequireNot("range(11, 100)"), "Order", "Make"),
        ["create-require-not-and"] = (CreateUnderRequireNotAnd, "Order", "Make"),
        ["stage-p-action-not-p"] = (StageSmallActionNotSmall("range(0, 60)"), "Order", "Peek"),
        ["create-inside-loop"] = (CreateInsideLoop, "Order", "Go"),
        ["create-after-loop"] = (CreateAfterLoop, "Order", "Go"),
        ["create-after-transition"] = (CreateAfterTransition, "Order", "Go"),
        ["create-after-cross-invoke"] = (CreateAfterCrossEntityInvoke, "Order", "Go"),
        ["create-after-create-in-subscription"] = (CreateAfterCreateInSubscription, "Order", "Go"),
        ["untyped-initializer"] = (UntypedInitializer, "Order", "Make"),
        ["self-invoke-require-not"] = (SelfInvokeRequireNot, "Order", "Keep"),
    };

    // ── A. Characterisation pins ────────────────────────────────

    /// <summary>Every shape the slice touches, as master analyses it: the probed action's narrowed <c>Qty</c> (or
    /// <c>A</c>), the error codes, and the range findings. A row V-18 moves on purpose carries master's value.</summary>
    [Test]
    [Arguments("assign-require", "Qty=[0,10] errors=none clean")]
    [Arguments("create-require", "Qty=[0,10] errors=none clean")] // V-18 D (#809): the initializer reads the guard; master "Qty=[0,10] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(0, 10) on property 'Qty'."
    [Arguments("create-in-require", "Qty=[0,10] errors=none clean")] // V-18 D (#809): the initializer reads the guard; master "Qty=[0,10] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(0, 10) on property 'Qty'."
    [Arguments("create-require-not-0-10", "Qty=[11,100] errors=DMEFF008 DMEFF008! Assigned expression value range [11, 100] is entirely outside constraint range(0, 10) on property 'Qty'.")] // V-18 C+D: require not narrows and the create reads it (every guard-passing Qty is >= 11); master "Qty=[0,100] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(0, 10) on property 'Qty'."
    [Arguments("create-require-not-11-100", "Qty=[11,100] errors=none clean")] // V-18 C+D: require not narrows and the create reads it; master "Qty=[0,100] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(11, 100) on property 'Qty'."
    [Arguments("create-require-not-and", "A=[0,10] errors=none DMEFF008? Assigned expression value range [0, 10] can fall outside constraint range(0, 5) on property 'Qty'.")]
    [Arguments("stage-p-action-not-p", "Qty=[11,100] errors=none DMEFF008? Assigned expression value range [11, 100] can fall outside constraint range(0, 60) on property 'Seen'.")] // V-18 C: the stage gate the action negates is suppressed; master "Qty=[0,10] errors=none clean" was WRONG (Qty=70 passes both guards and fails the assign)
    [Arguments("create-inside-loop", "Qty=[0,10] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(0, 10) on property 'Qty'.")]
    [Arguments("create-after-loop", "Qty=[0,10] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(5, 15) on property 'Qty'.")]
    [Arguments("create-after-transition", "Qty=[0,10] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(0, 10) on property 'Qty'.")]
    [Arguments("create-after-cross-invoke", "Qty=[0,10] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(0, 10) on property 'Qty'.")]
    [Arguments("create-after-create-in-subscription", "Qty=[0,10] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(0, 10) on property 'Qty'.")] // added after the characterisation commit: master judges every initializer on the declared path, so this is master's verdict too
    [Arguments("untyped-initializer", "Qty=[0,10] errors=none DMCS003? Cannot verify 'create Item { Qty: parts Qty }' against constraint range(0, 10) on property 'Qty': the assigned value has no bound analysis can use. | DMCS003? Cannot verify 'assign Local to parts Qty' against constraint range(0, 10) on property 'Local': the assigned value has no bound analysis can use.")]
    [Arguments("self-invoke-require-not", "Qty=[11,100] errors=none clean")] // V-18 C: the callee's require not narrows its entry; master "Qty=[0,100] errors=none DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(11, 100) on property 'Local'. | DMEFF008? Call-chain postcondition (Go → Keep): assigned value range [0, 100] can fall outside constraint range(11, 100) on property 'Local'."
    public async Task Shape_Verdict_IsPinned(string shape, string expected) {
        var (dsl, entity, action) = Shapes[shape];
        var (domain, analysis) = Analyze(dsl);
        var probe = shape == "create-require-not-and" ? "A" : "Qty";
        var verdict = $"{probe}={Narrowed(Context(domain, analysis, entity, action), probe)} " +
                      $"errors={ErrorCodes(analysis)} {RangeFindings(analysis)}";
        await Assert.That(verdict).IsEqualTo(expected);
    }

    /// <summary>The runtime half of the wrong-today row: the stage gate is suppressed, so <c>Qty</c> = 70 reaches
    /// the assign, which fails <c>Seen</c>'s range; 50 succeeds. A write that relies on the stage's <c>Small</c> is
    /// not safe.</summary>
    [Test]
    public async Task StagePAndActionNotP_Simulator_AssignFailsAboveSeenRange() {
        var exec = Simulator(StageSmallActionNotSmall("range(0, 60)"), out _);
        var fifty = Make(exec, "Order", ("Qty", 50L));
        var seventy = Make(exec, "Order", ("Qty", 70L));
        await Assert.That(Outcome(exec.Invoke(fifty, "Peek"))).IsEqualTo("ok");
        await Assert.That(exec.Invoke(seventy, "Peek").Succeeded).IsFalse();
    }

    /// <summary>The flagged loop, transition and write-back creates can fail at runtime with the guard held at
    /// entry: each runs with boundary input (<c>Qty</c> at the guard's edge, or the loop's zero-record case).</summary>
    [Test]
    public async Task FlaggedCreates_Simulator_FailWithGuardHeld() {
        var loop = Simulator(CreateInsideLoop, out _);
        var order = Make(loop, "Order", ("Qty", 10L));
        foreach (var _ in Enumerable.Range(0, 2))
            await Assert.That(loop.Link("Lines", order, Make(loop, "Line")).Succeeded).IsTrue();
        var oneLine = Make(loop, "Order", ("Qty", 10L));
        await Assert.That(loop.Link("Lines", oneLine, Make(loop, "Line")).Succeeded).IsTrue();
        await Assert.That(Outcome(loop.Invoke(oneLine, "Go"))).IsEqualTo("ok");
        await Assert.That(loop.Invoke(order, "Go").Succeeded).IsFalse();

        var afterLoop = Simulator(CreateAfterLoop, out _);
        var none = Make(afterLoop, "Order", ("Qty", 0L));
        var five = Make(afterLoop, "Order", ("Qty", 5L));
        await Assert.That(Outcome(afterLoop.Invoke(five, "Go"))).IsEqualTo("ok");
        await Assert.That(afterLoop.Invoke(none, "Go").Succeeded).IsFalse();

        var transition = Simulator(CreateAfterTransition, out _);
        await Assert.That(transition.Invoke(Make(transition, "Order", ("Qty", 5L)), "Go").Succeeded).IsFalse();

        var subscribed = Simulator(CreateAfterCreateInSubscription, out _);
        var notified = subscribed.Invoke(Make(subscribed, "Order", ("Qty", 5L)), "Go");
        await Assert.That(notified.Succeeded).IsFalse();
        await Assert.That(notified.Error ?? "").Contains("Qty");

        var cross = Simulator(CreateAfterCrossEntityInvoke, out _);
        var caller = Make(cross, "Order", ("Qty", 5L));
        var line = Make(cross, "Line");
        await Assert.That(cross.Link("line", caller, line).Succeeded).IsTrue();
        await Assert.That(cross.Link("order", line, caller).Succeeded).IsTrue();
        var outcome = cross.Invoke(caller, "Go");
        await Assert.That(outcome.Succeeded).IsFalse();
        await Assert.That(outcome.Error ?? "").Contains("Qty");
    }

    [Test]
    public async Task RequiredPolicy_RefinesEnv_SameAsInlineToday() {
        var (domain, analysis) = Analyze(AssignUnderRequire);
        var context = Context(domain, analysis, "Order", "Copy");
        await Assert.That(Narrowed(context, "Qty")).IsEqualTo("[0,10]");
        await Assert.That(Range(context.Postconditions.Single().ValueRange)).IsEqualTo("[0,10]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo("clean");
    }

    [Test]
    public async Task NotPolicy_SkippedToday_PinsCurrentVerdict() {
        var (domain, analysis) = Analyze(CreateUnderRequireNot("range(0, 10)"));
        // V-18 C+D: `require not Small` narrows Qty and the create reads it; master skipped the guard ("[0,100]") and
        // the create was the PossibleOutside0To10 warning.
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Make"), "Qty")).IsEqualTo("[11,100]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(
            "DMEFF008! Assigned expression value range [11, 100] is entirely outside constraint range(0, 10) on property 'Qty'.");
    }

    [Test]
    public async Task StagePolicyPlusActionNot_RunsOnSimulator_StageGateSuppressed() {
        var exec = Simulator(StageSmallActionNotSmall("range(0, 100)"), out _);
        var large = Make(exec, "Order", ("Qty", 50L));
        var small = Make(exec, "Order", ("Qty", 5L));
        await Assert.That(Outcome(exec.Invoke(large, "Peek"))).IsEqualTo("ok");
        await Assert.That(large.Bag["Seen"]).IsEqualTo(50L);
        await Assert.That(Outcome(exec.Invoke(small, "Peek"))).IsEqualTo("fail: 'Peek' blocked by policy 'Small'.");
    }

    [Test]
    public async Task ContractExport_NotPolicyListedOnce() {
        const string dsl = """
            domain billing

            public value Money {
              Amount: Number
            }

            public entity Order {
              Total: Number
              Positive: policy { Total > 0 }
              public action Charge (request: Money)
                require not Positive
              {
              }
            }
            """;
        var applied = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(dsl).Parse());
        var contract = new Poly.DomainModeling.ContractFill.InternalDomainProducer().Produce(applied.Root);
        var names = contract.Endpoints.Single().Preconditions.Select(p => p.Name).ToList();
        await Assert.That(string.Join(",", names)).IsEqualTo("not_Positive");
    }

    [Test]
    public async Task Item23c_CreateInitializer_FlaggedToday() {
        var (_, control) = Analyze(AssignUnderRequire);
        var (_, create) = Analyze(CreateUnderRequire);
        var (_, createIn) = Analyze(CreateInUnderRequire);
        // V-18 D (#809): the create and create-in initializers read the narrowing the assignment reads; master
        // narrowed only the assignment ("create=" and "createIn=" were the PossibleOutside0To10 warning).
        await Assert.That($"control={RangeFindings(control)} create={RangeFindings(create)} createIn={RangeFindings(createIn)}")
            .IsEqualTo("control=clean create=clean createIn=clean");
    }

    // ── Census (plan §4, lock amendment 8) ──────────────────────

    private static readonly string[] CensusCodes = [
        DomainModelDiagnosticCodes.EffectConstraintViolation,
        DomainModelDiagnosticCodes.ConstraintUndecided,
        DomainModelDiagnosticCodes.ConstraintSatisfiability,
    ];

    private static IEnumerable<(string Label, string Text, Poly.Compile.CompileResult Result)> CensusDocuments() {
        var root = Poly.Tests.Docs.GuideBlocks.RepoRoot();
        foreach (var file in CorpusVerdictSweepTests.PolyFiles()) {
            var text = File.ReadAllText(Path.Combine(root, file));
            yield return (file, text, Poly.Tests.Docs.GuideBlocks.Analyze(text));
        }
        var guide = Poly.Tests.Docs.GuideBlocks.Load();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var block in guide.Blocks) {
            var occurrence = seen[block.FirstLine] = seen.GetValueOrDefault(block.FirstLine) + 1;
            if (block.Marker is { Kind: Poly.Tests.Docs.GuideMarkerKind.Skip, Code: null }) continue;
            var label = occurrence == 1 ? $"guide fence {block.FirstLine}" : $"guide fence {block.FirstLine} ({occurrence})";
            var composed = Poly.Tests.Docs.GuideBlocks.Compose(block, guide.Wrappers);
            yield return (label, composed.Text, composed.Result);
        }
        foreach (var (name, text) in CorpusVerdictSweepTests.DemoSamples())
            yield return ($"demo sample {name}", text, Poly.Tests.Docs.GuideBlocks.Analyze(text));
        var quickstart = CorpusVerdictSweepTests.QuickstartDocument();
        yield return ("poly_guide quickstart", quickstart, Poly.Tests.Docs.GuideBlocks.Analyze(quickstart));
    }

    private static IEnumerable<Effect> AllEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            var nested = effect switch {
                ConditionalEffect c => c.ThenEffects.Concat(c.ElseEffects ?? []),
                CompositeEffect c => c.Effects,
                ForEachEffect f => f.Body,
                _ => [],
            };
            foreach (var inner in AllEffects(nested)) yield return inner;
        }
    }

    private static bool HasCreateUnderRequire(Domain domain) =>
        domain.Types.OfType<Entity>().Any(entity =>
            entity.Actions.Select(a => (Action: a, Stage: (Stage?)null))
                .Concat(entity.Stages.SelectMany(s => s.Actions.Select(a => (Action: a, Stage: (Stage?)s))))
                .Any(o => (o.Action.Policies.Count > 0 || (o.Stage?.Policies.Count ?? 0) > 0)
                    && AllEffects(o.Action.Effects).Any(e => e is CreateEntityInstance or CreateEntityInRelationshipEffect)));

    private static readonly System.Text.RegularExpressions.Regex RequireNot = new(@"\brequire\s+not\b");

    /// <summary>The measured census: its denominators, then one row per range finding (DMEFF008, DMCS003, DMCS001,
    /// errors and warnings) over the corpus the verdict sweep reads.</summary>
    internal static string CensusRows() {
        var rows = new List<string>();
        int scanned = 0, requireNot = 0, createUnderRequire = 0, noDomain = 0;
        foreach (var (label, text, result) in CensusDocuments()) {
            scanned++;
            if (RequireNot.IsMatch(text)) requireNot++;
            if (result.Domain is not { } domain) noDomain++;
            else if (HasCreateUnderRequire(domain)) createUnderRequire++;
            foreach (var d in result.Diagnostics.Where(d => (d.IsError || d.IsWarning) && CensusCodes.Contains(d.Code)))
                rows.Add($"{label} | {d.Line}:{d.Column} | {d.Code}{(d.IsError ? "!" : "?")} | {d.Message}");
        }
        return string.Join('\n', [
            $"documents scanned = {scanned}",
            $"documents with require not (source text) = {requireNot}",
            $"documents with a create under require (analysed domain) = {createUnderRequire}",
            $"documents with no analysed domain = {noDomain}",
            $"range findings = {rows.Count}",
            .. rows]);
    }

    private const string MeasuredStart = "# measured";
    private const string MeasuredEnd = "# end measured";

    private static string CensusPath() =>
        Path.Combine(Poly.Tests.Docs.GuideBlocks.RepoRoot(), "Poly.Tests", "DomainModeling", "Analysis", "PolicyGuardCensus.txt");

    /// <summary>The census golden's measured block equals the corpus as analysed now. <c>POLY_UPDATE_GOLDEN=1</c>
    /// rewrites the measured block and keeps the hand-written change table below it.</summary>
    [Test]
    public async Task Census_MatchesGolden() {
        var lines = File.ReadAllText(CensusPath()).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var start = lines.IndexOf(MeasuredStart);
        var end = lines.IndexOf(MeasuredEnd);
        if (start < 0 || end < start) throw new InvalidOperationException("The census golden has no measured block.");
        var measured = CensusRows();
        if (Environment.GetEnvironmentVariable("POLY_UPDATE_GOLDEN") == "1") {
            var rewritten = lines.Take(start + 1).Concat(measured.Split('\n')).Concat(lines.Skip(end));
            await File.WriteAllTextAsync(CensusPath(), string.Join('\n', rewritten));
            return;
        }
        await Assert.That(measured).IsEqualTo(string.Join('\n', lines.Skip(start + 1).Take(end - start - 1)));
    }

    // ── B. The seam ─────────────────────────────────────────────

    private static readonly EffectInvariantAnalyzer.GuardAxes QtyIntegral = new(
        new HashSet<string>(["Qty"], StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    private static Dictionary<string, AbstractValue> QtyEnv() =>
        new(StringComparer.Ordinal) { ["Qty"] = AbstractValue.From([new RangeConstraint(0L, 100L)]) };

    /// <summary><c>Small(Max)</c> is <c>Qty &lt;= Max</c>; F-09's substitution (here a delegate) binds
    /// <c>Max</c> to 10. A no-op refinement leaves <c>[0,100]</c>.</summary>
    private static readonly Policy ParameterisedSmall = new(
        "Small", DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Property("Max")));

    private static Entity OrderEntity() => EntityNamed(Analyze(AssignUnderRequire).Domain, "Order");

    [Test]
    public async Task RequireWithArgs_Instantiated_RefinesEnv() {
        var env = QtyEnv();
        PolicyGuardFacts.Refine(env, new GuardedPolicy(ParameterisedSmall, Negated: false), OrderEntity(),
            [DomainExpression.Literal(10L)],
            (policy, args) => DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), args[0]),
            QtyIntegral);
        await Assert.That(Range(env["Qty"].NumericRange)).IsEqualTo("[0,10]");
    }

    [Test]
    public async Task RefineWithArgsButNoInstantiator_Throws() {
        Exception? thrown = null;
        try {
            PolicyGuardFacts.Refine(
                QtyEnv(), new GuardedPolicy(ParameterisedSmall, Negated: false), OrderEntity(),
                [DomainExpression.Literal(10L)], instantiate: null, QtyIntegral);
        }
        catch (Exception e) {
            thrown = e;
        }
        await Assert.That(thrown?.GetType()).IsEqualTo(typeof(InvalidOperationException));
        await Assert.That(thrown!.Message).IsEqualTo(
            "Policy 'Small' on entity 'Order' is required with 1 argument(s), but no policy instantiator was " +
            "supplied to substitute them, so analysis cannot narrow by it.");
    }

    [Test]
    public async Task Guards_ActionThenStage_StagePolicyTheActionNegatesIsSuppressed() {
        var (domain, _) = Analyze(StageSmallActionNotSmall("range(0, 100)"));
        var order = EntityNamed(domain, "Order");
        var open = order.Stages.Single();
        var guards = PolicyGuardFacts.Guards(ActionNamed(order, "Peek"), open);
        await Assert.That(string.Join(",", guards.Select(g => (g.Negated ? "!" : "") + g.Policy.Name)))
            .IsEqualTo("!not_Small");
    }

    // ── C. require not P ────────────────────────────────────────

    /// <summary>The soundness pin: <c>require not Small</c> narrows by <c>Small</c> negated once, to
    /// <c>[11,100]</c>, so a create into <c>range(0, 10)</c> is entirely outside. Narrowing by <c>Small</c> itself,
    /// or by <c>not (not Small)</c>, gives <c>[0,10]</c> and accepts a create the runtime always rejects
    /// (master: skipped, <c>[0,100]</c> and a warning).</summary>
    [Test]
    public async Task NotPolicy_NarrowsByNegatedPolicy() {
        var (domain, analysis) = Analyze(CreateUnderRequireNot("range(0, 10)"));
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Make"), "Qty")).IsEqualTo("[11,100]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(
            "DMEFF008! Assigned expression value range [11, 100] is entirely outside constraint range(0, 10) on property 'Qty'.");
    }

    /// <summary>The accept direction (amendment 6): wrong polarity would give <c>[0,10]</c> and an error. The
    /// simulator runs the guard's boundary: 10 is blocked, 11 and 100 create.</summary>
    [Test]
    public async Task NotPolicy_AcceptDirection_IsClean() {
        var dsl = CreateUnderRequireNot("range(11, 100)");
        var (domain, analysis) = Analyze(dsl);
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Make"), "Qty")).IsEqualTo("[11,100]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo("clean");

        // The simulator twin returns the created Item, so the executor tracks it (a bare create is linked nowhere).
        var exec = Simulator(dsl.Replace("Make: action require", "Make: action -> Item require", StringComparison.Ordinal), out _);
        await Assert.That(Outcome(exec.Invoke(Make(exec, "Order", ("Qty", 10L)), "Make")))
            .IsEqualTo("fail: 'Make' blocked by policy 'Small'.");
        foreach (var qty in new[] { 11L, 100L }) {
            await Assert.That(Outcome(exec.Invoke(Make(exec, "Order", ("Qty", qty)), "Make"))).IsEqualTo("ok");
            await Assert.That(Items(exec).Last().Bag["Qty"]).IsEqualTo(qty);
        }
    }

    /// <summary><c>not (A &gt; 5 and B &gt; 5)</c> is the hull of <c>A &lt;= 5</c> and <c>B &lt;= 5</c>: A is not
    /// narrowed, so a create of A into <c>range(0, 5)</c> stays a warning. A treated as <c>A &lt;= 5</c> would accept
    /// it; A treated as <c>A &gt; 5</c> would reject it. Green on master too (master skips the guard).</summary>
    [Test]
    public async Task NotAnd_NarrowsToHullOfNegatedArms() {
        var (domain, analysis) = Analyze(CreateUnderRequireNotAnd);
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Make"), "A")).IsEqualTo("[0,10]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(
            "DMEFF008? Assigned expression value range [0, 10] can fall outside constraint range(0, 5) on property 'Qty'.");

        var exec = Simulator(CreateUnderRequireNotAnd, out _);
        await Assert.That(exec.Invoke(Make(exec, "Order", ("A", 9L), ("B", 1L)), "Make").Succeeded).IsFalse();
        await Assert.That(Outcome(exec.Invoke(Make(exec, "Order", ("A", 1L), ("B", 9L)), "Make"))).IsEqualTo("ok");
        await Assert.That(Outcome(exec.Invoke(Make(exec, "Order", ("A", 9L), ("B", 9L)), "Make")))
            .IsEqualTo("fail: 'Make' blocked by policy 'Big'.");
    }

    /// <summary>An API-built <c>not_Small</c> whose expression is NOT negated narrows exactly as the resolver's:
    /// the seam reads the entity's <c>Small</c>, as the runtime does, and ignores the marker's expression.</summary>
    [Test]
    public async Task ApiBuiltNotPolicy_UnNegatedExpression_StillNarrowsByEntityPolicy() {
        const string bare = """
            domain C
            Item: entity { Qty: Number range(11, 100) }
            Order: entity {
              Qty: Number range(0, 100)
              Small: policy { Qty <= 10 }
              Make: action { create Item { Qty: Qty } }
            }
            """;
        var unNegated = new Policy("not_Small",
            DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Literal(10L)));
        var (apiDomain, apiAnalysis) = Analyze(bare, new AddPolicyToActionChange("Order", "Make", unNegated));
        var (dslDomain, dslAnalysis) = Analyze(CreateUnderRequireNot("range(11, 100)"));
        await Assert.That(Narrowed(Context(apiDomain, apiAnalysis, "Order", "Make"), "Qty")).IsEqualTo("[11,100]");
        await Assert.That(Narrowed(Context(dslDomain, dslAnalysis, "Order", "Make"), "Qty")).IsEqualTo("[11,100]");
    }

    /// <summary>Amendment 7, red first: the action's <c>require not Small</c> suppresses the stage's <c>Small</c>,
    /// so a write that relies on the stage's <c>Small</c> is not accepted (master: refined by the suppressed gate,
    /// <c>[0,10]</c>, clean; <c>StagePAndActionNotP_Simulator_AssignFailsAboveSeenRange</c> runs 70).</summary>
    [Test]
    public async Task StagePAndActionNotP_WriteRelyingOnStageP_IsNotAccepted() {
        var (domain, analysis) = Analyze(StageSmallActionNotSmall("range(0, 60)"));
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Peek"), "Qty")).IsEqualTo("[11,100]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(
            "DMEFF008? Assigned expression value range [11, 100] can fall outside constraint range(0, 60) on property 'Seen'.");
    }

    /// <summary>The policy is not applied both ways: no false "unsatisfiable preconditions" (DMCS001).</summary>
    [Test]
    public async Task StagePAndActionNotP_NoFalseUnsatisfiable() {
        var (domain, analysis) = Analyze(StageSmallActionNotSmall("range(0, 100)"));
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Peek"), "Qty")).IsEqualTo("[11,100]");
        await Assert.That(ErrorCodes(analysis)).IsEqualTo("none");
        await Assert.That(RangeFindings(analysis)).IsEqualTo("clean");
    }

    /// <summary>Amendment 5: a <c>not_Ghost</c> whose <c>Ghost</c> is not an entity policy narrows nothing and does
    /// not throw. The companion action in the same domain shows the seam does narrow a resolvable <c>not_</c>.</summary>
    [Test]
    public async Task NotPolicy_MissingEntityPolicy_NarrowsNothing() {
        const string dsl = """
            domain C
            Order: entity {
              Qty: Number range(0, 100)
              Small: policy { Qty <= 10 }
              Ghostly: action { }
              Make: action require not Small { }
            }
            """;
        var ghost = new Policy("not_Ghost",
            DomainExpression.Not(DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Literal(10L))));
        var (domain, analysis) = Analyze(dsl, new AddPolicyToActionChange("Order", "Ghostly", ghost));
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Ghostly"), "Qty")).IsEqualTo("[0,100]");
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Make"), "Qty")).IsEqualTo("[11,100]");
    }

    /// <summary>A self-invoked callee's <c>require not Small</c> narrows its entry environment, in its own context
    /// and in the caller's call chain (master: both <c>[0,100]</c> warnings). The simulator shows the invoke runs the
    /// callee's guard: 10 is blocked, 11 writes.</summary>
    [Test]
    public async Task SelfInvoke_CalleeRequireNot_NarrowsCalleeEnv() {
        var (domain, analysis) = Analyze(SelfInvokeRequireNot);
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Keep"), "Qty")).IsEqualTo("[11,100]");
        await Assert.That(Range(Context(domain, analysis, "Order", "Go").Postconditions.Single().ValueRange)).IsEqualTo("[11,100]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo("clean");

        var exec = Simulator(SelfInvokeRequireNot, out _);
        await Assert.That(exec.Invoke(Make(exec, "Order", ("Qty", 10L)), "Go").Succeeded).IsFalse();
        var eleven = Make(exec, "Order", ("Qty", 11L));
        await Assert.That(Outcome(exec.Invoke(eleven, "Go"))).IsEqualTo("ok");
        await Assert.That(eleven.Bag["Local"]).IsEqualTo(11L);
    }

    // ── D. Create and create-in initializers (#809) ─────────────

    private static string InitializerOf(ActionStageInvariant context, string property) {
        var values = context.InitializerValues.Where(v => v.Initializer.PropertyName == property).ToList();
        return values.Count == 0 ? "absent" : string.Join(",", values.Select(v => Range(v.ValueRange)));
    }

    /// <summary>#809's probe (EvidenceBacklogProbes b0484719), ported: the create initializer reads the
    /// <c>require</c>-narrowed <c>Qty</c>, as the assignment control does (master: a <c>[0, 100]</c> warning).</summary>
    [Test]
    public async Task Item23c_CreateInitializerNarrowedByRequire_IsNotFlagged() {
        var (_, control) = Analyze(AssignUnderRequire);
        var (domain, create) = Analyze(CreateUnderRequire);
        await Assert.That($"control={RangeFindings(control)} create={RangeFindings(create)}")
            .IsEqualTo("control=clean create=clean");
        await Assert.That(InitializerOf(Context(domain, create, "Order", "Make"), "Qty")).IsEqualTo("[0,10]");

        // The disappearance is sound at the guard's boundary: 10 creates, 11 is blocked before the create.
        var exec = Simulator(CreateUnderRequire.Replace("Make: action require", "Make: action -> Item require", StringComparison.Ordinal), out _);
        await Assert.That(Outcome(exec.Invoke(Make(exec, "Order", ("Qty", 10L)), "Make"))).IsEqualTo("ok");
        await Assert.That(Items(exec).Single().Bag["Qty"]).IsEqualTo(10L);
        await Assert.That(Outcome(exec.Invoke(Make(exec, "Order", ("Qty", 11L)), "Make")))
            .IsEqualTo("fail: 'Make' blocked by policy 'Small'.");
    }

    [Test]
    public async Task CreateIn_NarrowedByRequire_IsNotFlagged() {
        var (domain, analysis) = Analyze(CreateInUnderRequire);
        await Assert.That(RangeFindings(analysis)).IsEqualTo("clean");
        await Assert.That(InitializerOf(Context(domain, analysis, "Order", "Make"), "Qty")).IsEqualTo("[0,10]");

        var exec = Simulator(CreateInUnderRequire, out _);
        var made = exec.Invoke(Make(exec, "Order", ("Qty", 10L)), "Make");
        await Assert.That(Outcome(made)).IsEqualTo("ok");
        await Assert.That(made.CreatedChildren.Single().Bag["Qty"]).IsEqualTo(10L);
        await Assert.That(Outcome(exec.Invoke(Make(exec, "Order", ("Qty", 11L)), "Make")))
            .IsEqualTo("fail: 'Make' blocked by policy 'Small'.");
    }

    /// <summary>The guard fact does not outlive an assign: after <c>assign Qty to 50</c> the create reads 50, so a
    /// create into <c>range(0, 10)</c> is entirely outside (a guard fact kept past the assign would accept it).
    /// The simulator: the guard holds at 5 and the create still fails.</summary>
    [Test]
    public async Task AssignBeforeCreate_FlowEnvWins() {
        const string dsl = """
            domain C
            Item: entity { Qty: Number range(0, 10) }
            Order: entity {
              Qty: Number range(0, 100)
              Small: policy { Qty <= 10 }
              Make: action require Small { assign Qty to 50  create Item { Qty: Qty } }
            }
            """;
        var (domain, analysis) = Analyze(dsl);
        await Assert.That(InitializerOf(Context(domain, analysis, "Order", "Make"), "Qty")).IsEqualTo("[50,50]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(
            "DMEFF008! Assigned expression value range [50, 50] is entirely outside constraint range(0, 10) on property 'Qty'.");
        // The simulator twin widens Item's range (so the create can run) and returns the created Item.
        var twin = dsl.Replace("Item: entity { Qty: Number range(0, 10) }", "Item: entity { Qty: Number range(0, 100) }",
            StringComparison.Ordinal).Replace("Make: action require", "Make: action -> Item require", StringComparison.Ordinal);
        var exec = Simulator(twin, out _);
        await Assert.That(Outcome(exec.Invoke(Make(exec, "Order", ("Qty", 5L)), "Make"))).IsEqualTo("ok");
        await Assert.That(Items(exec).Single().Bag["Qty"]).IsEqualTo(50L);
    }

    /// <summary>Amendment 2: a create in a loop body keeps the declared-range path (the body is walked once on the
    /// pre-loop environment); <c>FlaggedCreates_Simulator_FailWithGuardHeld</c> runs the second iteration.</summary>
    [Test]
    public async Task CreateInsideLoop_AssignedInBody_NotNarrowed() {
        var (domain, analysis) = Analyze(CreateInsideLoop);
        await Assert.That(InitializerOf(Context(domain, analysis, "Order", "Go"), "Qty")).IsEqualTo("absent");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(PossibleOutside0To10);
    }

    /// <summary>A create after a loop keeps the declared path: the loop's environment is its first iteration's
    /// (#863), and with zero records the create reads the entry value 0.</summary>
    [Test]
    public async Task CreateAfterLoop_NotNarrowed() {
        var (domain, analysis) = Analyze(CreateAfterLoop);
        await Assert.That(InitializerOf(Context(domain, analysis, "Order", "Go"), "Qty")).IsEqualTo("absent");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(
            "DMEFF008? Assigned expression value range [0, 100] can fall outside constraint range(5, 15) on property 'Qty'.");
    }

    /// <summary>Amendment 3: the transition runs the target stage's entry block, which the flow does not model.</summary>
    [Test]
    public async Task CreateAfterTransition_EntryAssign() {
        var (domain, analysis) = Analyze(CreateAfterTransition);
        await Assert.That(InitializerOf(Context(domain, analysis, "Order", "Go"), "Qty")).IsEqualTo("absent");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(PossibleOutside0To10);
    }

    /// <summary>Amendment 3: the cross-entity callee invokes back into the caller, which the flow does not model.</summary>
    [Test]
    public async Task CreateAfterCrossEntityInvoke_WriteBack() {
        var (domain, analysis) = Analyze(CreateAfterCrossEntityInvoke);
        await Assert.That(InitializerOf(Context(domain, analysis, "Order", "Go"), "Qty")).IsEqualTo("absent");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(PossibleOutside0To10);
    }

    /// <summary>A create after a create-in keeps the declared path: the create-in can notify a subscription on the
    /// creator, which the walk does not model.</summary>
    [Test]
    public async Task CreateAfterCreateIn_Subscription_NotNarrowed() {
        var (domain, analysis) = Analyze(CreateAfterCreateInSubscription);
        await Assert.That(InitializerOf(Context(domain, analysis, "Order", "Go"), "Qty")).IsEqualTo("absent");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(PossibleOutside0To10);
    }

    /// <summary>No guard: the create stays a warning, and its initializer is recorded at the declared range (the
    /// presence control that shows the arm ran).</summary>
    [Test]
    public async Task CreateOutsideGuard_StillFlagged() {
        var dsl = CreateUnderRequire.Replace("action require Small {", "action {", StringComparison.Ordinal);
        var (domain, analysis) = Analyze(dsl);
        await Assert.That(InitializerOf(Context(domain, analysis, "Order", "Make"), "Qty")).IsEqualTo("[0,100]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo(PossibleOutside0To10);
    }

    /// <summary>One record per initializer per stage context: an entity-level action has one context per stage, and
    /// each arm of an <c>if</c> records its own create once, under the arm's condition.</summary>
    [Test]
    public async Task InitializerFacts_RecordedOncePerStageContext() {
        var (domain, analysis) = Analyze("""
            domain C
            Item: entity { Qty: Number range(0, 10) }
            Order: entity {
              Qty: Number range(0, 100)
              Small: policy { Qty <= 10 }
              Make: action require Small {
                if (Qty <= 5) { create Item { Qty: Qty } } else { create Item { Qty: Qty } }
              }
              A: stage { }
              B: stage { }
            }
            """);
        var make = ActionNamed(EntityNamed(domain, "Order"), "Make");
        var contexts = analysis.GetActionInvariants(make)!.StageContexts();
        await Assert.That(string.Join(" ", contexts.Select(c => $"{c.StageName}:{InitializerOf(c, "Qty")}")))
            .IsEqualTo("A:[0,5],[6,10] B:[0,5],[6,10]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo("clean");
    }

    /// <summary>T-05b's ordering note (#680 V-18 also-line): this analyzer runs before <c>UntypedExpressionPass</c>
    /// and fails closed on an expression it cannot type. <c>parts Qty</c> is a many hop (untyped on master; DMSEM006
    /// once #868 merges): its value carries no range, so neither write is verified nor flagged as out of range, under
    /// a guard that narrows <c>Qty</c>. The recorded initializer is the presence control.</summary>
    [Test]
    public async Task UntypedInitializer_FailsClosed() {
        var (domain, analysis) = Analyze(UntypedInitializer);
        var make = Context(domain, analysis, "Order", "Make");
        await Assert.That(InitializerOf(make, "Qty")).IsEqualTo("none");
        await Assert.That(Range(Context(domain, analysis, "Order", "Copy").Postconditions.Single().ValueRange)).IsEqualTo("none");
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain)!;
        await Assert.That(facts.Verified).IsEqualTo(0);
        await Assert.That(string.Join(",", facts.Unverified.Select(u => u.TargetProperty).Order(StringComparer.Ordinal)))
            .IsEqualTo("Local,Qty");
    }

    [Test]
    public async Task StageRequireNot_NarrowsAsToday() {
        var (domain, analysis) = Analyze("""
            domain S
            Order: entity {
              Qty: Number range(0, 100)
              Seen: Number range(11, 100) optional
              Small: policy { Qty <= 10 }
              Open: stage require not Small {
                Peek: action { assign Seen to Qty }
              }
            }
            """);
        await Assert.That(Narrowed(Context(domain, analysis, "Order", "Peek"), "Qty")).IsEqualTo("[11,100]");
        await Assert.That(RangeFindings(analysis)).IsEqualTo("clean");
    }
}