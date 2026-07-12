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
| **0** Honesty | Fail-loud, tool honesty, PolicySubject, EmitInvoke | ✅ **Required Done** — optional 0.1d, 0.2a |
| **1** Structure | Verify path + pin Person/Order | 🟡 **1.1 Done**; **next 1.2** |
| **2** Policy API | Subject + true/false + domain-attached e2e | after 1.2 |
| **3** Policy MCP | add_policy → evaluate_policy → smoke | after Slice 2 |
| **M2** Docs checkpoint | Mark M2 closed | after Slice 3 |

**Do not pick Slice 4/5** (effects / relationships) unless an orchestrator reopens them.

**Next:** [`vs-s1-pin-canonical-entity.md`](vs-s1-pin-canonical-entity.md) **(1.2)** → Slice 2 `vs-s2-*`.  

Optional polish: **0.2a** README row (still “Places an existing action”); **0.1d** remove-zero-match.

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
