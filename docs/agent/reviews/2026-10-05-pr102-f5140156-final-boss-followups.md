# Followups: PR 102 (B1b) Final Boss, tip f5140156

Findings from F360. Nothing blocks SHIP.

- [ ] **F360 (= Razor F1, B1c / F291)** Delete `EmitInstanceNotify` from `LoweringContext`, the `if (_emitInstanceNotify)` `this.Notify` branch in `EffectLoweringPass.StageTransition`, and the four explicit `EmitInstanceNotify: false` arguments (Actions, when-handler, first-stage ctor entry, `BuildStageBatchMethodExport`). Update `StageTransitionHostAbiTests` (`StageTransition_RuntimeContext_LowersToAssignmentAndInvokeNotify`, `Export_Transition_EmitsNotifyCallAndCompilesShape`) to assert **no** instance Notify. Confirm nothing still needs `DomainEntityInstance.Notify(string)` from a lowered tree.
- [ ] **F361 (= Razor F2 + class-doc nit)** Optional: `Open=false` → stay in `B`, `Differences` empty. Narrow the test class summary so it does not imply exit is covered (exit remains F292).
- [ ] **F292 (unchanged, out of scope)** Exit-block `transition` overflows the exporter; still not testable here.
