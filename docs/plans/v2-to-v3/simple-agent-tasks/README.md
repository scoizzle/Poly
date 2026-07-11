# Simple Agent Tasks (Micro-Tasks for Smaller Models)

**Purpose**: Small, self-contained tasks for lower-context / cheaper agents.  
**Last Updated**: 2026-07-10 (M1–M4 complete in tree; focus → WP5 runtime)

## Operating rule (mandatory)

**Continue with In Progress tasks first.**  
When none are In Progress, take **Not Started** from the **Next** table below.

| Mark | Meaning |
|------|---------|
| `[ ] Not Started` | Ready to pick |
| `[~] In Progress` | Finish follow-ups before anything else |
| `[x] Done` | Closed |
| **Superseded** | Leapfrogged by later work — do not execute |

---

## Current Focus (July 2026)

**Completion plan:** [`../v3-completion-plan.md`](../v3-completion-plan.md)  
**Master roadmap:** [`../master-roadmap.md`](../master-roadmap.md)

| Milestone | Status |
|-----------|--------|
| **M1** Foundation | ✅ Done |
| **M2** First consumer (direct API + MCP) | ✅ Done |
| **M3** V2 freeze | ✅ Done |
| **M4** V2 delete | ✅ Done in tree (`Poly/Data/Modeling` removed; V2 tests/demos/MCP DomainTools gone) |

### In Progress

_None._

### Next (pick in order)

| Pri | Task | Package | Goal |
|-----|------|---------|------|
| **1** | [`ws8-e2e-policy-vm-eval.md`](ws8-e2e-policy-vm-eval.md) | WP5 / WS8 | Policy on V3 domain + VM eval with C# record |
| **2** | [`ws8-domainexpression-lower-smoke-matrix.md`](ws8-domainexpression-lower-smoke-matrix.md) | WP5 / WS8 | DomainExpression lower/execute smoke matrix |
| **3** | [`wp5-optional-mcp-evaluate-policy.md`](wp5-optional-mcp-evaluate-policy.md) | WP5 | MCP `evaluate_policy` tool — only if dogfood needs it |
| **4** | [`ws4-agent-trace-reading-guide.md`](ws4-agent-trace-reading-guide.md) | Polish | Evolution trace guide for agents |
| Later | [`ws8-inventory-v2-contract-interface-rules.md`](ws8-inventory-v2-contract-interface-rules.md) | Pull only | Contract gen prep — only if a consumer demands it |

### Done (cutover complete)

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
| `wp6-declare-v2-freeze.md` | AGENTS + roadmap freeze |

### Superseded (do not run)

These were leapfrogged by a full V2 purge (delete rather than staged inventory/port):

| Task | Why |
|------|-----|
| `wp7-inventory-v2-tests-and-demos.md` | V2 tests/demos already removed |
| `wp7-port-v2-tests-batch1.md` | No V2 test tree left to port |
| `wp7-port-v2-demos-batch1.md` | V3 demos under `Poly/DomainModeling/Examples/Demos/`; old benchmarks demos deleted |
| `wp8-delete-v2-gate-check.md` | V2 core already deleted; gate was the delete |

Also skip: all old `ws1-*` / `ws2-research-*` / `ws3-add-*` foundation tasks.

---

## Philosophy

- One task = one small, verifiable change.
- Prefer implementation + tests over design.
- DomainModeling never owns MCP workspace; MCP never owns domain rules.
- No new V2 code — V2 is gone; do not resurrect `Poly/Data/Modeling`.

## How to Use

1. Claim one **Next** task (top first) unless an In Progress residual exists.
2. Follow Exact Steps; mark Done only when Verification is checked.
3. File `agent-summaries/` on completion.
4. Orchestrators keep this README aligned with the master roadmap.

## Related

- `../v3-completion-plan.md`
- `../master-roadmap.md`
- `../spikes/first-v3-consumer.md`
- `../spikes/mcp-guiding-principles.md`
