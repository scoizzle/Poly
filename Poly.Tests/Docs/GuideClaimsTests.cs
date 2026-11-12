using Poly.DomainModeling.Ontology;

namespace Poly.Tests.Docs;

/// <summary>
/// The guide's behavioural claims, probed against the code. Each probe runs the reproduction from an
/// open issue through the same <see cref="PolyCompiler"/> path <c>poly_define</c> uses, then through
/// <see cref="ModuleExec"/>; <see cref="ClaimProbe.Claimed"/> is what the guide says the answer is.
/// <para>
/// <see cref="EveryClaimAgreesWithTheCode_ExceptTheLedger"/> is the ratchet: the set of probes whose
/// observation contradicts the guide must equal <see cref="KnownGuideDrift"/> exactly, so a row
/// cannot be deleted until its contradiction is fixed on one side or the other.
/// </para>
/// </summary>
public class GuideClaimsTests {
    private const string NotifyDomain = """
        domain Notif
        uses storage
        Shipment: entity {
          Code: Text
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

    private sealed record ClaimProbe(
        string Id,
        string Issue,
        string Claim,
        string Claimed,
        string Deleter,
        Func<string> Observe);

    /// <summary>
    /// Every claim the guide makes that this slice can observe. <see cref="ClaimProbe.Deleter"/> is the
    /// one slice that can delete the row (A1); there is no second owner.
    /// </summary>
    private static readonly ClaimProbe[] Probes = [
        new("c600", "#600",
            "Store notify runs stage-scoped handlers first, then entity-level.",
            "21", "M-02", NotifyOrder),
        new("c601", "#601",
            "Fires **once** when **at least one** linked related entity is in a matching stage",
            "1", "M-02", AnyFiresOnce),
        new("c492", "#492",
            "Not a runtime or emit gate",
            "rejected", "G-05", ActionWhenStage),
        new("c637", "#637",
            "removing the owner instance entirely is not an unlink, so it is exempt from the minimum.",
            "removal exists", "M-02", OwnedRemoval),
        new("c577", "#518, #577",
            "It is an invariant: every action that can run in the stage must keep it true.",
            "Clear refused", "V-10", StageRequireAfterAction),
        new("c576", "#576",
            "It is an invariant: every action that can run in the stage must keep it true.",
            "create refused", "L-17", StageRequireAtCreate),
    ];

    /// <summary>
    /// The rows this slice knows about, id to the slice that deletes the row. Empty means the guide and
    /// the code agree on every probe; each entry is a recorded contradiction, never a pass.
    /// </summary>
    private static readonly (string Id, string Deleter, string Observed)[] KnownGuideDrift = [
        ("c600", "M-02", "12"),
        ("c601", "M-02", "AnyN=2, EachN=2"),
        ("c637", "M-02", "3/3 effect verbs are parse errors; last-owner unlink refused"),
        ("c492", "G-05", "accepted"),
        ("c576", "L-17", "created in Open with Cleared=true"),
        ("c577", "V-10", "0 errors; Clear succeeded; Close blocked"),
    ];

    [Test]
    public async Task EveryClaimAgreesWithTheCode_ExceptTheLedger() {
        var drifted = Probes
            .Select(probe => (probe.Id, Observed: probe.Observe()))
            .Where(pair => pair.Observed != Probes.Single(p => p.Id == pair.Id).Claimed)
            .Select(pair => pair.Id)
            .Order(StringComparer.Ordinal)
            .ToList();
        var listed = KnownGuideDrift.Select(row => row.Id).Order(StringComparer.Ordinal).ToList();

        var unlisted = drifted.Except(listed).ToList();
        var stale = listed.Except(drifted).ToList();
        var observers = Probes.ToDictionary(p => p.Id, p => p.Observe(), StringComparer.Ordinal);
        var table = string.Join("\n", Probes.Select(probe =>
            $"  {probe.Id} {probe.Issue}: guide says {probe.Claimed}, code says {observers[probe.Id]}"));

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

        foreach (var (id, deleter, observed) in KnownGuideDrift) {
            var probe = Probes.SingleOrDefault(candidate => candidate.Id == id);
            if (probe is null) {
                problems.Add($"{id}: no probe carries this row");
                continue;
            }
            if (!string.Equals(deleter, probe.Deleter, StringComparison.Ordinal))
                problems.Add($"{id}: row deleter '{deleter}' does not match the probe's owner '{probe.Deleter}'");
            if (!guide.Contains(GuideBlocks.Normalise(probe.Claim), StringComparison.Ordinal))
                problems.Add($"{id}: the claim text is gone from the guide; delete the row or re-point it");
            if (probe.Observe() == probe.Claimed)
                problems.Add($"{id}: the code now agrees with the guide; delete the row ({deleter})");
            else if (probe.Observe() != observed)
                problems.Add($"{id}: the code now answers \"{probe.Observe()}\"; the row pins \"{observed}\"");
        }

        var orphanDeleters = KnownGuideDrift
            .Where(row => row.Deleter.Length == 0)
            .Select(row => row.Id)
            .ToList();
        if (orphanDeleters.Count > 0)
            problems.Add("rows without a deleter: " + string.Join(", ", orphanDeleters));

        await Assert.That(problems.Count).IsEqualTo(0)
            .Because("each row is one issue, one claim substring and one deleter:\n" + string.Join("\n", problems));
    }

    // ── probes ─────────────────────────────────────────────────────────────

    /// <summary>#600: which handler runs first — the last write to Trace wins.</summary>
    private static string NotifyOrder() {
        var domain = Compile(NotifyDomain);
        var exec = ModuleExec.For(domain);
        var order = exec.Create("Order", Values(("Ref", "O1"))).Instance;
        if (order is null)
            return "create Order refused";
        if (!exec.Invoke(order, "AddShipment", Values(("code", "S"))).Succeeded)
            return "AddShipment refused";
        var shipment = exec.LinkedTargets("shipments", order).Single();
        if (!exec.Invoke(shipment, "Ship").Succeeded)
            return "Ship refused";
        return GuideBlocks.Text(order.Bag["Trace"]);
    }

    /// <summary>#601: how many times <c>when any</c> fires across two deliveries.</summary>
    private static string AnyFiresOnce() {
        var domain = Compile(AnyOnceDomain);
        var exec = ModuleExec.For(domain);
        var order = exec.Create("Order", Values(("Ref", "O1"))).Instance;
        if (order is null)
            return "create Order refused";
        foreach (var code in new[] { "S1", "S2" }) {
            if (!exec.Invoke(order, "AddShipment", Values(("code", code))).Succeeded)
                return "AddShipment refused";
        }
        var shipments = exec.LinkedTargets("shipments", order);
        foreach (var shipment in shipments) {
            if (!exec.Invoke(shipment, "Ship").Succeeded)
                return "Ship refused";
        }
        return GuideAnswers(("AnyN", order.Bag["AnyN"]), ("EachN", order.Bag["EachN"]));
    }

    /// <summary>#492: is <c>X: action when S { }</c> refused, or accepted and silently dropped?</summary>
    private static string ActionWhenStage() {
        var result = GuideBlocks.Analyze("""
            domain T

            A: entity {
              S: stage { }
              X: action when S { }
            }
            """);
        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        return errors.Count == 0 ? "accepted" : "rejected (" + string.Join(", ", errors.Select(d => d.Code)) + ")";
    }

    /// <summary>
    /// #637: the guide says removing an owner is exempt from the owned-target minimum. The probe is
    /// negative: no removal effect exists, and the minimum still refuses a last-owner unlink.
    /// </summary>
    private static string OwnedRemoval() {
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
            return "create refused";
        if (!exec.Link("kid", a, b).Succeeded)
            return "link refused";
        var unlink = exec.Unlink("kid", a, b);
        return $"{refused}/3 effect verbs are parse errors; last-owner unlink "
               + (unlink.Succeeded ? "allowed" : "refused");
    }

    /// <summary>#518/#577: an action inside the stage can falsify the stage's <c>require</c>.</summary>
    private static string StageRequireAfterAction() {
        var result = GuideBlocks.Analyze(StageRequireDomain);
        var errors = result.Diagnostics.Count(d => d.IsError);
        if (result.Domain is null)
            return $"rejected ({errors} errors)";
        var exec = ModuleExec.For(result.Domain);
        var created = exec.Create("Case", Values()).Instance;
        if (created is null)
            return "create refused";
        var cleared = exec.Invoke(created, "Clear");
        var closed = exec.Invoke(created, "Close");
        return $"{errors} errors; Clear {(cleared.Succeeded ? "succeeded" : "refused")}; "
               + $"Close {(closed.Succeeded ? "succeeded" : "blocked")}";
    }

    /// <summary>#576: <c>create</c> can start an instance in a stage it already violates.</summary>
    private static string StageRequireAtCreate() {
        var result = GuideBlocks.Analyze(StageRequireDomain);
        if (result.Domain is null)
            return "rejected";
        var exec = ModuleExec.For(result.Domain);
        var created = exec.Create("Case", Values(("Cleared", true))).Instance;
        if (created is null)
            return "create refused";
        var stage = created.Bag.TryGetValue("CurrentStage", out var current)
            ? GuideBlocks.Text(current)
            : "(no stage)";
        return $"created in {stage} with Cleared=" + GuideBlocks.Text(created.Bag["Cleared"]);
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

    private static string GuideAnswers(params (string Name, object? Value)[] pairs) =>
        string.Join(", ", pairs.Select(pair => pair.Name + "=" + GuideBlocks.Text(pair.Value)));
}