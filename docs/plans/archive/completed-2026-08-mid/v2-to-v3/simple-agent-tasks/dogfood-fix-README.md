# Dogfood Fix Pass Queue (`dogfood-fix-*`)

**Source findings:**  
- [`../agent-summaries/dogfood/DOGFOOD-S1-20260725.md`](../agent-summaries/dogfood/DOGFOOD-S1-20260725.md)  
- [`../agent-summaries/dogfood/DOGFOOD-S1-MUTATION-FINDINGS-20260725.md`](../agent-summaries/dogfood/DOGFOOD-S1-MUTATION-FINDINGS-20260725.md)  
- Wave 2: [`DOGFOOD-S4-20260806.md`](../agent-summaries/dogfood/DOGFOOD-S4-20260806.md) · [`DOGFOOD-S5-20260806.md`](../agent-summaries/dogfood/DOGFOOD-S5-20260806.md) · [`DOGFOOD-S6-20260806.md`](../agent-summaries/dogfood/DOGFOOD-S6-20260806.md)

**Discovery queue:** [`dogfood-README.md`](dogfood-README.md)

Fix product gaps found by dogfood. **Do not** mix into S2/S3 discovery runs.

---

## How to pick

1. First `[ ]` not marked optional/host-only.  
2. Open that task + Required Reading only.  
3. Exact Steps → Definition of Done → Verification.  
4. Flip status; set CURRENT next.  
5. **One fix task per turn.**

---

## Agent pick

```text
DONE:    G1, G3, HOST, G-S6-1; wave-2 discovery S4/S5/S6 PASS (reports on disk)
CURRENT: none — dogfood stage complete; master-roadmap CURRENT → amu
PULL:    G2 (optional); to-one link cardinality (S5/S6 M note, optional)
```

---

## Tasks (priority order)

| ID | File | From | Status | Diff |
|----|------|------|--------|------|
| **G1** | [`dogfood-fix-G1-simulate-policy-fail-closed.md`](dogfood-fix-G1-simulate-policy-fail-closed.md) | Mutation G1 — unknown prop → true | `[x]` | S–M |
| **G3** | [`dogfood-fix-G3-storagepass-rollback-noise.md`](dogfood-fix-G3-storagepass-rollback-noise.md) | Mutation G3 — StoragePass noise | `[x]` | S–M |
| **G-S6-1** | [`dogfood-fix-G-S6-1-bag-exists-fail-closed.md`](dogfood-fix-G-S6-1-bag-exists-fail-closed.md) | S6 — bag `Rel exists` fail-open | `[x]` | S–M |
| **G2** | [`dogfood-fix-G2-policy-expression-serialize.md`](dogfood-fix-G2-policy-expression-serialize.md) | Mutation G2 — AST dump | `[ ]` optional | M |
| **HOST** | [`dogfood-fix-HOST-enable-runtime-tools.md`](dogfood-fix-HOST-enable-runtime-tools.md) | S1-B1 invoke disabled | `[x]` | S |
| **S1-R** | [`dogfood-fix-S1-rerun-checkout.md`](dogfood-fix-S1-rerun-checkout.md) | S1 PARTIAL | `[ ]` after HOST | S |

---

## Finding → task map

| Finding | Task | Action |
|---------|------|--------|
| simulate_policy fail-open | **G1** | Product fix + test |
| StoragePass on rollback | **G3** | Filter/skip noise; keep D3.0 isolation fail-closed |
| get_policy_expression AST | **G2** | Optional JSON serialize |
| invoke_action disabled by user | **HOST** | Enable tools in MCP client — not Poly code |
| S1 incomplete runtime | **S1-R** | Re-dogfood after HOST |
| S6 bag-mode `Rel exists` → true (fail-open; guide promises throw) | **G-S6-1** | Bag path must throw for relationship-named exists (guide-consistent, fail-closed) + test |

---

## Rules

- Smallest fix + automated test  
- Do **not** remove StoragePass fail-closed for isolated/codegen paths (G3 is messaging / skip-on-structural-failure)  
- No DAU/codegen feature work  
- Check DoD boxes only after verify  

---

## After fixes

Return pick to **S2** on [`dogfood-README.md`](dogfood-README.md).  
