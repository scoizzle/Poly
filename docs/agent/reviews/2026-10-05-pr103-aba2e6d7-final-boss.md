# Final Boss review: PR 103 (M1, Analyze rejects cross-entity assign, DMEFF012) at aba2e6d7

- **PR:** https://github.com/scoizzle/Poly/pull/103 (`slice/m1` → `master`)
- **SHA reviewed:** `aba2e6d759aab84157ca3ce7bc7be556d954a226` (single product commit on merge base `076a9014`). Tip re-checked at review start and before posting; unchanged.
- **Master:** `dc7029d8` (4 commits ahead of merge base: T1 #98, N6 #100, N5 #99, B1b #102). Tip is BEHIND; `mergeable_state=behind`. GitHub compare tip↔master is `diverged` (ahead 1 / behind 4). File set does not overlap those master commits; Razor reported `git merge-tree` clean. Update-branch / rebase before land.
- **Role:** Final Boss (Pass B) after Razor SHIP. Mill lineage: hand/Grok implement → OpenCode Razor → Final Boss.
- **Mill (this pass):** Box Shell `/usr/bin/bash` ENOENT for the whole review window — could not run `mill.sh` / OpenCode locally. Spot-checked every Razor mill finding against tip source (EffectAnalyzer / SubscriptionAnalyzer / tests / stage-map). Razor's mill: OpenCode `opencode-go/deepseek-v4.1-flash`, SHIP, 0 bugs.
- **Suite:** CI Build & test **success** at tip (run [37265340164](https://github.com/scoizzle/Poly/actions/runs/37265340164)): **3084 passed / 0 failed** (duration ~1m 11s). Matches Razor oracle 3084/3084. Local tip/merged suite not re-run (Shell broken; box load high). No golden files in the diff.
- **Gate (hand-edit):** **YES.**

## Verdict: **SHIP**. Bugs introduced: **0**. Gate: **YES**.

Standing-merge eligible (SHIP + gate YES) through **2026-10-06 07:20 CDT**. Tip SHA for Chieftan squash-merge: `aba2e6d759aab84157ca3ce7bc7be556d954a226` (update-branch first).

## Scope vs card / PR body

| Claim | Diff |
|---|---|
| DMEFF012 Error for RelationshipNavigation assign targets | `EffectAnalyzer.ValidateAssign` + shared `ReportCrossEntityMutation`; `SubscriptionAnalyzer` `CrossEntityAssignRel` for `when` |
| V4=a: own-state in action/entry/exit/when stays clean | Positive test `OwnStateInActionEntryExitWhen_CreateIn_AndCrossEntityInvoke_HaveNoErrors` |
| Negatives: action, nested if, entry+exit, when | Four negative tests plant API-built `AssignEffect(RelationshipNavigation(...))` |
| Stage-map §2.4 notes what M1 enforces | One sentence appended; still says "does not enforce all of this yet" then lists today's rule |
| Parser cannot author nav assign; API-only plant | Honest; matches `PolyDslParser` assign → `PropertyAccess` only |
| Link/unlink gap; ParameterAccess early return | Admitted; not hidden |
| DMEFF012 may collide with PR 101 | FYI; second merge renumbers — do not hold |

Files: +170/−2 across 5 paths. No T1-frozen fixtures. No product path outside Analyze. Scope matches the M1 rule in the convergence plan ("Analyze Error for a mutation whose target is not the owner").

## Code check (independent)

1. **`ValidateAssign`** (EffectAnalyzer): top-level `ae.Target is RelationshipNavigation` → `ReportCrossEntityMutation` + return. Conditional/Composite already recurse via `EffectValidationDispatch`, so nested action/entry/exit assigns are covered without a special nest walk. Then existing `PropertyAccess` path unchanged. Non-PropertyAccess non-RelNav still early-returns (admitted ParameterAccess / OwnedAccess gap = Razor F1).
2. **`SubscriptionAnalyzer`:** on real subscriber RelNav with `isAssignTarget`, sets `CrossEntityAssignRel` and stops; reporter shared with EffectAnalyzer. Peer-binder l-value remains DMSS004 (pre-existing). Reads still walk inner.
3. **Message:** names relationship + owner entity; suggests `invoke rel.SomeAction`. Error severity via `ReportError` + `EffectCrossEntityMutation`.
4. **Simulate == print:** Analyze now blocks the API shape that lowering would emit as a cross-entity write (`this.customer.Balance = …`). DSL/MCP cannot build it; product path closed for RelNav. Remaining API-only wrap (OwnedAccess) is follow-up, not a ship blocker.
5. **Tests not vacuous (static):** each negative plants the exact RelNav assign and asserts `DMEFF012` count 1 or 2; positive asserts zero DMEFF012 and `HasErrors` false on own-state / create-in / invoke. Razor mutation (revert both analyzer files → 4 negatives red, positive green) accepted; could not re-run locally (Shell ENOENT).
6. **Cultures:** new tests are Analyze diagnostics only — no decimal/date formatting — no new culture risk. Pre-existing five comma-decimal failures unchanged ownership.
7. **Goldens:** untouched (no golden path in diff).

## Razor F1–F6 call (none block)

| id | Razor | Final Boss |
|---|---|---|
| F1 | suggestion: OwnedAccess→RelNav API target gets 0 errors | **Do not block** (Foreman). API-only; no DSL/MCP route. Follow-up: walk target for any RelNav; fail closed on other non-PropertyAccess shapes. Recoded as F380. |
| F2 | suggestion: pin composite / entity-level / when-nested-if | Do not block. Nice-to-have coverage. |
| F3 | nit: unknown-rel name still says "belongs to another entity" | Do not block. Still Error. |
| F4 | nit: message wording "Only 'Invoice' and its own actions…" | Do not block. Imprecise-but-true. |
| F5 | nit: peer-binder in when is DMSS004, not DMEFF012 | Do not block. Stage-map sentence is still true for RelNav assigns. |
| F6 | nit: pre-existing `lookup is null` silent return | Do not block. Pre-existing; not reachable via `DomainModelAnalyzer.Analyze`. |

## Findings (this pass)

| id | severity | note |
|---|---|---|
| F380 | suggestion (carry Razor F1) | `ValidateAssign` only matches top-level `RelationshipNavigation`. `OwnedAccess("doc", RelNav(...))` API plant → 0 errors. Follow-up slice; not ship-blocking. |
| F381 | nit (carry Razor F4) | Slight wording tighten on DMEFF012 message. |

No bugs. No false doc claim that flips the gate.

## Hand-edit gate: YES

- `ReportCrossEntityMutation` has two call sites; `CrossEntityAssignRel` written and read.
- No new fallback/legacy/interim branch.
- No renamed/removed symbol left dangling.
- Scot can open EffectAnalyzer/SubscriptionAnalyzer cold and change the RelNav check by hand.

Tenets: one rule at existing assign seams; closes a real Analyze hole before Compile trusts it; thin slice; shared reporter.

## Merge notes (for Scot / Chieftan)

- **Rebase/update-branch:** tip behind `dc7029d8`; do update-branch before squash-merge.
- **Risks:** DMEFF012 number collision if PR 101 lands first — renumber on the second merge; do not hold this PR.
- **After merge:** confirm Analysis suite still green; no golden regen expected.
- **Standing-merge:** SHIP + gate YES → eligible through 2026-10-06 07:20 CDT at tip `aba2e6d759aab84157ca3ce7bc7be556d954a226`.

## Limits of this review

- Local Shell ENOENT: no worktree, no local mill, no local mutation re-run, no culture re-run, no merged-tree suite. Evidence substituted: CI 3084/0, Razor oracle + mutation report, full tip source + diff read, GitHub compare vs `dc7029d8`.
