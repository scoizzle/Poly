# Domain surface extensions — Agent Queue (`spe-*`)

**Parent plan:** [`../domain-surface-extensions-plan.md`](../domain-surface-extensions-plan.md)  
**Gate:** [`./spe-gate.md`](./spe-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**Related:** peer binding residuals (closed) · product guide `Poly.Mcp/Docs/poly-dsl-guide.md`

Three **parallel** workstreams after optional SPE-0. Agents may claim **one task per workstream** concurrently if different agents; single agent: pick any free chain head.

---

## How to pick

1. Prefer **first `[ ]` in any open workstream** (E / L / O are independent).  
2. Soft prereq SPE-0: read design locks in parent plan §4 (no need to “complete” SPE-0 if locks unchanged).  
3. Within a workstream, respect numbered order.  
4. Edit only files listed in the task (especially guide sections).  
5. Pre-ship review before marking suite Done.

### Parallel fan-out (workflow)

```text
Three agents in parallel:
  Agent E → spe-e1 (then e2, e3)
  Agent L → spe-l1 (then l2, l3)
  Agent O → spe-o1 (then o2, o3)
Do not start e2/l2/o2 until their workstream’s prior task is [x].
```

### Workflow kickoff (copy)

```text
# Prefer explicit suite key so plan-orchestrator does not default to DAS:
suite=spe  mode=until-done
# or:
suite=docs/plans/simple-agent-tasks/spe-README.md  mode=until-done

Parent plan: docs/plans/domain-surface-extensions-plan.md §4 design locks.
Parallel workstreams E / L / O. One task at a time per chain (or three agents on E1|L1|O1).
```

---

## Hard rules

| Rule | Why |
|------|-----|
| Fail closed | Missing peer/export metadata / missing store for owned → loud error |
| Guide honesty | Same change as behavior |
| No date work | Dates deferred (pack debate) |
| No link/unlink DSL | Explicitly parked |
| Sibling path | VM + export + analysis messages agree |
| File ownership | E/L/O chains do not edit each other’s primary production files |

---

## Workstream status

| Stream | Theme | Tasks | Status |
|--------|--------|-------|--------|
| **0** | Design locks (read-only) | `spe-0-design-locks.md` | `[ ]` optional |
| **E** | C# export peer handlers | `spe-e1`…`e3` | `[x]` E1–E3 done |
| **L** | Entity-level when dispatch | `spe-l1`…`l3` | `[x]` L1–L3 done |
| **O** | Owned policy evaluation honesty | `spe-o1`…`o3` | `[x]` O1–O3 done |
| **Gate** | Suite close | `spe-gate.md` | `[x]` |

---

## Task pick order (parallel heads first)

| ID | File | Stream | Size | Soft prereq | Status |
|----|------|--------|------|-------------|--------|
| **0** | [`spe-0-design-locks.md`](./spe-0-design-locks.md) | Shared | S | — | `[ ]` optional |
| **E1** | [`spe-e1-export-peer-handler-shape.md`](./spe-e1-export-peer-handler-shape.md) | E | M | §4 E | `[x]` verify pass (suggestion) 2026-08-02 |
| **L1** | [`spe-l1-entity-level-dispatch-plan.md`](./spe-l1-entity-level-dispatch-plan.md) | L | M | §4 L | `[x]` verify pass (none) 2026-08-02 |
| **O1** | [`spe-o1-owned-policy-inventory.md`](./spe-o1-owned-policy-inventory.md) | O | S | §4 O | `[x]` verify pass (none) 2026-08-02 |
| **E2** | [`spe-e2-export-peer-lowering.md`](./spe-e2-export-peer-lowering.md) | E | M | E1 | `[x]` verify pass (nit) 2026-08-02 |
| **L2** | [`spe-l2-entity-level-notify-runtime.md`](./spe-l2-entity-level-notify-runtime.md) | L | M | L1 | `[x]` verify pass (suggestion) 2026-08-02 |
| **O2** | [`spe-o2-owned-policy-eval-fix.md`](./spe-o2-owned-policy-eval-fix.md) | O | M | O1 | `[x]` verify pass (none) 2026-08-02 |
| **E3** | [`spe-e3-export-peer-tests-guide.md`](./spe-e3-export-peer-tests-guide.md) | E | S | E2 | `[x]` verify pass (none) 2026-08-02 |
| **L3** | [`spe-l3-entity-level-peer-and-guide.md`](./spe-l3-entity-level-peer-and-guide.md) | L | S | L2 | `[x]` verify pass (suggestion) 2026-08-02 |
| **O3** | [`spe-o3-owned-golden-and-guide.md`](./spe-o3-owned-golden-and-guide.md) | O | S | O2 | `[x]` verify pass (nit) 2026-08-02 |
| **G** | [`spe-gate.md`](./spe-gate.md) | Gate | S | E3+L3+O3 | `[x]` verify pass (nit) 2026-08-02 |

---

## Agent pick (one line)

```text
NEXT:     (none — SPE suite complete; gate G1–G6 [x])
```
