# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose:** Small, self-contained tasks for lower-context / cheaper agents.  
**Last Updated:** 2026-07-12  

## Operating rule (mandatory)

**Continue with In Progress tasks first.**  
When none are In Progress, take the **first Not Started** task from the **current focus suite**.

| Mark | Meaning |
|------|---------|
| `[ ] Not Started` | Ready to pick when prior tasks Done |
| `[~] In Progress` | **Work these first** |
| `[x] Done` | Closed |
| **Skip / Superseded** | Do not execute |

---

## Current focus (July 2026) — USE THIS

### Vertical slices → M2 product-complete

**Suite index (pick order lives here):**  
**[`vs-README.md`](vs-README.md)**

**Parent plan:** [`../vertical-slice-finish-plan.md`](../vertical-slice-finish-plan.md)

| Slice | Goal | Status |
|-------|------|--------|
| **0** Honesty | Fail-loud, tool honesty, PolicySubject, EmitInvoke | ✅ Done |
| **1** Structure | Verify path + pin Person | ✅ Done |
| **2** Policy API | Subject + true/false + domain-attached e2e | ✅ Done |
| **3** Policy MCP | add_policy → evaluate_policy → smoke | ✅ Done |
| **M2** | First consumer vertical slice | ✅ **Done** (1175 green) |

**Do not pick Slice 4/5** unless an orchestrator reopens them.

**Next (post-M2):** see [`vs-README.md`](vs-README.md) — prefer  
1. [`vs-pm2-evaluate-policy-sample-bag.md`](vs-pm2-evaluate-policy-sample-bag.md) multi-property sample  
2. [`vs-pm2-add-policy-evaluate-affordance.md`](vs-pm2-add-policy-evaluate-affordance.md) affordance  
3. Optional [`vs-s0-fail-loud-remove-zero-match.md`](vs-s0-fail-loud-remove-zero-match.md)  
4. Naming cleanup [`../../post-v2-delete-naming-cleanup.md`](../../post-v2-delete-naming-cleanup.md)

---

## Older suites (reference only)

### WS8 policy micro-tasks

[`ws8-README.md`](ws8-README.md) — Phase A Done; Phase B maps into **vs Slice 2–3**. Prefer **`vs-*`** when both exist.

### Cutover WP1–WP8

Historical; M1–M4 / V2 delete **Done**. See `wp1-*` … `wp8-*` only for provenance.

| Status | Tasks |
|--------|--------|
| Done | `wp1-*` … `wp4-*`, `wp6-*`, V2 delete |
| Superseded | `wp7-port-*`, old foundation `ws1-*` / `ws3-*` |

---

## Template

- Task template: [`TEMPLATE-micro-task.md`](TEMPLATE-micro-task.md)
- Summary template: [`../agent-summaries/TEMPLATE-task-summary.md`](../agent-summaries/TEMPLATE-task-summary.md)

After completing a task: summary in `../agent-summaries/`; update only the Status line on the task file; do **not** edit master roadmap unless the task is `vs-checkpoint-m2-close`.
