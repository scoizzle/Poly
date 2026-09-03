# MCP Dogfood Report — 2026-07-18

## Executive recommendation
**Next product slice:** **Runtime MCP thin vertical** (CallAction + instance management via MCP)

**Why (top finding):** The highest-ranked finding (C9-F1, PainScore 18) is the **complete absence of runtime execution** through MCP. Models with lifecycle stages, actions, effects, and stage subscriptions can be authored and analyzed but **never exercised**. This is the fundamental blocker for any agent trying to validate spawn-and-wire behavior.

**Not next:** Effect micro-tools (C2-F5, Score 14). While the `AddActionWithEffect` naming is confusing, effect editing can be worked around once `AddEffectToAction` is known. The runtime gap (R) has no workaround in MCP.

## Ranked pains (Top 10)

| Rank | ID | Score | Cat | Title | Backlog bucket |
|------|-----|-------|-----|-------|----------------|
| 1 | C9-F1 | 18 | R | No CallAction tool in MCP | Runtime-MCP |
| 2 | C9-F2 | 16 | R | No instance management in MCP | Runtime-MCP |
| 3 | C2-F5 | 14 | W | `AddActionWithEffect` naming implies modification, creates duplicate | effect-micro |
| 4 | C9-F3 | 14 | R | Stage subscriptions in model only, no executor | Runtime-MCP |
| 5 | C2-F1 | 13 | A | Library evolution failed (duplicate action name) | other |
| 6 | C4-F3b | 13 | D | Missing relationship target throws FormatException | guide-honesty |
| 7 | C4-F4 | 13 | A | DMAS001 hints invisible through `GetAnalysisSummary` | other |
| 8 | C4-F1b | 12 | D | Lab construct 'actor' rejection message unclear | guide-honesty |
| 9 | C2-F6 | 12 | T | No unified "add effect to action" pattern in builder | effect-micro |
| 10 | C1-F4 | 0 | — | No pain found in C1 (batch DSL round-trip) | — |

## What worked (keep)

| Area | Detail |
|------|--------|
| **Batch DSL (C1)** | Parsing → evolution → printing → re-parse round-trip is **fully idempotent** for real-world 3-entity domains |
| **Export fidelity** | 491 chars round-tripped exactly through parse→print→re-parse cycle |
| **Expression lowering (C3)** | Both simple and composite expressions lower correctly to Syntax AST |
| **Policy evaluation (C3)** | `DomainEntityInstance.EvaluatePolicy` correctly evaluates all 3 property bags (T/F/T) |
| **Simulation parity (C3)** | Ad-hoc simulation matches entity-based evaluation — consistent results across all test cases |
| **Named policy `require`** | Guide-syntax `require Adult` parses and applies correctly |
| **Lab construct rejection** | `actor` keyword correctly rejected (message clarity is the only issue) |

## Coverage matrix

| Mission | Completed | Finding count | Pain findings | Notes |
|---------|-----------|---------------|---------------|-------|
| C1: Batch DSL | ✅ | 4 | 0 | All green — batch authoring is solid |
| C2: Micro incremental | ✅ | 4 | 3 | API naming confusion, duplicate action bug |
| C3: Oracle/policy | ✅ | 5 | 0 | All green — oracle/policy tools work correctly |
| C4: Repair/adversarial | ✅ | 4 | 3 | 3 pain points found (DSL clarity, suggestion visibility) |
| C9: Runtime gap | ✅ | 3 | 3 | Confirmed: no runtime execution available through MCP |

**Total:** 5 missions completed, 20 findings (9 pain, 11 OK)

## Category distribution

| Category | Count | Top pain |
|----------|-------|----------|
| **R** (Runtime) | 3 | No CallAction tool |
| **W** (Workflow) | 1 | AddActionWithEffect naming |
| **A** (Analysis) | 2 | Suggestion visibility |
| **D** (DSL/Guide) | 2 | Error message clarity |
| **T** (Tool gap) | 1 | Effect editing pattern |

## "Do not build" list

| Item | Reason |
|------|--------|
| **Effect micro-tools as next slice** | Not the highest pain — the runtime gap should be addressed first |
| **Guide/parser honesty fixes** | Real but low severity — error messages are functional, just not ideal |
| **Suggestion visibility** | Important but secondary to runtime execution from agent perspective |
| **Constraint remove MCP tool** | Not exercised in this dogfood; no evidence of pain yet |

## Evidence links

| Mission | Path |
|---------|------|
| C1 | `C1-dsl-batch-20260718.md` |
| C2 | `C2-micro-incremental-20260718.md` |
| C2 (supplementary) | `C2-supplementary-api-naming-20260718.md` |
| C3 | `C3-oracle-policy-20260718.md` |
| C4 | `C4-repair-adversarial-20260718.md` |
| C9 | `C9-runtime-gap-20260718.md` |
| Raw rollup | `dogfood-findings-20260718.json` |

## Detailed finding analysis

### R1: Runtime MCP gap (C9-F1, Score 18)
The most impactful issue. The entire `DomainEntityInstance`, `DomainInstanceStore`, action execution, and stage subscription machinery exists in the core library but has **zero MCP tooling**. Any agent wanting to create instances, call actions, or observe lifecycle transitions must write custom C# code. The model-only session is explicitly documented as honest, but the capability gap is severe.

### W1: API naming confusion (C2-F5, Score 14)
`AddActionWithEffect` sounds like it adds an effect to an existing action (following `AddStageTransitionEffect`'s pattern), but it actually creates a **new** action. This caused an evolution failure in the micro-incremental path. The correct method `AddEffectToAction` is easy to miss because it doesn't follow the naming pattern of other effect methods.

### A1: Suggestion invisibility (C4-F4, Score 13)
The `AuthoringSuggestionAnalyzer` correctly generates DMAS001 hint diagnostics (3 info-level diagnostics confirmed for stage-less + policy-relevant entity), but `GetAnalysisSummary` only surfaces errors and warnings. An MCP agent calling `get_domain_analysis` via the standard query path would **never see suggestions**.

### D1: Missing relationship target message (C4-F3b, Score 13)
N1 nav properties to non-existent entities throw a `FormatException` at parse time rather than producing an analysis diagnostic. The error message is clear ("references unknown entity") but the exception type is unexpected for what could be an evolution-time analysis finding.

### D2: Lab construct rejection message (C4-F1b, Score 12)
While `actor` is correctly rejected, the error reads "Expected Colon, got 'Patient' (Identifier)" rather than a clear "'actor' is not supported in Phase 1a (use 'entity' instead)". The `_unsupportedKeywords` check in the parser exists but may not include `actor`.

## Recommended next-slice rationale (per §9.1)

```text
IF top pain is R and dogfood needs instances in MCP → Runtime MCP thin vertical
```

Top pain IS R (PainScore 18). The rule fires: recommend **Runtime MCP thin vertical**.

This slice would add:
1. `create_instance` (create `DomainEntityInstance` from entity + property values)
2. `call_action` (execute an action on an instance, triggering effects + stage transitions)
3. `list_instances` / `get_instance` (inspect instance state)
4. `get_instance_stage` (read current stage of an instance)
5. Stage subscription evaluation (at minimum: declarative "would fire" preview; ideally: live fan-out)

**Estimated size:** Small-to-medium — the core library already has all the machinery
(`DomainEntityInstance`, `DomainInstanceStore`, `PolicyEvaluator`, effect types).
The work is wrapping these in MCP tools + session state management for instances.
