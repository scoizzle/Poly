# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose**: Small, self-contained tasks for lower-context / cheaper agents.  
**Last Updated**: 2026-07-10 (WP4 closed; next suite queued)

## Operating rule (mandatory)

**Continue with In Progress tasks first.**  
When none are In Progress, take **Not Started** in the priority table below.

| Mark | Meaning |
|------|---------|
| `[ ] Not Started` | Ready to pick |
| `[~] In Progress` | Finish follow-ups before anything else |
| `[x] Done` | Closed |

---

## Current Focus (July 2026)

**Completion plan:** [`../v3-completion-plan.md`](../v3-completion-plan.md)  
**M2 authoring path:** WP1–WP4 **Done** (MCP + direct API vertical slice).

### In Progress

_None — WP4 no-op residual closed (fingerprint guard + tests)._

### Next suite (pick in order)

| Pri | Task | Package | Goal |
|-----|------|---------|------|
| **1** | [`wp6-declare-v2-freeze.md`](wp6-declare-v2-freeze.md) | WP6 | Formal V2 freeze + inventory for port |
| **2** | [`wp7-inventory-v2-tests-and-demos.md`](wp7-inventory-v2-tests-and-demos.md) | WP7 | Prioritized port/delete list → `spikes/v2-port-inventory.md` |
| **3** | [`wp7-port-v2-tests-batch1.md`](wp7-port-v2-tests-batch1.md) | WP7 | First aggressive test port/delete batch |
| **4** | [`wp7-port-v2-demos-batch1.md`](wp7-port-v2-demos-batch1.md) | WP7 | One demo/benchmark off V2 |
| **5** | [`ws8-e2e-policy-vm-eval.md`](ws8-e2e-policy-vm-eval.md) | WP5/WS8 | Policy on domain + VM eval with C# record |
| **6** | [`ws8-domainexpression-lower-smoke-matrix.md`](ws8-domainexpression-lower-smoke-matrix.md) | WP5/WS8 | DE node smoke matrix |
| **7** | [`wp5-optional-mcp-evaluate-policy.md`](wp5-optional-mcp-evaluate-policy.md) | WP5 | MCP tool only if dogfood needs it |
| **8** | [`wp8-delete-v2-gate-check.md`](wp8-delete-v2-gate-check.md) | WP8 | Ready-to-delete? write readiness spike |
| Later | `ws4-agent-trace-reading-guide.md` | Polish | Trace UX docs |
| Later | `ws8-inventory-v2-contract-interface-rules.md` | Pull only | Contract gen prep |

### Done (M2 foundation)

| Task | Notes |
|------|--------|
| `wp1-v3-builtin-catalog.md` | DomainFactory + builtins |
| `wp1-sever-policyevaluator-v2.md` | V3-only PolicyEvaluator |
| `wp2-domain-query-projections.md` | DomainQueries |
| `wp2-direct-api-happy-path-tests.md` | Authoring path tests |
| `wp3-evolution-rollback-suite.md` | Rollback + no-op documented |
| `wp4-mcp-session-and-overview.md` | Session + structured queries |
| `wp4-mcp-evolve-tools.md` | Atomic evolve + no-op honesty |
| `wp4-retire-v2-domaintools.md` | V3 registration cliff |

**Skip:** all `ws1-*` / `ws2-research-*` / `ws3-add-*` — superseded.

---

## Philosophy

- One task = one small, verifiable change.
- Prefer implementation + tests over design.
- Port aggressively; delete redundant V2 tests.
- DomainModeling never owns MCP workspace; MCP never owns domain rules.

## How to Use

1. Claim one Not Started task from the **Next suite** table (top first).
2. Follow Exact Steps; leave status Done only when Verification is checked.
3. File `agent-summaries/` on completion.
4. Orchestrators update this README when a WP finishes.

## Related

- `../v3-completion-plan.md`
- `../master-roadmap.md`
- `../spikes/first-v3-consumer.md`
- `../spikes/mcp-guiding-principles.md`
