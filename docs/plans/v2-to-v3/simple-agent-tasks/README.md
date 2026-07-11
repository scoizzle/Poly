# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose**: Small, self-contained tasks for lower-context / cheaper agents.  
**Last Updated**: 2026-07-10 (WS8 code review — residuals In Progress)

## Operating rule (mandatory)

**Continue with In Progress tasks first.**  
When none are In Progress, take **Not Started** from the tables below.

| Mark | Meaning |
|------|---------|
| `[ ] Not Started` | Ready to pick |
| `[~] In Progress` | Review residual or partial ship — **work these first** |
| `[x] Done` | Closed (may note review caveats) |
| **Superseded** | Do not execute |

---

## Current Focus (July 2026)

**Completion plan:** [`../v3-completion-plan.md`](../v3-completion-plan.md)  
**Master roadmap:** [`../master-roadmap.md`](../master-roadmap.md)  
**WS8 suite:** [`ws8-README.md`](ws8-README.md)

| Milestone | Status |
|-----------|--------|
| **M1–M4** Cutover (V3 only, V2 deleted) | ✅ Done |
| **WS8 / WP5** Runtime truth | 🟡 Partial — review residuals open |

### In Progress first (code review 2026-07-10)

| Pri | Task | Residual |
|-----|------|----------|
| **1** | [`wp5-optional-mcp-evaluate-policy.md`](wp5-optional-mcp-evaluate-policy.md) | **Critical:** `evaluate_policy` claims VM true/false; only does metadata lookup — implement eval **or** rename/honest description |
| **2** | [`ws8-e2e-policy-vm-eval.md`](ws8-e2e-policy-vm-eval.md) | **High:** add DomainFactory → evolve `AddPolicyToEntity` → evaluate from domain graph test |

Do **not** start new Not Started work until these are Done.

### WS8 suite status

| Pri | Task | Status |
|-----|------|--------|
| 1 | [`ws8-e2e-policy-vm-eval.md`](ws8-e2e-policy-vm-eval.md) | [~] In Progress (domain-attach residual) |
| 2 | [`ws8-domainexpression-lower-smoke-matrix.md`](ws8-domainexpression-lower-smoke-matrix.md) | [x] Done* (lower inventory; VM gaps documented) |
| 3 | [`ws8-policyevaluator-vm-primary.md`](ws8-policyevaluator-vm-primary.md) | [x] Done* (VM-primary; optional README nit) |
| 4 | [`wp5-optional-mcp-evaluate-policy.md`](wp5-optional-mcp-evaluate-policy.md) | [~] In Progress (**honesty**) |
| 5 | [`ws8-inventory-contract-interface-rules.md`](ws8-inventory-contract-interface-rules.md) | [x] Done (docs spike) |
| Later | [`ws4-agent-trace-reading-guide.md`](ws4-agent-trace-reading-guide.md) | Polish |

\*See task file for review caveats / optional follow-ups.

### Code review highlights (do not ignore)

- **MCP:** Tool descriptions must match behavior — no false “evaluates via VM.”
- **Tests:** Bare `Policy` + records ≠ domain-authored policy e2e.
- **Smoke matrix:** “Lowers without throwing” is gap inventory, not full VM coverage.

### Done (cutover)

| Task | Notes |
|------|--------|
| `wp1-*` … `wp4-*` | M2 direct API + MCP |
| `wp6-declare-v2-freeze.md` | Freeze declared |
| WP7/WP8 port/delete batches | **Superseded** by full V2 delete |

### Superseded

| Task | Why |
|------|-----|
| `wp7-inventory-*`, `wp7-port-*`, `wp8-delete-v2-gate-check` | V2 purged wholesale |
| `ws8-inventory-v2-contract-interface-rules.md` | Use `ws8-inventory-contract-interface-rules.md` |
| Old `ws1-*` / `ws2-research-*` / `ws3-add-*` | Foundation shipped long ago |

---

## Philosophy

- One task = one small, verifiable change.
- Prefer implementation + tests over design.
- DomainModeling never owns MCP workspace; MCP never owns domain rules.
- **Honesty over ship theater** — reopen Done tasks when review finds false claims.

## How to Use

1. Claim **In Progress** first (table above).
2. Follow Exact Steps + **Follow-ups** sections from code review.
3. Mark Done only when Verification checkboxes for residuals are complete.
4. File `agent-summaries/` on completion.

## Related

- `../v3-completion-plan.md`
- `../master-roadmap.md`
- `ws8-README.md`
- `../spikes/first-v3-consumer.md`
- `../spikes/mcp-guiding-principles.md`
