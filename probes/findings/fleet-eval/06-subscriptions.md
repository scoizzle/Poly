# Fleet-eval 2026-08-12 — 06-subscriptions findings (slice: subscription & dispatch analysis)

Probes: `probes/fleet-eval/06-subscriptions/` — dispatch-hub.poly (stage-scoped +
entity-level any/all/Each, peer binders, multi-stage lists), laundry-service.poly
(stage-scoped create-in-subscription + entity-level all w/ binder + multi-stage
any/all), library-fines.poly (guide §0.4 create-Fine canonical + entity-level
peer binder + multi-stage Each), missing-subscriber-prop.poly (reject probe, F5).
All probes run through `scripts/run-probe.sh`; exports statically reviewed against
`DomainInstanceStore.NotifyTransition` / `DomainEntityInstance` / `DomainToCSharpExporter`.

## F1 — subscription `create` / `create in` crashes the runtime notify loop (collection modified during enumeration)
- **Signal:** divergence (runtime crash; export succeeds on the same DSL)
- **Severity:** 🔴
- **Slice:** subscription runtime dispatch (quality / product / reliability)
- **Repro:** `probes/fleet-eval/06-subscriptions/library-fines.poly` (or §0.4-canonical
  `when loans Overdue { create Fine { Amount: 5 } }`):
  1. store.Add(patron); store.Add(loan); store.Link("loans", patron, loan);
  2. loan.InvokeAction("MarkOverdue") → TransitionStage → Store.NotifyTransition
  3. `foreach (var subscriber in _instances)` (DomainInstanceStore.cs:153) → subscriber
     match → `ExecuteSubscriptionEffects` → `EffectExecutor.CreateEntityInstance`
     → `CreateChildInstance` → `Store?.Add(child)` (DomainEntityInstance.cs:1092)
     → `_instances.Add` while the outer `foreach` is live → `InvalidOperationException`
     ("Collection was modified; enumeration operation may not execute").
- **Expected:** guide §0.4 canonical pattern works on the store path
  (create_instance → invoke_action → subscription fires a create). The export handles
  it (emits `var fine = Fine.Create(...)` in the handler).
- **Actual:** the runtime notify loop crashes deterministically; the whole
  subscription-create surface dead-ends. **Verified at runtime** via a scratch harness
  referencing Poly (no repo edits): `loanInstance.InvokeAction("GoOverdue")` on a
  patron with `when loans Overdue { create Fine { Amount: 5 } }` →
  `InvalidOperationException: Collection was modified; enumeration operation may not
  execute.` No runtime test creates inside a subscription; the P4/store fail-closed
  suites only assign.
- **Proposed patch:** snapshot `_instances` for the notification sweep
  (`foreach (var subscriber in _instances.ToArray())`) or dispatch against a
  subscriber-list indexed at notify time, so effects may add/remove instances;
  add a runtime regression test for `when loans Overdue { create ... }`.

## F2 — multi-stage `all` never fires when the linked set is spread across the listed stages
- **Signal:** guide drift + silent gap (export and runtime AGREE, both wrong per guide)
- **Severity:** 🟠
- **Slice:** quantifier set-state semantics (quality / consistency)
- **Repro:** `probes/fleet-eval/06-subscriptions/laundry-service.poly`
  `when all orders Ready, Delivered` with 2 linked orders, one in Ready, one in
  Delivered (order A → Ready, then order B → Delivered).
  Runtime: `DispatchMatchingEntries` counts `matchedCount = targets with CurrentStage ==
  targetStageName` only (DomainInstanceStore.cs:248-249) → 1 != 2, never fires.
  Export: per-stage handlers `WhenAllOrderReady` / `WhenAllOrderDelivered` each gate on
  `linkedTarget.CurrentStage != OrderStage.<that stage>` → same, never fires.
- **Expected:** guide §7: "all only fires once the whole linked set is in a matching
  stage" — every linked order IS in {Ready, Delivered}, so `all` must fire.
- **Actual:** fires only when every target is in the SAME single stage; a spread set
  silently never fires on either path. Single-stage `all` is correct. **Verified at
  runtime**: customer with `when all orders Ready, Delivered`, order A→Ready then
  order B→Delivered (set = {A:Ready, B:Delivered}) leaves `Status='none'` — never
  fires.
- **Proposed patch:** evaluate the set predicate against the union of the subscription's
  `StageNames` in both the runtime (`entry.StageNames.Any(sn => t.CurrentStage == sn)`)
  and the export gate.

## F3 — export fires entity-level handlers BEFORE stage-scoped; runtime (and guide) do stage-scoped first
- **Signal:** export/runtime divergence (guide drift in the export)
- **Severity:** 🟠
- **Slice:** dispatch-plan order (stage plan first, then entity plan) (consistency)
- **Repro:** `probes/fleet-eval/06-subscriptions/dispatch-hub.poly` — `NotifyBackSubscribers`
  calls `WhenAnyTruckBack`/`WhenAllTruckBack`/`WhenEachTruckBack` (entity-level)
  BEFORE `WhenAllTruckBack()`/`WhenEachTruckBack_2` (stage-scoped Open). The runtime
  dispatches the subscriber's current-stage plan first, then the entity plan
  (DomainInstanceStore.cs:156-198). Guide §7: "Store notify runs stage-scoped handlers
  first, then entity-level."
- **Expected:** stage-scoped effects run first, entity-level second, on both paths.
- **Actual:** the export's notify list is entity-first because `DomainProgramProjection`
  collects entity plans before stage plans; observable divergence when both placements
  write the same subscriber property. **Verified at runtime**: stage-scoped
  `assign HubStatus to "from-stage"` + entity-level `assign HubStatus to "from-entity"`
  on the same `when trucks OnRoute` → runtime leaves `'from-entity'` (stage first);
  the export's notify order (entity first) would leave `'from-stage'`.
- **Proposed patch:** order `subscriptionsByTarget` entries stage-first (collect stage
  plans before the entity plan in `DomainProgramProjection.ToSyntax`), or sort the
  notify calls by placement.

## F4 — multi-initializer `create`/`create in` printer/parser round-trip break; guide §0.4 `;` syntax never parses
- **Signal:** guide drift + fail-loud-but-sharp (golden workflow `apply_dsl → export_dsl → apply_dsl` breaks)
- **Severity:** 🟠
- **Slice:** subscription effect authoring (consistency)
- **Repro:**
  - `DomainDslPrinter.CreateEntityInstance` / `CreateEntityInRelationship` emit `, `
    separators (DomainDslPrinter.cs:362, 380).
  - `PolyDslParser.ParsePropertyInitializers` rejects `,` and `;`
    (verified: `create Item { Code: "A", Qty: 5 }` → "Parse error: Expected property
    name, got ','").
  - The guide §0.4 example `create Fine { Amount: 5; Reason: "Overdue" }` (`;` separators)
    also fails to parse (verified; `probes/fleet-eval/12-mcp/mcp-library.poly` fails the same way).
  - Any multi-initializer create in a subscription (or action) body exports to text
    `apply_dsl` cannot re-parse. The shipped authorable form is space-separated
    (`create Fine { Amount: 5 Reason: "Overdue" }`), which the guide never shows.
- **Expected:** printer output round-trips through the parser; guide examples parse.
- **Actual:** export_dsl emits commas the parser rejects; the guide's own `;` example
  is unparseable.
- **Proposed patch:** make `ParsePropertyInitializers` consume optional `,`/`;` after
  each binding (matching the printer), and fix the §0.4 guide example.

## F5 — missing subscriber/peer property in subscription effects passes analysis, fails at the C# compiler (late rung)
- **Signal:** fail-loud-but-sharp (wrong rung; analyzer emits a dropped WARNING)
- **Severity:** 🟡
- **Slice:** subscription effect bindings (reliability)
- **Repro:** `probes/fleet-eval/06-subscriptions/missing-subscriber-prop.poly`
  `when orders Delivered { assign Balance to Total }` (notification-only; `Balance` and
  `Total` do not exist on the subscriber). run-probe → `error CS1061: 'Account' does not
  contain a definition for 'Balance'...'Total'`. Runtime would fail later at VM compile.
- **Expected:** SubscriptionAnalyzer rejects missing subscriber/peer property references
  as an ERROR at authoring time (earliest rung; the check exists but calls
  `ReportWarning`, SubscriptionAnalyzer.cs:354-376). The DslCompiler surfaces only
  `DiagnosticSeverity.Error` (DslCompiler.cs:135-136), so the warning is dropped and the
  DSL is accepted until Roslyn.
- **Actual:** analysis accepts; export compile-fails (and the runtime VM fails differently
  on the same DSL — divergent failure modes on one authoring error).
- **Proposed patch:** promote the missing-prop diagnostics to `ReportError` (fail-closed)
  so the authoring rung rejects; keep the message/available-props hint.

## F6 — export `Register{Source}{Stage}Subscriber` accepts unlinked subscribers; runtime requires a store link
- **Signal:** export/runtime integrity asymmetry (security)
- **Severity:** 🟡
- **Slice:** subscriber registration / peer isolation (security)
- **Repro:** `dispatch-hub.poly` export — `internal void RegisterDispatchHubBackSubscriber(DispatchHub subscriber)`
  adds any DispatchHub unconditionally (no link check). The runtime only fires for
  instances `IsLinked` to the transitioned entity (DomainInstanceStore.cs:223). An
  internal caller can inject a non-linked subscriber into the target's registry and it
  will receive cross-entity notifications in the export but not the runtime.
- **Expected:** registration mirrors the runtime's link validation (fire only when the
  subscriber is actually a linked peer).
- **Actual:** export registration is unvalidated; runtime dispatch is link-validated.
- **Proposed patch:** have `InitializeSubscriptions`/`CreateNav` remain the only
  registration path (they are), or gate the notify loop on the collection membership of
  the subscriber in the target's nav — matching the store's `IsLinked` contract.

## F7 — `create Fine` (no relationship) inside a subscription creates an ORPHAN record on both paths
- **Signal:** modeling trap (faithful but surprising)
- **Severity:** 🟡
- **Slice:** subscription effects (product)
- **Repro:** `library-fines.poly` `when loans Overdue { create Fine { Amount: 5 } }` (the
  guide §0.4 canonical form). Export handler: `var fine = Fine.Create(...);` — value
  discarded, never added to `Patron.Fines`. Runtime: child registered in the store but
  not linked to `fines` — unreachable via the patron.
- **Expected:** the guide's §0.4 example should either use `create in fines` (linked) or
  the docs should state that `create T` is an unlinked record.
- **Actual:** the Fine is created and immediately unreachable (detached local in the
  export; orphan in the store) — plus the F1 crash on the runtime path.
- **Proposed patch:** docs: show `create in fines { ... }` in §0.4 (the linking form), and
  note `create T` is an unlinked record.

## Verified-OK in this slice (not findings)
- Single-stage `any`/`all`/`Each` fire semantics match between export and runtime
  (including the round-5 F10 all-set gate now emitted in the export).
- `Each` multi-stage lists + shared peer binder compile 0/0 and lower correctly
  (one handler per stage, binder = transitioned instance on both paths).
- DMSS003 rejects `any`/`all` on singular relationships (existing rejects).
- Fail-closed metadata guards verified by tests: missing RelationshipContract /
  EntityStructure / SubscriptionDispatchPlan / DomainCatalog all throw.
- Peer path-prefix binding (`order Code` → parameter `order.Code` in export; literal
  from the peer bag in runtime) is consistent; nested peer paths and peer-as-assign-
  target are rejected at analysis.
- Subscriber registration dedup in the export is correct: multiple subscriptions on the
  same (rel, stage) register the subscriber once per stage and the notify fan-out calls
  each quantifier-disambiguated handler.
