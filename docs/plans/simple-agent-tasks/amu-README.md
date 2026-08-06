# Analysis Metadata Utilization — Agent Queue (`amu-*`)

**Parent / orientation:** [`../domainmodeling-cohesion-and-metadata-findings.md`](../domainmodeling-cohesion-and-metadata-findings.md) §5  
**Related:** archived DACR/DAS (catalog monopath already shipped); residual analysis-consuming-lowering  
**Gate:** [`amu-gate.md`](./amu-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**CORE:** [`../../CORE.md`](../../CORE.md)  

**Status:** Ready suite — **not CURRENT**. Admit only after dogfood-wave-2 clears (or explicit master-roadmap pick).

---

## Objective

Make analysis a **pipeline of facts**, not a fan-out of independent domain IR walks. Wire existing bags into peer analyzers, lowering residuals, and MCP projection. Prefer delete dual scans over new metadata types.

---

## How to pick

1. First `[ ]` in wave order (W0 → W4).  
2. Within W1, agents may claim **one task each** if file ownership disjoint.  
3. Do not start W2 until W1 product ACs met (soft: W0 inventory done).  
4. Pre-ship review before suite Done.

### Workflow kickoff

```text
suite=docs/plans/simple-agent-tasks/amu-README.md  mode=next
# or mode=until-done after CURRENT admits amu
```

---

## Hard rules

| Rule | Why |
|------|-----|
| No new parallel name→member indexes | Catalog is sole product map |
| Fail closed when analysis present | Missing required bag → loud error |
| Prefer catalog helpers | `DomainSemanticLookupExtensions` over `Relationships.FirstOrDefault` |
| No second MCP fact store | Project `LatestAnalysis` only |
| Do not invent bags without a consumer in the same wave | Utilization first |
| File ownership | Respect per-task edit lists |

---

## Wave status

| Wave | Theme | Tasks | Status |
|------|--------|-------|--------|
| **W0** | Live inventory (publish × consume × residual scans) | `amu-w0-inventory` | `[ ]` |
| **W1** | Catalog-only name resolve in semantic lints | `amu-w1-1`…`w1-3` | `[ ]` |
| **W2** | Honest Dependencies; Storage←EntityStructure | `amu-w2-1` | `[ ]` |
| **W3** | Lowering residual metadata lookups | `amu-w3-1`, `amu-w3-2` | `[ ]` |
| **W4** | MCP structured facts expansion | `amu-w4-mcp-facts` | `[ ]` |
| **Gate** | Suite close | `amu-gate` | `[ ]` |

---

## Task pick order

| ID | File | Size | Soft prereq | Status |
|----|------|------|-------------|--------|
| **W0** | [`amu-w0-inventory.md`](./amu-w0-inventory.md) | M | — | `[ ]` |
| **W1.1** | [`amu-w1-1-effect-analyzer-catalog.md`](./amu-w1-1-effect-analyzer-catalog.md) | M | W0 | `[ ]` |
| **W1.2** | [`amu-w1-2-policy-analyzer-catalog.md`](./amu-w1-2-policy-analyzer-catalog.md) | M | W0 | `[ ]` parallel W1.1 |
| **W1.3** | [`amu-w1-3-subscription-catalog.md`](./amu-w1-3-subscription-catalog.md) | M | W0 | `[ ]` parallel W1.1 |
| **W2.1** | [`amu-w2-1-deps-and-storage-structure.md`](./amu-w2-1-deps-and-storage-structure.md) | M | W1 | `[ ]` |
| **W3.1** | [`amu-w3-1-exporter-residual.md`](./amu-w3-1-exporter-residual.md) | M | W1 | `[ ]` |
| **W3.2** | [`amu-w3-2-effect-lowering-residual.md`](./amu-w3-2-effect-lowering-residual.md) | M | W1 | `[ ]` parallel W3.1 |
| **W4** | [`amu-w4-mcp-facts.md`](./amu-w4-mcp-facts.md) | M | W2 soft | `[ ]` |
| **G** | [`amu-gate.md`](./amu-gate.md) | S | W0–W4 | `[ ]` |

---

## Agent pick (when CURRENT)

```text
DONE:    (none on amu)
CURRENT: first [ ] in table above
PARK:    domain→AST program bridge; new metadata types without consumers
```

---

## Do not pick

| Item | Why |
|------|-----|
| Re-open DAS/DACR suites | Already monopath for product catalog |
| Temporal / P4 / cohesion | Separate suites |
| Grammar re-base | Not required |

---

## Done definition (suite)

1. All tasks `[x]` with notes.  
2. Gate checks complete.  
3. Build + tests green.  
4. No new product dual path for name resolve when analysis present.  
