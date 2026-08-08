# MCP Dogfood Queue (`dogfood-*`)

**Protocol:** [`../mcp-dogfood-protocol.md`](../mcp-dogfood-protocol.md)  
**Reports:** [`../agent-summaries/dogfood/`](../agent-summaries/dogfood/)  
**Fix pass (from findings):** [`dogfood-fix-README.md`](dogfood-fix-README.md)  
**Guide:** MCP `get_dsl_guide` / [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../../../Poly.Mcp/Docs/poly-dsl-guide.md)  
**Orientation:** [`../../domainmodeling-cohesion-and-metadata-findings.md`](../../domainmodeling-cohesion-and-metadata-findings.md) § trust / dogfood  

**Admitted:** 2026-08-06 — **Wave 2** is CURRENT (master-roadmap Agent pick).

---

## How to pick

### Discovery (scenarios)

1. Protocol §§2–5.  
2. First scenario with status `[ ]` in **Wave 2** pick order.  
3. MCP only; report required.  
4. **Do not fix platform** in a discovery turn.

### Fix pass (from findings)

1. Open [`dogfood-fix-README.md`](dogfood-fix-README.md) or add a new `dogfood-fix-*` for wave-2 findings.  
2. First fix task `[ ]`.  
3. Implement + test; one task per turn.  
4. Re-run the failed scenario after fix.

---

## Agent pick

```text
DONE:    Wave 1 S1/S2 PASS; owned-1/2/3; link-1/2/3; dogfood-fix G1/G3/HOST
CURRENT: Wave 2 discovery — first [ ] among S4 → S5 → S6
THEN:    Fix suite from S4–S6 findings; re-run; clear CURRENT
PULL:    S3 re-run optional; G2 optional; link-4/5 DSL deferred; dates/actors not dogfood
PARK:    Codegen / DAU / grammar / invent scenarios outside S4–S6
```

---

## Wave 2 — shipped SPE / peer / exists / owned (CURRENT)

| ID | File | Status | Concept |
|----|------|--------|---------|
| **S4** | [`dogfood-S4-peer-binder.md`](dogfood-S4-peer-binder.md) | `[ ]` | `when Rel Stage as name` peer binder e2e |
| **S5** | [`dogfood-S5-entity-level-when.md`](dogfood-S5-entity-level-when.md) | `[ ]` | Entity-level always-active `when` |
| **S6** | [`dogfood-S6-owned-exists-quantifiers.md`](dogfood-S6-owned-exists-quantifiers.md) | `[ ]` | Owned + store-aware exists + Q3′ quantifiers |

**Pick order:** S4 → S5 → S6. One scenario per session.

**Success of wave:** reports on disk for all three; blockers classified; fix tasks filed or PASS; master-roadmap CURRENT cleared or moved to forced feature suite.

---

## Wave 1 — historical

| ID | File | Status |
|----|------|--------|
| **S1** | [`dogfood-S1-library-checkout.md`](dogfood-S1-library-checkout.md) | `[x]` [S1](../agent-summaries/dogfood/DOGFOOD-S1-20260725.md) · [mut](../agent-summaries/dogfood/DOGFOOD-S1-MUTATION-FINDINGS-20260725.md) · [R](../agent-summaries/dogfood/DOGFOOD-S1-RERUN-20260725.md) · [R2](../agent-summaries/dogfood/DOGFOOD-S1-RERUN2-20260725.md) |
| **S2** | [`dogfood-S2-reassign-link.md`](dogfood-S2-reassign-link.md) | `[x]` [S2](../agent-summaries/dogfood/DOGFOOD-S2-20260725.md) · [R](../agent-summaries/dogfood/DOGFOOD-S2-RERUN-20260725.md) |
| **S3** | [`dogfood-S3-owned-profile.md`](dogfood-S3-owned-profile.md) | `[~]` ([report](../agent-summaries/dogfood/DOGFOOD-S3-20260725.md)) |
| **Synthesis** | [`DOGFOOD-SYNTHESIS-20260725.md`](../agent-summaries/dogfood/DOGFOOD-SYNTHESIS-20260725.md) | `[x]` — link S-tier later closed |

### Wave 1 findings → fix tasks (summary)

| ID | Issue | Task |
|----|--------|------|
| G1 | `simulate_policy` unknown prop → true | [fix-G1](dogfood-fix-G1-simulate-policy-fail-closed.md) |
| G3 | StoragePass noise on rollback | [fix-G3](dogfood-fix-G3-storagepass-rollback-noise.md) |
| G2 | policy expression AST dump | [fix-G2](dogfood-fix-G2-policy-expression-serialize.md) optional |
| B1 | `require not` negation bug | [fix](dogfood-fix-require-not.md) `[x]` |
| S1-B1 | invoke_action host-disabled | [HOST](dogfood-fix-HOST-enable-runtime-tools.md) |
| S1 runtime incomplete | re-run checkout | [S1-R](dogfood-fix-S1-rerun-checkout.md) |

Related: [`dogfood-link-README.md`](dogfood-link-README.md) (link-1–3 done; link-4/5 deferred).

---

## Rules (discovery)

| Rule | Detail |
|------|--------|
| MCP only | No DslCompiler / packs / C# store helpers |
| Guide first | `get_dsl_guide` |
| No concept dodge | Do not redesign away from peer / entity when / owned+exists |
| Capture | Report file required under `agent-summaries/dogfood/` |
| Classify | C/I/M/G/A/R/S/W |
| No platform fix | Discovery turn is report-only |

---

## Do not pick

| Item | Why |
|------|-----|
| Temporal / dates / actors / schedule | Not product claim — absorption/host later |
| Codegen / DAU / grammar | Parked |
| Fix during S4–S6 discovery | Confounds signal |
| Invent scenarios beyond S4–S6 | Wave 2 is fixed set |
| Parallel amu / cohesion / P* | One CURRENT only |

---

## Principles

- Domain fidelity over tool sprawl  
- Fail closed  
- Smallest fix from repeated **R/A/M/G** findings  
- Trust bar: dogfood steers next admit (amu, P4, temporal, or clear idle)  
