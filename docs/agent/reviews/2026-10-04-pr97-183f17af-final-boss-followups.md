# Followups: PR 97 (B1) Final Boss, tip 183f17af

Findings numbered from F290. Nothing here blocks the PR.

- [ ] **F290 (bug, pre-existing, same root cause as B1)** `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:322` (`BuildStageBatchMethodExport`): add `EmitInstanceNotify: false` to its `LoweringContext`. Failing-first test: compile the `DomainSession.Emit` output (not the exporter-only parity harness) for
  ```
  domain M
  Z: entity { Tag: Text  Count: Number  A: stage { Go: action { transition to B } }  B: stage { entry { if (Count >= 0) { transition to C } } }  C: stage { } }
  ```
  Today `OnEntryB` prints `this.Notify("C")` (CS1061; no `Notify` member in printed classes). Add the same shape for `exit` only after F292 is fixed. Suggested: fold into B1 as a one-line addition, or card as B1b.
- [ ] **F291** Flip `LoweringContext.EmitInstanceNotify` default to `false` (or delete the flag and the `if (_emitInstanceNotify)` try/finally at `EffectLoweringPass.cs:601`); `StageTransitionHostAbiTests` (lines ~47-50) is the only user of `true`. Do after F290.
- [ ] **F292 (card)** Exit-block transition overflows the exporter stack. Repro DSL:
  ```
  domain E1
  Z: entity { Tag: Text  A: stage { exit { transition to C }  Go: action { transition to B } }  B: stage { }  C: stage { } }
  ```
  Export aborts: `Stack overflow. Repeated 5224 times: EffectLoweringPass.StageTransition -> EffectDispatch.Route -> RouteWithRuntimeCreate -> AppendInlinedStageEffects`, exit 134, takes down the whole test host. Also a non-first stage `exit { if (Count >= 0) { transition to C } }`. Needs a fail-loud analyzer diagnostic or a bounded lowering, plus a failing-first test that does not crash the host.
- [ ] **F293 (card)** Duplicate `private Z()` constructors (CS0111). Repros (all fail on master and head):
  ```
  domain S1
  Paper: entity { A: stage { Advance: action { transition to B } }  B: stage { } }
  ```
  ```
  domain S2
  Z: entity { Tag: Text  A: stage { entry { assign Tag to "entered" } }  B: stage { } }
  ```
  Printed class carries `private Z() { }` and `private Z() { this.CurrentStage = ZStage.A; this.Tag = "entered"; }`. Trigger is "create has no required parameters", not only "stage-only". The transition-only first-stage entry (`entry-transition-in-first-stage.poly`) compiles. Likely owner: C2b (compiled Create).
- [ ] **F294 (nit)** Give `KnownGap_FirstStageEntryTransition_SimulateStaysInFirstStage` a firm owner slice (comment currently: "none named in the plan; closest is C2b") and narrow its sentence to "skips top-level transition effects" (a nested `if` transition in first-stage entry runs in simulate and agrees with printed: B/B).
- [ ] **F295 (nit)** Correct the PR body counts if it is reused for the squash message: 6 new tests go red with the fix reverted (not 5), and three printed contexts defaulted to `true` (F290), not two.
- [ ] After merge: plan "Done so far" gets B1 (merge SHA); B1 card should list the pinned gaps (cascade to C5b; first-stage-entry transition owner TBD) and the cards from F290, F292, F293.
