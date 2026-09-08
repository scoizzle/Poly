# Razor PR 63 follow-ups — 2026-09-08

Tip: `335d8d63d5b3866543e5c27ece4cf6f70e81c3fc` · review: `docs/agent/reviews/2026-09-08-pr63-335d8d63-razor.md`

## Prior dispositions (PR 51 pipeline follow-ups)

| F# | Disposition | Evidence |
|----|-------------|---------|
| PR51 **F3** EvaluatePolicy cache | **partially addressed / still open** | `PolicyBodies` + Prefer path landed; soft DomainExpression re-lower remains (`DomainEntityInstance.cs:397-416`); no cache-identity test |
| PR51 **F4** subs / transition batches at GetOrLower | **partially addressed / still open** | EntryExit + SubscriptionBodies populate; nested flush + Domain-null + soft miss still `LowerActionBody`; Item3 tests are outcome theater |
| PR51 **F5** unbound core-catalog fallback | **still open** (out of scope) | Unchanged this PR |

## Open tasks

- [ ] **F1** — Fail closed on Domain-bound cache miss: after `GetOrLower`, entry/exit/subscription lookup miss must throw (mirror named-action `:734-743`). Keep `LowerActionBody` only for explicit residuals (nested StageTransition partial flush without stage names; Domain-null). Files: `DomainEntityInstance.cs:771-776`, `DomainEntityInstance.HostAbi.cs:227-229`, `:321-327`.

- [ ] **F2** — Replace Item3 theater with cache-bind oracles: poison/replace `PolicyBodies` / `EntryExitBodies` / `SubscriptionBodies` (or assert node identity / no execute-time lower) so green requires the GetOrLower tree. Add OnEntry sibling; peer `as order` Materialize; empty notify-only; GetOrLower idempotent same references; wrong-entity key. File: `Item3LowerPopulateTests.cs`.

- [ ] **F3** — Complete or fail-loud `MaterializePeerInSyntax` (walk `TryCatchFinally` and reject unhandled nodes that still contain the peer binding). File: `DomainEntityInstance.HostAbi.cs:351-434`.

- [ ] **F4** — Domain-bound `EvaluatePolicy`: remove or fail-closed the DomainExpression re-lower fallback when `PolicyBodies` miss after GetOrLower. File: `DomainEntityInstance.cs:397-416`.

- [ ] **F5** — Drop or wire `watchedStageName` + unused `SubscriptionHandlers` execute path; rename `Subscription_FiresFromCachedHandler_*` to body-accurate name. Files: `HostAbi.cs:282`, `DomainInstanceStore.cs:531`, `Item3LowerPopulateTests.cs:70`.

- [ ] **F6** — Update `PIPELINE-STATUS.md` residual line for Item 3 (populate done; list explicit residual keys). File: `docs/plans/simple-agent-tasks/PIPELINE-STATUS.md:65`.

- [ ] **F7** — Delete dead `IsUnsupportedPolicyStub`. File: `DomainEntityInstance.cs:432-435`.

- [ ] **F8** — Align `SubscriptionHandlers` dictionary comparer with `SubscriptionBodies` reference equality when handlers become live. File: `RuntimeAnalysisCache.cs:312`.

## Process

- [ ] **F9** — Gate Item-N “populate at Lower” PRs on a cache-identity (or fail-closed miss) test pattern — same class as PR 51 F1 — so outcome-only greens cannot close the dual-path bug.
