# Dogfood — S1-R: Library checkout re-run

**Date:** 2026-07-25
**Agent / session id:** Agent current / 64f9e1b964b847d38d78778f710f0f03
**Scenario file:** `simple-agent-tasks/dogfood-S1-library-checkout.md`
**Result:** FAIL (product blocker — not host disable)

## Executive (3 lines max)

- What worked: Domain authoring, instance creation, policy evaluation, structured facts. `invoke_action` now callable (HOST unblocked).
- First hard blocker: `require not AtLimit` guard blocks CheckOut even when AtLimit evaluates to `false`. The negation in `require not PolicyName` does not work correctly.
- Recommended product bucket: R (Runtime surprise) — guard negation semantics don't match policy evaluation.

## Success checklist

| # | Criterion | Met? |
|---|-----------|------|
| 1 | Domain applied via MCP with zero analysis errors | yes |
| 2 | export_dsl round-trips | yes (S1 initial run) |
| 3 | Create instances | yes (Book + Patron) |
| 4 | Invoke lifecycle action on Loan | no — CheckOut blocked by policy negation bug |
| 5 | evaluate_policy with instanceId | yes (GoodStanding passed, AtLimit correctly false) |
| 6 | get_domain_analysis usable with structured facts | yes |

## Timeline (short)

1. create_domain_session — success
2. apply_dsl with full library domain — zero analysis errors
3. create_instance Book (Gatsby) — success
4. create_instance Patron (Alice, Active stage) — success
5. evaluate_policy GoodStanding with instanceId — passed (true)
6. evaluate_policy AtLimit with instanceId — passed (false — 0 >= 5 is false, correct)
7. invoke_action CheckOut — FAILED: blocked by AtLimit guard despite AtLimit == false
8. Stopped: runtime semantics bug — did not attempt workaround

## Blockers

### B1 — `require not PolicyName` fails when PolicyName is false

| Field | Value |
|-------|--------|
| Bucket | R (Runtime surprise) |
| Score | F4 + B5 + N3 = 12 |
| Goal step | Step 7: invoke CheckOut action on Patron |
| Tried | Called invoke_action with Patron instance (Active stage, CurrentBorrowCount=0, MaxItems=5, Status=Active) and book parameter |
| Error / behavior | `"Action 'CheckOut' blocked by guards: AtLimit"` — but AtLimit evaluates to false (0 >= 5 is false) |
| Smallest product fix | Fix guard negation: `require not PolicyName` should succeed when PolicyName evaluates to false. The negation is lost or not applied in the guard evaluation pipeline. |
| Workaround | Redesign action to not use negation: use inverted policy or inline condition |

## What worked (keep)

- apply_dsl — zero errors on 3-entity domain with stages, policies, create-in effects
- get_domain_analysis — structured facts present (roots, actions, storage/transport)
- create_instance — correct property initialization and stage placement
- evaluate_policy with instanceId — correct results for both GoodStanding (true) and AtLimit (false)
- invoke_action — tool is now callable (HOST fix confirmed)

## Suggested backlog rows

| Priority | Bucket | One-line work item |
|----------|--------|--------------------|
| 1 | R | Fix `require not PolicyName` guard negation — negate policy result before checking |
| 2 | R | Add test: action with `require not AtLimit` on instance where AtLimit is false |

## Out of scope observed (do not act this session)

- Loop lifecycle actions (Renew, Return) not tested due to blocker
- Subscription fan-out not tested due to blocker
