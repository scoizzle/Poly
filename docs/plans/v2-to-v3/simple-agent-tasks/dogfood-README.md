# MCP Dogfood Queue (`dogfood-*`)

**Protocol:** [`../mcp-dogfood-protocol.md`](../mcp-dogfood-protocol.md)  
**Reports:** [`../agent-summaries/dogfood/`](../agent-summaries/dogfood/)  
**Fix pass (from S1 findings):** [`dogfood-fix-README.md`](dogfood-fix-README.md)  
**Guide:** MCP `get_dsl_guide` / [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../../../Poly.Mcp/Docs/poly-dsl-guide.md)

---

## How to pick

### Discovery (scenarios)

1. Protocol §§2–5.  
2. First scenario `[ ]`.  
3. MCP only; report required.  
4. **Do not fix platform** in a discovery turn.

### Fix pass (from findings)

1. Open [`dogfood-fix-README.md`](dogfood-fix-README.md).  
2. First fix task `[ ]`.  
3. Implement + test; one task per turn.

---

## Agent pick

```text
DONE:    S1 PASS, S2 PASS, owned-1/2/3
CURRENT: (all product blockers resolved — all three scenarios green)
THEN:    S3 re-run optional
PULL:    G2 optional, link-4/5 deferred
```

---

## Scenarios (wave 1)

| ID | File | Status |
|----|------|--------|
| **S1** | [`dogfood-S1-library-checkout.md`](dogfood-S1-library-checkout.md) | `[x]` [S1](../agent-summaries/dogfood/DOGFOOD-S1-20260725.md) · [mut](../agent-summaries/dogfood/DOGFOOD-S1-MUTATION-FINDINGS-20260725.md) · [R](../agent-summaries/dogfood/DOGFOOD-S1-RERUN-20260725.md) · [R2](../agent-summaries/dogfood/DOGFOOD-S1-RERUN2-20260725.md) |
| **S2** | [`dogfood-S2-reassign-link.md`](dogfood-S2-reassign-link.md) | `[x]` [S2](../agent-summaries/dogfood/DOGFOOD-S2-20260725.md) · [R](../agent-summaries/dogfood/DOGFOOD-S2-RERUN-20260725.md) |
| **S3** | [`dogfood-S3-owned-profile.md`](dogfood-S3-owned-profile.md) | `[~]` ([report](../agent-summaries/dogfood/DOGFOOD-S3-20260725.md)) |
| **Synthesis** | [`DOGFOOD-SYNTHESIS-20260725.md`](../agent-summaries/dogfood/DOGFOOD-SYNTHESIS-20260725.md) | `[x]` — next slice: link/unlink runtime |

---

## S1 findings → fix tasks (summary)

| ID | Issue | Task |
|----|--------|------|
| G1 | `simulate_policy` unknown prop → true | [fix-G1](dogfood-fix-G1-simulate-policy-fail-closed.md) |
| G3 | StoragePass noise on rollback | [fix-G3](dogfood-fix-G3-storagepass-rollback-noise.md) |
| G2 | policy expression AST dump | [fix-G2](dogfood-fix-G2-policy-expression-serialize.md) optional |
| B1 | `require not` negation bug | [fix](dogfood-fix-require-not.md) `[x]` — entity-level guard skip |
| S1-B1 | invoke_action host-disabled | [HOST](dogfood-fix-HOST-enable-runtime-tools.md) |
| S1 runtime incomplete | re-run checkout | [S1-R](dogfood-fix-S1-rerun-checkout.md) |

---

## Rules (discovery)

| Rule | Detail |
|------|--------|
| MCP only | No DslCompiler / packs |
| Guide first | `get_dsl_guide` |
| No concept dodge | |
| Capture | Report file required |
| Classify | C/I/M/G/A/R/S/W |

---

## Do not pick

| Item | Why |
|------|-----|
| Fix during S2/S3 discovery | Confounds signal |
| Codegen / DAU | Parked |
| Invent scenarios | |

---

## Principles

- Domain fidelity over tool sprawl  
- Fail closed  
- Smallest fix from repeated **R/A/M** findings  
