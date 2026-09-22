# PR 74 Slice A — Final Boss follow-ups — 2026-09-21

- **Review**: [`2026-09-21-pr74-8d687912-final-boss.md`](2026-09-21-pr74-8d687912-final-boss.md)
- **PR**: [#74](https://github.com/scoizzle/Poly/pull/74)
- **SHA**: `8d687912844a3cbdc92c97fee341643a84f819d3`
- **Mode**: re-verify of Razor `docs/agent/reviews/2026-09-21-pr74-8d687912-razor-followups.md`
- **Model**: grok-4.6
- **Open bugs**: 1 (F1) — honesty comment; **do not block ship**
- **Verdict**: **ship**

## Open

- [ ] **F1** — **bug** — `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:309` — Fix false invariant comment claiming `SubscriptionBodies` are export-shaped (UseThis). Cache still lowers with `UseThisReference: false` (`RuntimeAnalysisCache.cs:346`). Align with HostAbi `:300` / cache `:339-341`. Prefer before merge. **Not** a demonstrated fail-open (this SHA).

- [ ] **F2** — **suggestion** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:958` — Harden `RewriteVoidFailClosedThrow` default (`_ => node`) to match `BindThis` fail-loud on unknown kinds, or exhaust EffectLowering node shapes. Preserve `TypeCast.IsChecked` (`:929-931` vs `TypeCast.cs:12`). Add unit oracle: plant `ThrowStatement(New IOE(...))` → assert `Return(DomainResult.Failure(...))`.

- [ ] **F3** — **suggestion** — `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:762-770` — Route entry/exit `TryGetExitMethod` / `TryGetEntryMethod` fallback through `BindExportBody` (or apply `RewriteVoidFailClosedThrow` after `BindModuleMethodBody`) so void fail-closed throw rewrite is not primary-path-only.

- [ ] **F4** — **suggestion** — `Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:225-228` (exit `:237-240`) — On `existingNames.Add` failure for `OnEntry{Stage}` / `OnExit{Stage}`, do not attach a second lowered Body to EntryExitBodies while the module keeps the old method. Fail closed or reuse existing method Body so ReferenceEquals share holds (or document collision → no share).

- [ ] **F5** — **suggestion** — `Poly.Tests/DomainModeling/Compile/SliceASharedModuleBodyTests.cs` + `ClearEntryExitBody` (`RuntimeAnalysisCache.cs:445`) — Add Slice A–named oracles: (1) BindExportBody void throw→Failure shape; (2) ClearEntryExitBody → Domain-bound transition/entry throws (mirror Item3 ClearPolicy/ClearSubscription). No silent LowerActionBody.

## Out of scope (do not reopen as Slice A ship blockers)

- DEI delete, Item 5 Occupancy, CURRENT, producer-loop C, DEI-not-sim E, PR 73
- Slice B execute-time LowerActionBody residuals (`DomainEntityInstance.cs:778-780`, Domain-null `:789`)
- Policy + subscription Parameter twins (explicit residuals; F1 is comment honesty only)
- PIPELINE-STATUS / CURRENT admission
- ToSyntax ctor first-stage OnEntry inline vs OnEntry{FirstStage} method (pre-existing dual UseThis lower; stop is method↔EntryExitBodies)

## Prior follow-ups (Razor @ 8d687912)

| Razor | This SHA | Disposition |
|-------|----------|-------------|
| F1 HostAbi `:309` false UseThis comment | still at `:309`; cache `:346` still false | **still open** — honesty, not fail-open |
| F2 Rewrite soft default `:958` | still `_ => node` | **still open** |
| F3 fallback BindModuleMethodBody `:762-770` | still no throw rewrite | **still open** |
| F4 name-collision extras.Add | still `:225-228` / `:237-240` | **still open** — not upgraded |
| F5 ClearEntryExitBody untested | `rg` callers still zero in tests | **still open** |

Razor verdict **ship** / stop **MET** — **confirmed**. No new F#.

## Process

- [ ] **P1** — Invariant-stating comments on bind wrappers must match the cache’s UseThis flag on **that** body (HostAbi `:309` vs `:300` vs `RuntimeAnalysisCache.cs:346`). Recurring class: comment claims export-shaped; residual twin is still Parameter-rooted.
