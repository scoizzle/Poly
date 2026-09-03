# Round 5 — agent-b findings (slice: stage-subscription quantifiers + dispatch-plan parity)

Probes: `probes/round5-agent-b/` — milestones.poly (entity-level `all` + stage-scoped
`any` + `Each`), warehouse.poly (placement parity), rejects/ (DMSS003 singular
quantifier rejections). Exports statically reviewed.

## F10 — `when all Rel Stage` subscription fires on EVERY matching transition in the export (no all-set gate)
- **Signal:** export/runtime divergence (+ guide drift)
- **Severity:** 🟠
- **Slice:** subscription quantifier `all`
- **Repro:** `probes/round5-agent-b/milestones.poly` — entity-level
  `when all tasks Done { assign Status to "allDone" }`.
  Export: `Task.NotifyDoneSubscribers()` calls `sub.WhenAllTaskDone()` unconditionally on
  every Done transition; the handler body `this.Status = "allDone";` has no check that
  every linked task is Done. With 2 linked tasks, the FIRST task's Finish already sets
  "allDone". Runtime (DomainInstanceStore.DispatchMatchingEntries) computes
  `matchedCount == allLinkedTargets.Count` and skips until every linked target is Done.
- **Expected:** guide §7: "`all` only fires once the whole linked set is in a matching
  stage; until then transitions into the stage are ignored." Export must gate the
  handler on the full linked set (e.g. `Tasks.All(t => t.CurrentStage == Done)`).
- **Actual:** export fires on the first transition; runtime waits for all → divergent
  state on the same DSL. (Export consumes the dispatch plan for handler NAMES but not
  the quantifier preconditions.)
- **Proposed patch:** the C# export must emit the quantifier precondition inside the
  handler (or at the notify site): for `All`, `sub.Tasks.All(t => t.CurrentStage == <Stage>)`;
  for `Any` the per-transition fire is equivalent (condition monotonic), so only `All`
  needs the gate. Also add the stage-scoped gate for entity-level handlers' stage
  placement if applicable (stage-scoped `any` already gates via CurrentStage check).

## Verified-OK in this slice (not findings)
- `Each` (notification-only + peer-dependent overloads) fire per transition on both
  paths; handler naming `WhenEach/WhenAny/WhenAll{Target}{Stage}` matches the guide.
- Stage-scoped placement gate is inside the generated handler (`CurrentStage != X →
  return`), and stage-scoped handlers run before entity-level in the runtime store.
- DMSS003 rejects `any`/`all` on singular relationships at analysis (rejects/ probes).
- Binder (`as name`) path-prefix reads lower to the handler parameter; multi-stage
  lists + entity-level binder compile 0/0.
