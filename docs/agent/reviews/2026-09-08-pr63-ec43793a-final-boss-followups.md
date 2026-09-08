# Final Boss PR 63 follow-ups (re-verify) — 2026-09-08

Tip: `ec43793ae74f1e44c77b9b5bbbad51e5957c181c` · review: `docs/agent/reviews/2026-09-08-pr63-ec43793a-final-boss.md`  
Prior Razor (this tip): `docs/agent/reviews/2026-09-08-pr63-ec43793a-razor.md` + followups  
Prior not-ship: `335d8d63` · F1–F8 land: `4631157e` · Oracle tip: `ec43793a`  
Model: grok-4.6 · Mode: re-verify

## Open bugs (must close before ship)

None.

## Razor F1–F8 @ this tip (re-verified, not chain-trusted)

| F# | Disposition | Evidence (this SHA) |
|----|-------------|---------------------|
| **F1** Domain-bound miss throw | **closed** | DEI `:767-780` entry/exit; HostAbi sub `:302-308`; policy `:388-391`; residual Lower only `:775` / `:784` |
| **F2** Item3 cache-bind oracles | **closed** | Poison/clear/OnEntry/peer/idempotent tests in `Item3LowerPopulateTests.cs` (8/8 pass); empty notify-only skip `HostAbi.cs:285-287` |
| **F3** MaterializePeerInSyntax | **closed** | Walk TryCatchFinally `:441-447` + fail-loud default `:479-481`; `Subscription_PeerBinding_MaterializesInsideTryCatchFinally` |
| **F4** EvaluatePolicy no re-lower | **closed** | DEI `:388-391` throw; `DomainBound_MissingPolicyBody_Throws_DoesNotRelower` |
| **F5** watchedStageName / handlers / rename | **closed** | Param + handlers deleted (product grep empty); test `Subscription_FiresFromCachedBody_*` |
| **F6** PIPELINE-STATUS Item 3 line | **closed** | Residual sentence updated `:65` |
| **F7** IsUnsupportedPolicyStub | **closed** | Deleted (product grep empty) |
| **F8** SubscriptionHandlers comparer | **closed** | Handlers removed; bodies keep ReferenceEqualityComparer `:289` |

## Prior PR51 pipeline follow-ups (carried)

| F# | Disposition | Evidence |
|----|-------------|---------|
| PR51 **F3** EvaluatePolicy cache | **closed** this PR | PolicyBodies + fail-closed miss; Oracle tip attaches `_sim` for MCP |
| PR51 **F4** subs / transition batches | **closed** this PR (claimed residuals remain) | Nested flush + Domain-null LowerActionBody only |
| PR51 **F5** unbound core-catalog fallback | **still open** (out of scope) | Unchanged; PIPELINE-STATUS still names it |

## Tip Oracle (`ec43793a`)

| Item | Disposition |
|------|-------------|
| Attach `Policy("_sim")` on `Entity.Policies` | **ship glue** — `OracleTool.cs:406-412` still `EvaluatePolicy` → `TryGetPolicyBody`; no dual-path. OracleToolTests 16/16 pass |

## Open tasks (non-blocking)

- [ ] **F1** — Optional Item3 wrong-entity key oracle (Replace on entity A, evaluate entity B). File: `Item3LowerPopulateTests.cs`. Razor F1 still open; Final Boss did not find a cross-entity hit at this SHA.

- [ ] **F2** — Future: side-cache nested StageTransition partial flush so `DomainEntityInstance.cs:775` LowerActionBody can retire. Files: `HostAbi.cs:227-229`, `DomainEntityInstance.cs:773-775`.

## Process

- [x] **F9** (prior) — Item-N populate PRs gated on cache-identity / fail-closed miss tests — Item3 suite follows PR 51 F1 pattern (poison + clear throw). Keep that gate.

No new process task. Razor’s optional suggestions are not ship-blocking; this pass found no new bug.
