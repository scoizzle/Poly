# PR 74 Slice A — Final Boss follow-ups (re-verify) — 2026-09-22

- **Review**: [`2026-09-21-pr74-7784b2ab-final-boss.md`](2026-09-21-pr74-7784b2ab-final-boss.md)
- **PR**: [#74](https://github.com/scoizzle/Poly/pull/74)
- **SHA**: `7784b2abe81500b6e5c83c937e921ba7e87f952e`
- **Mode**: re-verify of Final Boss @ `8d687912844a3cbdc92c97fee341643a84f819d3` after named F1 fix-up (skip Razor)
- **Model**: grok-4.6
- **Open bugs**: 0 (F1 CLOSED)
- **Verdict**: **ship**
- **Prior ship**: [`2026-09-21-pr74-8d687912-final-boss.md`](2026-09-21-pr74-8d687912-final-boss.md) @ `8d687912844a3cbdc92c97fee341643a84f819d3`

## Closed

- [x] **F1** — **bug** — `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:309-311` — **CLOSED**. Comment now: SubscriptionBodies are Parameter-rooted (UseThis:false); residual twin vs module UseThis handlers; BindExportBody no-op without This. Cross-check `RuntimeAnalysisCache.BuildSubscriptionCaches` still `UseThisReference: false` at `:346` (residual comment `:339-341`). `git diff 8d687912..7784b2ab` is HostAbi comment-only (+3/−1). HostAbi `:300` and `:309-311` no longer contradict. Not a demonstrated fail-open; miss still throws `:302-308`.

## Open

- [ ] **F2** — **suggestion** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:958` — Harden `RewriteVoidFailClosedThrow` default (`_ => node`) to match `BindThis` fail-loud on unknown kinds, or exhaust EffectLowering node shapes. Preserve `TypeCast.IsChecked` (`:929-931` vs `TypeCast.cs:12`). Add unit oracle: plant `ThrowStatement(New IOE(...))` → assert `Return(DomainResult.Failure(...))`.

- [ ] **F3** — **suggestion** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:762-770` — Route entry/exit `TryGetExitMethod` / `TryGetEntryMethod` fallback through `BindExportBody` (or apply `RewriteVoidFailClosedThrow` after `BindModuleMethodBody`) so void fail-closed throw rewrite is not primary-path-only.

- [ ] **F4** — **suggestion** — `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:225-228` (exit `:237-240`) — On `existingNames.Add` failure for `OnEntry{Stage}` / `OnExit{Stage}`, do not attach a second lowered Body to EntryExitBodies while the module keeps the old method. Fail closed or reuse existing method Body so ReferenceEquals share holds (or document collision → no share).

- [ ] **F5** — **suggestion** — `Poly.Tests/DomainModeling/Compile/SliceASharedModuleBodyTests.cs` + `ClearEntryExitBody` (`RuntimeAnalysisCache.cs:445`) — Add Slice A–named oracles: (1) BindExportBody void throw→Failure shape; (2) ClearEntryExitBody → Domain-bound transition/entry throws (mirror Item3 ClearPolicy/ClearSubscription). No silent LowerActionBody. `git grep ClearEntryExitBody -- '*.cs'` still only the definition.

## Out of scope (do not reopen as Slice A ship blockers)

- DEI delete, Item 5 Occupancy, CURRENT, producer-loop C, DEI-not-sim E, PR 73
- Slice B execute-time LowerActionBody residuals (`DomainEntityInstance.cs:778-780`, Domain-null `:789`)
- Policy + subscription Parameter twins (explicit residuals; F1 was comment honesty only — now CLOSED)
- PIPELINE-STATUS / CURRENT admission
- ToSyntax ctor first-stage OnEntry inline vs OnEntry{FirstStage} method (pre-existing dual UseThis lower; stop is method↔EntryExitBodies)

## Disposition vs prior Final Boss @ 8d687912

| Prior F# | This SHA | Disposition |
|----------|----------|-------------|
| F1 HostAbi `:309` false UseThis comment | `:309-311` Parameter-rooted UseThis:false; cache `:346` still false; comment-only delta | **CLOSED** |
| F2 Rewrite soft default `:958` | still `_ => node` | **still open** |
| F3 fallback BindModuleMethodBody `:762-770` | still no throw rewrite | **still open** |
| F4 name-collision extras.Add | still `:225-228` / `:237-240` | **still open** — not upgraded |
| F5 ClearEntryExitBody untested | `git grep` callers still zero in tests | **still open** |

Prior Final Boss verdict **ship** / stop **MET** — **confirmed** on tip. No new F#. No fail-open found.

## Process

- [x] **P1** (prior) — Invariant-stating comments on bind wrappers must match the cache’s UseThis flag on **that** body. HostAbi `:309-311` now matches `:300` / `RuntimeAnalysisCache.cs:346`. Recurring class addressed for this site.
