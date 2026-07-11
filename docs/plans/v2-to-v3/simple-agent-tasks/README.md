# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose**: Small, self-contained tasks for lower-context / cheaper agents.  
**Last Updated**: 2026-07-10 (WS8 A+ micro-tasks queued)

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
| **WS8 Phase A** Foundation (policy VM, honesty Path B) | ✅ Done |
| **WS8 Phase B** A+ agent loop | 🟡 6–6c Done; **next 6d + 6e–6h invariants** then MCP 7–11 |

### In Progress

_None._

### Next — WS8 Phase B (A+ package)

**Full index:** [`ws8-README.md`](ws8-README.md)

| Pri | Task | Goal |
|-----|------|------|
| **6–6c** | Spike + harden + demote Emit | ✅ Done |
| **6d** | [`ws8-invariant-policy-subject-types.md`](ws8-invariant-policy-subject-types.md) | **Next** — subject helper + defaults |
| **6e** | [`ws8-spike-bool-abi-adult-assert.md`](ws8-spike-bool-abi-adult-assert.md) | Adult assert: `bool true` **or** `1L` |
| **6f** | [`ws8-spike-matchnumeric-positive-control.md`](ws8-spike-matchnumeric-positive-control.md) | MatchNumeric true on working subject |
| **6g** | [`ws8-invariant-policy-property-name-alignment.md`](ws8-invariant-policy-property-name-alignment.md) | Expression/subject/domain name alignment |
| **6h** | [`ws8-invariant-no-dict-expando-subjects.md`](ws8-invariant-no-dict-expando-subjects.md) | Reject Dict/Expando at boundary |
| **7a** | [`ws8-mcp-add-policy-expression-contract.md`](ws8-mcp-add-policy-expression-contract.md) | Constrained expression contract |
| **7–9** | add_policy → evaluate_policy → MCP e2e smoke | Not started |
| **10–11** | polish + tool honesty invariant | Not started |

**Order:** **6d** (parallel **6e**, **6f**) → **6g/6h** → **7a → 7 → 8 → 9 → 10/11**.

### WS8 Phase A (done)

| Task | Status |
|------|--------|
| [`ws8-e2e-policy-vm-eval.md`](ws8-e2e-policy-vm-eval.md) | Done (incl. domain-attached) |
| [`ws8-domainexpression-lower-smoke-matrix.md`](ws8-domainexpression-lower-smoke-matrix.md) | Done* |
| [`ws8-policyevaluator-vm-primary.md`](ws8-policyevaluator-vm-primary.md) | Done* |
| [`wp5-optional-mcp-evaluate-policy.md`](wp5-optional-mcp-evaluate-policy.md) | Done Path B (`get_policy_expression`) |
| [`ws8-inventory-contract-interface-rules.md`](ws8-inventory-contract-interface-rules.md) | Done |

\*Optional polish in task #10.

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
