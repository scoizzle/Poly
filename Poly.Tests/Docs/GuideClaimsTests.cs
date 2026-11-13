using Poly.DomainModeling.Ontology;

namespace Poly.Tests.Docs;

/// <summary>
/// The guide's behavioural claims, probed against the code. Each probe runs the reproduction from an
/// open issue through the same <see cref="PolyCompiler"/> path <c>poly_define</c> uses, then through
/// <see cref="ModuleExec"/>, and answers in a small vocabulary so both directions of the ratchet are
/// reachable: <see cref="ClaimProbe.Claimed"/> must be one of the values the probe can return, and the
/// measured detail is pinned by the ledger row.
/// <para>
/// <see cref="EveryClaimAgreesWithTheCode_ExceptTheLedger"/> is the ratchet: the set of probes whose
/// claim is still in the guide and whose verdict disagrees with it must equal
/// <see cref="KnownGuideDrift"/> exactly, so a row cannot be dropped while its contradiction stands,
/// and a guide-only change retires the probe by taking the claim text out.
/// </para>
/// </summary>
public class GuideClaimsTests {
    /// <summary>A probe's answer: the vocabulary verdict the ratchet compares, and the measured detail.</summary>
    private sealed record ProbeAnswer(string Verdict, string Detail);

    private const string NotifyDomain = """
        domain Notif
        uses storage
        Shipment: entity {
          Code: Text
          order: Order via shipments
          Packed: stage { Ship: action { transition to Delivered } }
          Delivered: stage { }
        }
        Order: entity {
          Ref: Text
          Trace: Number default(0)
          shipments: many Shipment
          Open: stage {
            AddShipment: action (code: Text) { create in shipments { Code: code } }
            when shipments Delivered { assign Trace to Trace * 10 + 2 }
          }
          when shipments Delivered { assign Trace to Trace * 10 + 1 }
        }
        """;

    private const string AnyOnceDomain = """
        domain AnyOnce
        uses storage
        Shipment: entity {
          Code: Text
          order: Order via shipments
          Packed: stage { Ship: action { transition to Delivered } }
          Delivered: stage { }
        }
        Order: entity {
          Ref: Text
          AnyN: Number default(0)
          EachN: Number default(0)
          shipments: many Shipment
          AddShipment: action (code: Text) { create in shipments { Code: code } }
          when any shipments Delivered { assign AnyN to AnyN + 1 }
          when shipments Delivered { assign EachN to EachN + 1 }
        }
        """;

    /// <summary>#577's reproduction: an action inside the stage can falsify the stage's <c>require</c>.</summary>
    private const string StageRequireDomain = """
        domain StageRequire
        Case: entity {
          Cleared: Boolean default(false)
          NotCleared: policy { Cleared is false }
          Open: stage require NotCleared {
            Clear: action { assign Cleared to true }
            Close: action { transition to Closed }
          }
          Closed: stage {}
        }
        """;

    /// <summary>
    /// #576's reproduction: the same entity without the clearing action, so the create path is the only
    /// thing under test. A second slice cannot become a deleter of this row through a shared fixture.
    /// </summary>
    private const string CreateOnlyDomain = """
        domain StageRequireCreate
        Case: entity {
          Cleared: Boolean default(false)
          NotCleared: policy { Cleared is false }
          Open: stage require NotCleared {
            Close: action { transition to Closed }
          }
          Closed: stage {}
        }
        """;

    private const string OwnedDomain = """
        domain Owned
        A: entity {
          N: Number
          kid: owned B
        }
        B: entity {
          M: Number
        }
        """;

    /// <summary>#492's reproduction: <c>action when S</c> where <c>S</c> is a real stage.</summary>
    private const string ActionWhenDomain = """
        domain T

        A: entity {
          S: stage { }
          X: action when S { }
        }
        """;

    private sealed record ClaimProbe(
        string Id,
        string Issue,
        string Claim,
        string[] Vocabulary,
        string Claimed,
        string Deleter,
        Func<ProbeAnswer> Observe);

    /// <summary>
    /// Every claim the guide makes that this slice can observe. <see cref="ClaimProbe.Deleter"/> is the
    /// one slice that can delete the row (A1); there is no second owner.
    /// </summary>
    private static readonly ClaimProbe[] Probes = [
        new("c600", "#600",
            "Store notify runs stage-scoped handlers first, then entity-level.",
            ["entity-first", "stage-first"], "stage-first", "M-02", NotifyOrder),
        new("c601", "#601",
            "Fires **once** when **at least one** linked related entity is in a matching stage",
            ["once", "each-transition"], "once", "M-02", AnyFiresOnce),
        new("c637", "#637",
            "removing the owner instance entirely is not an unlink, so it is exempt from the minimum.",
            ["present", "absent"], "present", "M-02", OwnedRemoval),
        new("c492", "#492",
            "Action `when Stage` | Not a runtime or emit gate",
            ["accepted", "rejected"], "rejected", "G-05",
            () => ActionWhenVerdict(ActionWhenDomain)),
        new("c576", "#576",
            "It is an invariant: every action that can run in the stage must keep it true.",
            ["refused", "accepted"], "refused", "L-17", StageRequireAtCreate),
        new("c577", "#518, #577",
            "It is an invariant: every action that can run in the stage must keep it true.",
            ["refused", "accepted"], "refused", "V-10",
            () => StageRequireVerdict(StageRequireDomain)),
    ];

    /// <summary>One recorded contradiction: the measured detail and the resolution that retires it.</summary>
    private sealed record DriftRow(string Id, string Deleter, string Observed, string Resolution);

    /// <summary>
    /// The rows this slice knows about: the id, the one slice that deletes the row, the measured detail,
    /// and which resolution retires it. Each entry is a recorded contradiction, never a pass.
    /// <para>
    /// c576 and c577 share one claim sentence — the guide's only statement about a stage <c>require</c>
    /// being an invariant — so one M-02 reword retires both rows, and neither can outlive the sentence.
    /// </para>
    /// </summary>
    private static readonly DriftRow[] KnownGuideDrift = [
        new("c600", "M-02", "entity-first: Trace=12",
            "M-02 reword: the guide says stage-scoped first; it must say entity-level first."),
        new("c601", "M-02", "each-transition: AnyN=2, EachN=2",
            "M-02 reword: 'once' becomes 'each matching transition'."),
        new("c637", "M-02", "absent: 3/3 effect verbs are parse errors; last-owner unlink refused",
            "M-02 reword: the removal exemption goes, or a removal operation lands."),
        new("c492", "G-05", "accepted: 0 errors",
            "G-05 refuses `action when S` in analysis, at the action's own line; the section 10 row is then true."),
        new("c576", "L-17", "accepted: created in Open with Cleared=true",
            "L-17 refuses create in a stage whose require the initial values falsify. Shares c577's claim "
            + "sentence, so one M-02 reword retires both rows."),
        new("c577", "V-10", "accepted: 0 errors; the clearing action succeeds; the following action is blocked",
            "V-10 refuses or post-checks an action that falsifies the stage require. Shares c576's claim "
            + "sentence, so one M-02 reword retires both rows."),
    ];

    /// <summary>The audit's answer: contradictions with no row, and rows with no contradiction.</summary>
    private sealed record DriftAudit(IReadOnlyList<string> Unlisted, IReadOnlyList<string> Stale);

    /// <summary>
    /// The R6a rule as a pure function, so it can be exercised without the repository's guide, probes or
    /// ledger. A probe counts only while its claim text is in the guide: a claim the guide no longer makes
    /// cannot contradict it, so it retires rather than blocks.
    /// </summary>
    private static DriftAudit Audit(
        string guideText, IReadOnlyList<ClaimProbe> probes, IReadOnlyList<DriftRow> rows) {
        var guide = GuideBlocks.Normalise(guideText);
        var drifted = probes
            .Where(probe => guide.Contains(GuideBlocks.Normalise(probe.Claim), StringComparison.Ordinal))
            .Where(probe => probe.Observe().Verdict != probe.Claimed)
            .Select(probe => probe.Id)
            .Order(StringComparer.Ordinal)
            .ToList();
        var listed = rows.Select(row => row.Id).Order(StringComparer.Ordinal).ToList();
        return new DriftAudit(drifted.Except(listed).ToList(), listed.Except(drifted).ToList());
    }

    [Test]
    public async Task EveryClaimAgreesWithTheCode_ExceptTheLedger() {
        var audit = Audit(GuideBlocks.ReadGuide(), Probes, KnownGuideDrift);
        var unlisted = audit.Unlisted;
        var stale = audit.Stale;

        var table = string.Join("\n", Probes.Select(probe => {
            var answer = probe.Observe();
            return $"  {probe.Id} {probe.Issue}: guide says {probe.Claimed}, code says "
                   + $"{answer.Verdict} ({answer.Detail})";
        }));

        await Assert.That(unlisted.Count + stale.Count).IsEqualTo(0)
            .Because("a guide claim the code contradicts must be a recorded row, and a row must still "
                     + "contradict:\nunlisted: " + string.Join(", ", unlisted)
                     + "\nrows that no longer drift: " + string.Join(", ", stale)
                     + "\nprobes:\n" + table);
    }

    [Test]
    public async Task KnownDrift_RowsStillDrift() {
        var guide = GuideBlocks.Normalise(GuideBlocks.ReadGuide());
        var problems = new List<string>();

        foreach (var (id, deleter, observed, resolution) in KnownGuideDrift) {
            var probe = Probes.SingleOrDefault(candidate => candidate.Id == id);
            if (probe is null) {
                problems.Add($"{id}: no probe carries this row");
                continue;
            }
            if (!string.Equals(deleter, probe.Deleter, StringComparison.Ordinal))
                problems.Add($"{id}: row deleter '{deleter}' does not match the probe's owner '{probe.Deleter}'");
            if (resolution.Trim().Length == 0)
                problems.Add($"{id}: the row does not say which resolution retires it");
            if (!guide.Contains(GuideBlocks.Normalise(probe.Claim), StringComparison.Ordinal)) {
                problems.Add($"{id}: the claim text is gone from the guide; delete the row or re-point it");
                continue;
            }
            var answer = probe.Observe();
            if (answer.Verdict == probe.Claimed)
                problems.Add($"{id}: the code now agrees with the guide; delete the row ({deleter})");
            else if (!string.Equals(answer.Detail, observed, StringComparison.Ordinal))
                problems.Add($"{id}: the code now answers \"{answer.Detail}\"; the row pins \"{observed}\"");
        }

        var orphanDeleters = KnownGuideDrift
            .Where(row => row.Deleter.Length == 0)
            .Select(row => row.Id)
            .ToList();
        if (orphanDeleters.Count > 0)
            problems.Add("rows without a deleter: " + string.Join(", ", orphanDeleters));

        await Assert.That(problems.Count).IsEqualTo(0)
            .Because("each row is one issue, one claim substring, one measured detail and one deleter:\n"
                     + string.Join("\n", problems));
    }

    /// <summary>
    /// A row is deletable only by the slice that ends the contradiction, and the probe must be able to
    /// say what the guide says: a claimed value outside the probe's vocabulary makes the "code agrees"
    /// branch unreachable, which would leave the row immortal.
    /// </summary>
    [Test]
    public async Task EveryProbe_ClaimsAValueItsObserveCanReturn() {
        var problems = Probes
            .Where(probe => !probe.Vocabulary.Contains(probe.Claimed, StringComparer.Ordinal))
            .Select(probe => $"{probe.Id}: claims '{probe.Claimed}', vocabulary is "
                             + string.Join("/", probe.Vocabulary))
            .ToList();
        var duplicates = Probes.GroupBy(probe => probe.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        problems.AddRange(duplicates.Select(id => $"{id}: two probes share this id"));

        await Assert.That(problems.Count).IsEqualTo(0)
            .Because("a probe whose claimed value it can never return cannot record agreement:\n"
                     + string.Join("\n", problems));
    }

    /// <summary>
    /// The retirement rule, proved on synthetic inputs so no live row or live sentence can make it pass
    /// by accident: present-and-drifted-with-a-row is clean, present-and-drifted-without-a-row is
    /// unlisted, and gone-with-no-row is clean again.
    /// </summary>
    [Test]
    public async Task Claims_WhoseTextIsGoneFromTheGuide_RetireTheirRow() {
        const string claim = "The synthetic claim sentence this test injects.";
        ClaimProbe[] probes = [
            new("s1", "#0", claim, ["agrees", "drifted"], "agrees", "M-02",
                () => new ProbeAnswer("drifted", "drifted: synthetic")),
        ];
        DriftRow[] row = [new("s1", "M-02", "drifted: synthetic", "the synthetic resolution")];
        var guide = "Intro text.\n\n" + claim + "\n\nMore text.\n";

        var listed = Audit(guide, probes, row);
        await Assert.That(listed.Unlisted.Count + listed.Stale.Count).IsEqualTo(0)
            .Because("present, drifted, and listed is the ledger working: unlisted "
                     + string.Join(", ", listed.Unlisted) + "; stale " + string.Join(", ", listed.Stale));

        var unlisted = Audit(guide, probes, []);
        await Assert.That(string.Join(",", unlisted.Unlisted)).IsEqualTo("s1")
            .Because("present and drifted with no row must report the probe as unlisted");

        var retired = Audit(guide.Replace(claim, "", StringComparison.Ordinal), probes, []);
        await Assert.That(retired.Unlisted.Count + retired.Stale.Count).IsEqualTo(0)
            .Because("with the claim gone the probe is inert, so a guide-only change retires the row: "
                     + "unlisted " + string.Join(", ", retired.Unlisted)
                     + "; stale " + string.Join(", ", retired.Stale));
    }

    /// <summary>
    /// #492 with a document the code does reject: the observer must be able to answer what the guide
    /// says, so the row can be deleted when the guide and the code meet.
    /// </summary>
    [Test]
    public async Task C492_RejectedDocument_AgreesWithClaim() {
        var probe = Probes.Single(candidate => candidate.Id == "c492");
        // `when Nope` (an unknown stage) is accepted today, which is #492's complaint; the parser only
        // refuses a malformed clause, so that is the document that can reach the claimed word.
        var rejected = ActionWhenVerdict("""
            domain T

            A: entity {
              S: stage { }
              X: action when { }
            }
            """);

        await Assert.That(rejected.Verdict).IsEqualTo(probe.Claimed)
            .Because("a document the code rejects must be able to answer the guide's word for it, "
                     + $"or the row can never be retired: {rejected.Detail}");
    }

    /// <summary>#577 with a document whose action the code refuses, so the claimed word is reachable.</summary>
    [Test]
    public async Task C577_RefusedDocument_AgreesWithClaim() {
        var probe = Probes.Single(candidate => candidate.Id == "c577");
        var refused = StageRequireVerdict("""
            domain StageRequire
            Case: entity {
              Cleared: Boolean default(false)
              NotCleared: policy { Cleared is false }
              Open: stage require NotCleared {
                Clear: action { assign Cleared to 5 }
              }
            }
            """);

        await Assert.That(refused.Verdict).IsEqualTo(probe.Claimed)
            .Because("an action the code refuses must be able to answer the guide's word for it, "
                     + $"or the row can never be retired: {refused.Detail}");
    }

    // ── probes ─────────────────────────────────────────────────────────────

    /// <summary>#600: which handler runs first — the last write to Trace wins.</summary>
    private static ProbeAnswer NotifyOrder() {
        var domain = Compile(NotifyDomain);
        var exec = ModuleExec.For(domain);
        var order = exec.Create("Order", Values(("Ref", "O1"))).Instance;
        if (order is null)
            return new ProbeAnswer("create-refused", "create Order refused");
        if (!exec.Invoke(order, "AddShipment", Values(("code", "S"))).Succeeded)
            return new ProbeAnswer("add-refused", "AddShipment refused");
        var shipment = exec.LinkedTargets("shipments", order).Single();
        if (!exec.Invoke(shipment, "Ship").Succeeded)
            return new ProbeAnswer("ship-refused", "Ship refused");
        var trace = GuideBlocks.Text(order.Bag["Trace"]);
        var verdict = trace switch {
            "12" => "entity-first",
            "21" => "stage-first",
            _ => "Trace=" + trace,
        };
        return new ProbeAnswer(verdict, verdict + ": Trace=" + trace);
    }

    /// <summary>#601: how many times <c>when any</c> fires across two deliveries.</summary>
    private static ProbeAnswer AnyFiresOnce() {
        var domain = Compile(AnyOnceDomain);
        var exec = ModuleExec.For(domain);
        var order = exec.Create("Order", Values(("Ref", "O1"))).Instance;
        if (order is null)
            return new ProbeAnswer("create-refused", "create Order refused");
        foreach (var code in new[] { "S1", "S2" }) {
            if (!exec.Invoke(order, "AddShipment", Values(("code", code))).Succeeded)
                return new ProbeAnswer("add-refused", "AddShipment refused");
        }
        foreach (var shipment in exec.LinkedTargets("shipments", order)) {
            if (!exec.Invoke(shipment, "Ship").Succeeded)
                return new ProbeAnswer("ship-refused", "Ship refused");
        }
        var any = GuideBlocks.Text(order.Bag["AnyN"]);
        var each = GuideBlocks.Text(order.Bag["EachN"]);
        var verdict = any switch {
            "1" => "once",
            "2" => "each-transition",
            _ => "AnyN=" + any,
        };
        return new ProbeAnswer(verdict, $"{verdict}: AnyN={any}, EachN={each}");
    }

    /// <summary>
    /// #637: the guide says removing an owner is exempt from the owned-target minimum. The probe asks
    /// whether any removal exists at all, and whether the minimum still refuses a last-owner unlink.
    /// </summary>
    private static ProbeAnswer OwnedRemoval() {
        var verbs = new[] { "delete", "remove", "destroy" };
        var refused = verbs.Count(verb => {
            var result = GuideBlocks.Analyze(
                "domain Verbed\n\nA: entity {\n  N: Number\n  Zap: action { " + verb + " this }\n}\n");
            return result.Diagnostics.Any(d => d.IsError && d.Code == "DSL0001");
        });

        var domain = Compile(OwnedDomain);
        var exec = ModuleExec.For(domain);
        var a = exec.Create("A", Values(("N", 1L))).Instance;
        var b = exec.Create("B", Values(("M", 1L))).Instance;
        if (a is null || b is null)
            return new ProbeAnswer("create-refused", "create refused");
        if (!exec.Link("kid", a, b).Succeeded)
            return new ProbeAnswer("link-refused", "link refused");
        var unlink = exec.Unlink("kid", a, b);
        var verdict = refused == verbs.Length ? "absent" : "present";
        return new ProbeAnswer(verdict,
            $"{verdict}: {refused}/3 effect verbs are parse errors; last-owner unlink "
            + (unlink.Succeeded ? "allowed" : "refused"));
    }

    /// <summary>#518/#577: an action inside the stage can falsify the stage's <c>require</c>.</summary>
    private static ProbeAnswer StageRequireVerdict(string document) {
        var result = GuideBlocks.Analyze(document);
        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        if (result.Domain is null)
            return new ProbeAnswer("refused", "refused: analysis " + string.Join(", ", errors.Select(d => d.Code)));
        var exec = ModuleExec.For(result.Domain);
        var created = exec.Create("Case", Values()).Instance;
        if (created is null)
            return new ProbeAnswer("refused", "refused: create refused");
        var cleared = exec.Invoke(created, "Clear");
        var closed = exec.Invoke(created, "Close");
        var verdict = cleared.Succeeded ? "accepted" : "refused";
        return new ProbeAnswer(verdict,
            $"{verdict}: {errors.Count} errors; the clearing action "
            + (cleared.Succeeded ? "succeeds" : "is refused") + "; the following action "
            + (closed.Succeeded ? "succeeds" : "is blocked"));
    }

    /// <summary>#576: <c>create</c> can start an instance in a stage it already violates.</summary>
    private static ProbeAnswer StageRequireAtCreate() {
        var result = GuideBlocks.Analyze(CreateOnlyDomain);
        if (result.Domain is null)
            return new ProbeAnswer("refused", "refused: analysis rejected the document");
        var exec = ModuleExec.For(result.Domain);
        var created = exec.Create("Case", Values(("Cleared", true))).Instance;
        if (created is null)
            return new ProbeAnswer("refused", "refused: create refused");
        var stage = created.Bag.TryGetValue("CurrentStage", out var current)
            ? GuideBlocks.Text(current)
            : "(no stage)";
        return new ProbeAnswer("accepted",
            $"accepted: created in {stage} with Cleared=" + GuideBlocks.Text(created.Bag["Cleared"]));
    }

    /// <summary>
    /// #492: is <c>X: action when S { }</c> refused, or accepted and silently dropped? An error elsewhere
    /// in the document does not make the clause refused, so only an error on the clause's own line counts.
    /// </summary>
    private static ProbeAnswer ActionWhenVerdict(string document) {
        var result = GuideBlocks.Analyze(document);
        var clauseLine = document.Split('\n')
            .Select((text, index) => (text, number: index + 1))
            .FirstOrDefault(pair => pair.text.Contains("action when", StringComparison.Ordinal)).number;
        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        var onTheClause = errors.Where(d => d.Line == clauseLine).ToList();
        if (onTheClause.Count > 0)
            return new ProbeAnswer("rejected",
                "rejected: " + string.Join(", ", onTheClause.Select(d => d.Code)) + $" at line {clauseLine}");
        return errors.Count == 0
            ? new ProbeAnswer("accepted", "accepted: 0 errors")
            : new ProbeAnswer("accepted",
                $"accepted: {errors.Count} error(s) elsewhere, none on the `action when` line");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static Domain Compile(string poly) {
        var result = GuideBlocks.Analyze(poly);
        if (result.Domain is not null)
            return result.Domain;
        throw new InvalidOperationException("The probe fixture did not compile: "
            + string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => $"{d.Code} {d.Message}")));
    }

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
}