# Razor PR 63 follow-ups (re-verify) — 2026-09-08

Tip: `ec43793ae74f1e44c77b9b5bbbad51e5957c181c` · review: `docs/agent/reviews/2026-09-08-pr63-ec43793a-razor.md`  
Prior not-ship: `335d8d63` · F1–F8 land: `4631157e` · Oracle tip: `ec43793a`

## Prior ship-gate dispositions (F1–F8 @ 335d8d63)

| F# | Disposition | Evidence |
|----|-------------|---------|
| **F1** Domain-bound miss throw | **closed** | DEI `:767-780`; HostAbi sub `:302-308`; residual Lower only `:775` / `:784` |
| **F2** Item3 cache-bind oracles | **closed** | Poison/clear/OnEntry/peer/idempotent tests in `Item3LowerPopulateTests.cs`; empty notify-only skip `HostAbi.cs:285-287` |
| **F3** MaterializePeerInSyntax | **closed** | Walk TryCatchFinally + fail-loud default; `Subscription_PeerBinding_MaterializesInsideTryCatchFinally` |
| **F4** EvaluatePolicy no re-lower | **closed** | DEI `:388-391` throw; `DomainBound_MissingPolicyBody_Throws_DoesNotRelower` |
| **F5** watchedStageName / handlers / rename | **closed** | Param + handlers deleted; test `Subscription_FiresFromCachedBody_*` |
| **F6** PIPELINE-STATUS Item 3 line | **closed** | Residual sentence updated |
| **F7** IsUnsupportedPolicyStub | **closed** | Deleted (grep empty) |
| **F8** SubscriptionHandlers comparer | **closed** | Handlers removed; bodies keep ReferenceEqualityComparer |

## Prior PR51 pipeline follow-ups (carried)

| F# | Disposition | Evidence |
|----|-------------|---------|
| PR51 **F3** EvaluatePolicy cache | **closed** this PR | PolicyBodies + fail-closed miss; Oracle tip attaches `_sim` for MCP |
| PR51 **F4** subs / transition batches | **closed** this PR (claimed residuals remain) | Nested flush + Domain-null LowerActionBody only |
| PR51 **F5** unbound core-catalog fallback | **still open** (out of scope) | Unchanged |

## Tip Oracle (`ec43793a`)

| Item | Disposition |
|------|-------------|
| Attach `Policy("_sim")` on `Entity.Policies` | **ship glue** — same `TryGetPolicyBody`; no dual-path |
| Regression risk if Policies empty again | Covered by F4 throw; keep Entity.Policies non-empty for Domain-bound Oracle |

## Open tasks (non-blocking)

- [ ] **F1** — Optional Item3 wrong-entity key oracle (Replace on entity A, evaluate entity B). File: `Item3LowerPopulateTests.cs`.

- [ ] **F2** — Future: side-cache nested StageTransition partial flush so `:775` LowerActionBody can retire. Files: `HostAbi.cs:227-229`, `DomainEntityInstance.cs:773-775`.

## Process

- [x] **F9** (prior) — Item-N populate PRs gated on cache-identity / fail-closed miss tests — Item3 suite now follows PR 51 F1 pattern.
