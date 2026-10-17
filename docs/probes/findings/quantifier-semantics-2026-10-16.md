# Stage-subscription quantifiers and the peer binder — evidence 2026-10-16

Worktree `s22-s25-quantifier-semantics`, base master `ff83c598`. Method: in-process MCP
(`McpTestTools.Dsl` / `McpTestTools.Runtime`, the same surface `SimulateCreateDogfoodTests`
builds) driven by a temporary TUnit probe that was deleted after capture; counters are read
from the live `DomainInstanceStore`. No product or test file is changed by this pass.
This closes the S21/S23/S25 thread in `docs/probes/findings/scratch-2026-10-14.md`.

The whole quantifier decision is one lowered body shared by simulate and print
(`DomainProgramProjection.ToSyntax` → `DomainToCSharpExporter.BuildTypeDefsForEntity`);
there is no store-side gate to compare against.

- `any`: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:679-706` (`linkedMatched` loop).
- `all`: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:643-677` (`linkedMatched` loop +
  `previousStage` once-edge; the `previousStage` slot is chosen at `:136-143`).
- `each`: no gate.
- Peer binder: `DomainToCSharpExporter.cs:576-594` (handler parameter) and `:603-607` (the binder
  is a **single** peer node, never the set).

## 1. Observed behaviour

### 1.1 `any` fires per transition exactly like `Each`; `all` fires once

Domain (inline, no `uses`): `QFleet` — one `WorkOrder` with `DoneCount` / `AnyCount` / `AllCount`,
three `Job`s, and one fan-out action that moves all three at once. This is the `fleet.poly`
shape from Oct-14.

```poly
domain QFleet
WorkOrder: entity {
  Note: Text required
  DoneCount: Number default(0)   AnyCount: Number default(0)   AllCount: Number default(0)
  jobs: many Job
  Open: stage {
    AddJob: action (label: Text) -> Job { create in jobs { Label: label } }
    CompleteAll: action { for jobs as t where t in Todo invoke t.Complete }
  }
  when jobs Done      { assign DoneCount to DoneCount + 1 }
  when any jobs Done  { assign AnyCount  to AnyCount  + 1 }
  when all jobs Done  { assign AllCount  to AllCount  + 1 }
}
Job: entity {
  Label: Text required
  workOrder: WorkOrder
  Todo: stage { Complete: action { transition to Done }   Skip: action { transition to Skipped } }
  Done: stage { }   Skipped: stage { }
}
```

Instrument: `AddJob` ×3, then one `CompleteAll`; counters after the single invocation:

```
== A. fan-out: for jobs as t where t in Todo invoke t.Complete (3 children) ==
   before      Done=0 Any=0 All=0
   invoke msg  Action 'CompleteAll' succeeded. New stage: 'Open'.
   after       Done=3 Any=3 All=1
```

`Done=3` (Each: 3 transitions), `Any=3` (guide promises 1), `All=1`. Reproduces S25 exactly.
`for … invoke` is the "one event moves several peers" instrument S21 deferred; with it, `any`
and `Each` cannot be told apart.

### 1.2 `any` is per-transition; `all` re-fires on set re-entry

Domain `QRefire`: `Board.items: many Item`; `Item.Draft.Start → Ready`, `Item.Ready.Unready → Draft`;
`when all items Ready` and `when any items Ready` counters. Three items linked via `link_instances`.

```
== D. `all`/`any` re-fire (Board items: A,B,C) ==
   start                                       All=0 Any=0
   A Start (Ready)                             All=0 Any=1
   B Start (Ready)                             All=0 Any=2
   C Start -> set all Ready                    All=1 Any=3
   A Unready (Ready->Draft, unwatched target)  All=1 Any=3
   A Start again -> set all Ready              All=2 Any=4
```

- `any` increments on every transition (4), including the re-entry: level-triggered, once per
  notify event.
- `all` fires on the set-completion edge (1) and **again** after the set leaves the all-matching
  state and re-enters it (2).
- `all` does **not** re-fire while the set stays matching: the `previousStage` guard returns when
  the transitioned peer's previous stage was itself watched. That is pinned by
  `Poly.Tests/DomainModeling/Compile/WhenAllSimulatePrintAgreeTests.cs:74-79` (Ready→Done,
  `Fires` stays 1).

So "Fires once" (guide table) means **once per set-entry**, not once ever. S23's "fires again on
later transitions once the set is already all-matching" is a re-entry, not a spurious re-fire.

### 1.3 `all` + peer binder silently loses every peer that entered before the set completed

Domain `QPayAll` (the `payroll.poly` shape): `Employee.timesheets: many Timesheet`,
`when all timesheets Approved as sheet { assign Gross to Gross + sheet Hours * Rate }`, `Rate=20`.
Sheets are created with `Log` (so the create-in links them) and approved one at a time.

```
== B. `when all timesheets Approved as sheet { assign Gross to Gross + sheet Hours * Rate }` (Rate 20) ==
   logged 8h, 6h; Gross=0
   approve 8h -> Gross=0 (of intent 160)
   approve 6h -> Gross=120 (of intent 280)
   log 4h + approve -> Gross=200 (of intent 360)

== C. control: default Each + peer binder (same math) ==
   approve 8h -> Gross=160
   approve 6h -> Gross=280
   approve 4h -> Gross=360
   intent total = 360

== E. `all` + binder, three sheets present and submitted upfront ==
   approve 8h -> Gross=0
   approve 6h -> Gross=0
   approve 4h -> Gross=80
   intent total = 360
```

Reproduces S22 exactly (`0 → 120 → 200`). The mechanism:

- Under `all` the handler runs **only** on the transition that completes the set. A sheet approved
  earlier never produced a firing, so its hours were never added.
- On that completion firing the binder is **only** the transitioned peer
  (`DomainToCSharpExporter.cs:603-607`), so even the completing transition contributes just its own
  hours.
- B: the set was `{8h, 6h}` when 6h was approved → `+6×20 = 120`; 4h was logged afterwards and
  completed the new set → `+4×20 = 80`; total 200. The 8h sheet contributed nothing.
- E: three sheets exist and are submitted up front, so every approval before the last is ignored;
  only the last peer is bound → 80.
- C: the identical money math under the default `Each` + binder sums exactly (160/280/360). The
  loss is specific to `all`, not to the binder.

No diagnostic is emitted in any case; `get_domain_analysis` reports 0 errors (`QFleet`, `QPayAll`
and `QFleet`'s `WorkOrder/jobs:[All,Any,Each]` dispatch plan are surfaced, but only as names).

### 1.4 What reconciles vs the Oct-14 notes

- S21 (`any` fires per transition) — confirmed.
- S22 (`0 → 120 → 200`) — reproduced exactly. S22's phrase "after sheet 2 (the set is now
  all-approved)" holds because the 4h sheet did not exist yet; the completion edges are real, the
  lost 8h is the defect.
- S23 ("`all` also fires on later transitions once the set was already entirely `Approved`") —
  **refined**: the sheet-3 firing is the set *growing* and completing a second time, not a re-fire
  while it stayed matching. No re-fire happens while the set stays matching (1.2).
- S25 (`any` indistinguishable from `Each`) — confirmed; additionally `any`'s gate is provably
  dead (below).

## 2. What the guide/router claims, and where it diverges

`Poly.Mcp/Docs/poly-dsl-guide.md` §7 (`get_dsl_guide` serves this file verbatim):

| Locus | Claim | Engine reality |
|---|---|---|
| `poly-dsl-guide.md:628` | ``any`` — "Fires **once** when **at least one** linked related entity is in a matching stage" | Fires on **every** notify, i.e. per transition. Measured 3 (A), 4 (D). |
| `poly-dsl-guide.md:629` | ``all`` — "Fires **once** when **every** linked related entity is in a matching stage" | Fires once **per set-entry**; can fire again after a re-entry. Measured 2 (D). |
| `poly-dsl-guide.md:631-633` | "`any`/`all` (and `Each`) all evaluate the **set state after the transition** … `all` only fires once the whole linked set is in a matching stage; until then transitions into the stage are ignored." | True for `all`. For `any` the set-state evaluation is **undecidable-to-false**: a notify is emitted only when a target enters a watched stage (`DomainToCSharpExporter.cs:525-573`), so the transitioned peer is itself matching and `linkedMatched` is always `true` — the `if (!linkedMatched) return;` at `:700-702` is unreachable. `any` is therefore exactly `Each` with a no-op loop. |
| `poly-dsl-guide.md:643-644` | "Peer binder (`as name`) remains valid with `any`/`all`; the peer is the **transitioned** instance for that firing (same as `Each` today)." | Literally true (`DomainToCSharpExporter.cs:603-607`). The divergence is what the guide omits: under `all` the firing only happens on the completion edge, so combined with a single peer binder the pattern `Gross += peer` silently drops every earlier peer (1.3). |
| `poly-dsl-guide.md:670-701` ("Two shapes") | Peer fields available through the binder; no caution about accumulation. | Same omission; the table reads as "you can accumulate with `all`". |
| `Poly/DomainModeling/Ontology/StageSubscription.cs:13-16` | XML: `Any` "Fires effects once when at least one related entity is in a matching stage." | Same false "once"; this enum doc is the second place the claim lives. |
| `Poly.Mcp/Tools/DomainTools.cs:371-381` (analysis) | Surfaces `WorkOrder/jobs:[All,Any,Each]` as three distinct dispatch entries. | Names only; a reader infers three behaviours where two are identical. |

Generated handler text proves the `any` gate is a formality (golden, `university`):

```csharp
internal void WhenAnyWaitOfferOffered()          // Poly.Tests/.../EmitGolden/university/Section.cs.golden:140
{
    var linkedMatched = false;
    foreach (var linkedTarget in this.Waitlist)
        if (linkedTarget.CurrentStage == WaitOfferStage.Offered) linkedMatched = true;
    if (!linkedMatched) return;                  // unreachable at notify time
    { this.OfferedSeats = this.OfferedSeats + 1L; }
}
```

`all` is not merely "once": its guard is `previousStage`, and its handler is a full-set loop:

```csharp
internal void WhenAllWaitOfferCancelled(WaitOfferStage? previousStage)  // Section.cs.golden:158
{
    var linkedMatched = false;
    foreach (var linkedTarget in this.Waitlist) {
        linkedMatched = true;
        if (linkedTarget.CurrentStage != WaitOfferStage.Cancelled) return;
    }
    if (!linkedMatched) return;                  // empty set never fires
    if (previousStage == WaitOfferStage.Cancelled) return;   // the "once" edge
    { this.WaitCleared = 1L; }
}
```

## 3. Options, seams, blast radius

### 3.1 `any`

**(a) Document that `any` is per-transition like `Each`.**
Seam: `Poly.Mcp/Docs/poly-dsl-guide.md:628` and `:631-633`; `Poly/DomainModeling/Ontology/StageSubscription.cs:13-16`;
the stale comment at `DomainToCSharpExporter.cs:680-681`.
Blast radius: guide text only. No test pins the §7 table (the `get_dsl_guide` tests pin §10's
"actor", §12's shapes, the golden-workflow marker, and the §11 example —
`Poly.Tests/Mcp/McpSmokeTests.cs:1309-1433`). Cheapest, zero-risk, stops the doc promising an
event model the engine does not implement.

**(b) Make `any` fire once per set-entry** ("has fired since the set last left the stage").
Seam: `DomainToCSharpExporter.cs:679-706` plus new per-`(subscriber, relationship)` state in the
generated module (a private flag reset when the matching count returns to zero); the registry
fan-out it would key off is `DomainToCSharpExporter.cs:525-573`.
Blast radius: changes the meaning of the one shipped `any` in a golden domain —
`docs/probes/dogfood/university.poly:107` `when any waitlist Offered { OfferedSeats + 1 }` is
used as a **per-offer counter**, i.e. as `Each`. Tests that would change:
`Poly.Tests/DomainModeling/P4SubscriptionQuantifierDslTests.cs:57` (asserts `any` fires on the
second transition), `Poly.Tests/DomainModeling/Compile/WhenAnySimulatePrintAgreeTests.cs:104-158`
(asserts Fires 1,2,3), `Poly.Tests/DomainModeling/Compile/EmitGolden/university/Section.cs.golden:140`,
`Poly.Tests/DomainModeling/Compile/EveryConceptHasTreeTests.cs:182-202`, and the `banking`/`fleet`
probes. Rejected: it adds persistent machinery to make a keyword match a doc row, and the gate in
(b) is only reachable by changing the notify trigger from "peer entered a watched stage" to
"set state changed", which is a much larger seam.

**(c) Retire `any`** (redundant with `Each`).
Seam: `Poly/DomainModeling/Language/PolyDslParser.cs:933-944`, `DomainDslPrinter.cs:406`,
`SubscriptionAnalyzer.cs:271-278`, plus a deprecation diagnostic.
Blast radius: parser/printer round-trip tests (`Poly.Tests/DomainModeling/Parsing/PolyDslRoundTripTests.cs:206,345`),
`Poly.Tests/Mcp/McpSmokeTests.cs:3104-3222`, and every probe/golden that writes `when any`
(`university.poly:107`, `banking.poly`, `fleet.poly`, `logistics.poly`). A follow-up, not this slice.

**Take (a); record (c) as the direction. Do NOT take (b).**
(a) is the only change that is both correct and free, and the language principle prefers shrinking
before adding state. (b) would silently change a shipped domain's money counter
(`OfferedSeats`) to prove a keyword whose current behaviour is already what real domains use.

### 3.2 `all` + peer binder

**(a) Document the loss** with the measured numbers, and show the two-subscription pattern that is
already correct: `when timesheets Approved as sheet { assign Gross to Gross + sheet Hours * Rate }`
(Each) for the accumulation + `when all timesheets Approved { … }` (no binder) for the completion
signal. Seam: `poly-dsl-guide.md:643-644` and `:670-701`; the binder comment at
`DomainToCSharpExporter.cs:596-599`.
Blast radius: guide text only.

**(b) A diagnostic when an `all` binder feeds an accumulation.** A warning, not an error, when an
`all` subscription has a `PeerBinding` and an effect assigns a **subscriber** property whose RHS
also reads that same subscriber property (the `X to X + peer …` trap) — i.e. the pattern whose
result depends on peers that this firing cannot see.
Seam: `Poly/DomainModeling/Analysis/SubscriptionAnalyzer.cs:292` (`ValidateSubscriptionEffectBindings`,
where peer/subscriber binding checks already live) with a new code in `DomainModelDiagnosticCodes`.
Blast radius: a warning leaves `DomainEvolution.Succeeded` true, so `VmAnalyzerReportTests`
(outcome is errors/`!Succeeded`) and `ParityTests` (reads errors) are unaffected;
`docs/probes/scratch-2026-10-14/payroll.poly` would gain one warning row in any report that prints
warnings. Narrow enough to avoid flagging the legitimate copy-a-peer-field use
(`assign LastCode to order Code`).

**(c) Lower the binder to see the whole set** (e.g. under `all`, run the body once per member when
the set completes, or bind the collection instead of one peer).
Seam: `DomainToCSharpExporter.cs:576-594`, `:603-607`, `:643-677`; analysis peer path-prefix
(`SubscriptionAnalyzer.cs:292+`); the whole-set iteration has no node today.
Blast radius: handler signature/param shape (`WhenAllSimulatePrintAgreeTests.cs:88-139`,
`Poly.Tests/DomainModeling/Compile/C1aRootProgramParamsTests.cs:88`), the `university`
goldens (`Section.cs.golden:158` `WhenAllWaitOfferCancelled(WaitOfferStage? previousStage)`), and
`EveryConceptHasTreeTests`/`ParityTests` for every `all` probe. Rejected: it turns a documented
scalar binder into a set binder and buys no capability — 1.3 C already shows `Each` + binder is the
exact total, and the completion signal is a separate `all` subscription with no binder.

**Take (a) now and (b) as the guard. Do NOT take (c).**

### 3.3 What I would not do

- Not implement `any` (b): new per-subscription state to satisfy a doc row, breaking a shipped
  domain's meaning.
- Not re-lower the binder (3.2c): no capability that `Each` + `all`-without-binder does not
  already provide.
- Not remove the `previousStage` once-edge from `all`: it is the only thing stopping `all` firing
  on every later transition; `all` would collapse into `Each` (plus the set test).
- Not "fix" the numbers by editing the payroll probe or the guide's example only: the engine loses
  data, and the probe is a faithful real shape.

**Top recommendation:** correct the guide/router (§7 table row `any`, the "once" wording for
`all`, and an explicit warning + worked example for `all` + binder), and add the narrow analysis
warning for the `all` accumulation trap. Touch neither lowering in this slice.

## Log

| Date | Who | Event | Findings |
|------|-----|-------|----------|
| 2026-10-16 | spike | Re-ran the S21/S22/S23/S25 instruments on master `ff83c598` via in-process MCP (`QFleet` fan-out, `QPayAll`/`QPayEach`/`QUpfront` binder math, `QRefire` re-entry); temp probe deleted, build 0/0 | `any` ≡ `Each` (3/3/1 fan-out; 4 on re-entry) and its `linkedMatched` gate is unreachable at notify time; `all` = once per **set-entry** (re-fires after re-entry), not once ever; `all` + peer binder loses pre-completion peers (Gross 0→120→200 vs 360; upfront 80 vs 360) while `Each` + binder is exact; guide §7:628/629/643 promise otherwise. Recommend guide correction + narrow `all` accumulation diagnostic; no lowering change. |
