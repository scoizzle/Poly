# Domain Analysis Simplification — Agent Queue (`das-*`)

**Future state (acceptance target):** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md)  
**Present inventory / cutovers:** [`../domain-analysis-simplification.md`](../domain-analysis-simplification.md)  
**CORE:** [`../../CORE.md`](../../CORE.md)  
**Gate process:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**Suite gate:** [`./das-gate.md`](./das-gate.md)  
**Related:** DACR residual fallbacks (`dacr-*`); DAU product complete (`dau-*`)

Waves **W0–W4** implement the future-state plan. Inventory phases S0–S4 map 1:1 (S0→W0 … S4→W4).

---

## How to pick

1. First unchecked product task in pick order (W0 → W4). **Gates are optional bookkeeping — do not block implementation.**
2. Within a wave, prefer numbered order; soft deps in task files are guidance, not hard stops.
3. Update [`das-gate.md`](./das-gate.md) when convenient; full-send may land multiple waves before gate ceremony.
4. **Follow-ups go into docs** — reopen tasks or add `das-followups-*.md`; do not leave residual work only in chat.
5. Pre-ship review before claiming suite Done / merge.

### Workflow kickoff (copy)

```text
Execute the DAS suite starting at the first unchecked task in docs/plans/simple-agent-tasks/das-README.md.
Read docs/plans/domain-analysis-future-state.md for acceptance criteria.
Hard rules in das-README apply. Land follow-ups in docs. Run das-gate after each wave.
```

**Grok workflow:** `.grok/workflows/plan-orchestrator.rhai`

| Mode | Command / args |
|------|----------------|
| One task | `/plan-orchestrator` or `{"mode":"next"}` |
| Status only | `{"mode":"status-only"}` |
| **Until plan complete** | `{"mode":"until-done"}` |
| Cap iterations | `{"mode":"until-done","max_tasks":16}` (default 16, max 32) |
| Halt on verify fail | `{"mode":"until-done","stop_on_verify_fail":true}` (**default is false** — full-send continues) |
| Budget floor | `{"mode":"until-done","min_budget_remaining":8}` |

`until-done` loops: orient → implement → verify → record until suite complete, `max_tasks`, or agent budget low. **Does not stop on wave gates or soft prereqs.**

---

## Hard rules

| Rule | Why |
|------|-----|
| Analysis = facts + diagnostics only | No Syntax IR / C# / SQL mid-pipeline |
| One catalog end-state | No new parallel name→member indexes |
| Fail closed when analysis present | Missing required bag → loud error |
| Lowering / export depend on Analysis | Not the reverse |
| Prefer delete dual path over documenting it | Dual paths are debt |
| Do not mark Done without DoD + green build/tests | Pre-ship gate |
| Sibling-path check on dual-path fixes | Metadata + scan both covered until W4 |

---

## Wave status

| Wave | Theme | Task files | Status |
|------|--------|------------|--------|
| **W0** | Export boundary (projection out of analysis) | `das-w0-*` | `[x]` |
| **W1** | Single domain catalog | `das-w1-*` | `[~]` catalog dual-write landed |
| **W2** | One effective-action/policy surface | `das-w2-*` | `[~]` StageCapability preferred for effective policies |
| **W3** | Validate vs facts; honest deps | `das-w3-*` | `[ ]` |
| **W4** | Zero semantic dual paths | `das-w4-*` | `[~]` domain-bound runtime/lowering scans tightened |
| **Gate** | Suite completion | `das-gate.md` | `[ ]` |

---

## Task pick order

### Wave 0 — Projection boundary (P0)

| ID | File | Status | Prereq |
|----|------|--------|--------|
| **W0.1** | [`das-w0-1-remove-entity-syntax-pass.md`](./das-w0-1-remove-entity-syntax-pass.md) | `[x]` | — |
| **W0.2** | [`das-w0-2-export-projection-entry.md`](./das-w0-2-export-projection-entry.md) | `[x]` | W0.1 |
| **W0.3** | [`das-w0-3-metadata-provider-no-cast.md`](./das-w0-3-metadata-provider-no-cast.md) | `[x]` | W0.1 (can parallel W0.2) |
| **W0.G** | [`das-gate.md`](./das-gate.md) § Wave 0 | `[x]` | W0.1–W0.3 |

### Wave 1 — Catalog

| ID | File | Status | Prereq |
|----|------|--------|--------|
| **W1.1** | [`das-w1-1-catalog-design.md`](./das-w1-1-catalog-design.md) | `[x]` | — |
| **W1.2** | [`das-w1-2-catalog-publish.md`](./das-w1-2-catalog-publish.md) | `[x]` | — |
| **W1.3** | [`das-w1-3-catalog-consumers.md`](./das-w1-3-catalog-consumers.md) | `[~]` | lookups prefer catalog |
| **W1.4** | [`das-w1-4-retire-duplicate-indexes.md`](./das-w1-4-retire-duplicate-indexes.md) | `[ ]` | dual-write still on |
| **W1.G** | [`das-gate.md`](./das-gate.md) § Wave 1 | `[ ]` | optional |

### Wave 2 — Effective surface

| ID | File | Status | Prereq |
|----|------|--------|--------|
| **W2.1** | [`das-w2-1-unify-effective-surface.md`](./das-w2-1-unify-effective-surface.md) | `[~]` | StageCapability preferred |
| **W2.G** | [`das-gate.md`](./das-gate.md) § Wave 2 | `[ ]` | optional |

### Wave 3 — Validate / deps

| ID | File | Status | Prereq |
|----|------|--------|--------|
| **W3.1** | [`das-w3-1-declare-dependencies.md`](./das-w3-1-declare-dependencies.md) | `[ ]` | — |
| **W3.2** | [`das-w3-2-split-validation-facts.md`](./das-w3-2-split-validation-facts.md) | `[ ]` | — |
| **W3.G** | [`das-gate.md`](./das-gate.md) § Wave 3 | `[ ]` | optional |

### Wave 4 — Dual-path removal

| ID | File | Status | Prereq |
|----|------|--------|--------|
| **W4.1** | [`das-w4-1-runtime-no-fallback-scans.md`](./das-w4-1-runtime-no-fallback-scans.md) | `[~]` | domain-bound no scan |
| **W4.2** | [`das-w4-2-mcp-lowering-export-no-fallback.md`](./das-w4-2-mcp-lowering-export-no-fallback.md) | `[~]` | analysis-present resolve closed |
| **W4.3** | [`das-w4-3-marker-zero-and-dacr-close.md`](./das-w4-3-marker-zero-and-dacr-close.md) | `[ ]` | markers remain (evolution/export) |
| **W4.G** | [`das-gate.md`](./das-gate.md) § Wave 4 + suite | `[ ]` | optional |

---

## Done definition (suite)

1. All wave tasks `[x]` with progress notes.
2. `das-gate.md` suite section complete.
3. Build + tests green.
4. Future-state success picture items 1–6 in `domain-analysis-future-state.md` §11 checkable as met or explicitly deferred with ADR.
5. `DM-META-REMOVE-FALLBACK` count in DomainModeling semantic routes is **0** (W4).
6. No `EntitySyntaxPass` / `EntitySyntaxMetadata` mid-pipeline requirement for emit.

---

## Agent notes

- Prefer **minimal diffs** that move one DoD checkbox.
- After each task: update this table Status column and the task file.
- Do not start W1 product code until W0 gate is green (catalog on a broken projection story multiplies pain).
- Evolution live-batch resolution and standalone (`Domain == null`) reduced contracts are called out in future-state §5.1 / non-goals — do not expand scope into full peers without an ADR.
