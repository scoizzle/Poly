# DomainModeling cohesion hygiene — Agent Queue (`coh-*`)

**Parent:** [`../domainmodeling-decomposition-proposal.md`](../domainmodeling-decomposition-proposal.md)  
**Orientation:** [`../domainmodeling-cohesion-and-metadata-findings.md`](../domainmodeling-cohesion-and-metadata-findings.md) §4  
**Gaps:** [`../domain-modeling-abstraction-gaps.md`](../domain-modeling-abstraction-gaps.md) (dispatch residual)  
**Gate:** [`coh-gate.md`](./coh-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  

**Status:** Ready suite — **not CURRENT**. Structural only: **no behavior change** except safer dispatch exhaustiveness. Admit on idle green tree or after dogfood; **do not** parallel with amu on same files without ownership check.

---

## Objective

Reduce cognitive surface of `Poly/DomainModeling/` via:

1. **Runtime/** folder for instance/store types  
2. Residual **DomainExpression** rewrites → `DomainExpressionDispatch`  
3. Residual **Effect** analysis switches → `EffectDispatch` where fit  
4. Evolution **mutation helpers** (dedupe ApplyTo / MutationContext)

**Non-goals:** multi-assembly split; Parsing→Poly/Dsl (later); create-in IR collapse (pull); product features.

---

## Parallel fan-out (after COH-0)

```text
Agent R → coh-r1 (Runtime/ folder)
Agent D → coh-d1 (DE dispatch rewrites)
Agent E → coh-e1 (Effect analysis dispatch)
Agent V → coh-v1 (evolution helpers)
```

One agent per chain; do not start r1/d1/e1/v1 until COH-0 locks file ownership.

---

## Hard rules

| Rule | Why |
|------|-----|
| Behavior-preserving | Tests green without new product claims |
| Name by type not Visit* | AGENTS naming |
| No multi-project | Single Poly project |
| File ownership | Chains do not edit each other’s primary files |
| CORE/AGENTS placement update | Same change as Runtime/ move |

---

## Task pick order

| ID | File | Stream | Size | Status |
|----|------|--------|------|--------|
| **0** | [`coh-0-design-locks.md`](./coh-0-design-locks.md) | Shared | S | `[ ]` |
| **R1** | [`coh-r1-runtime-folder.md`](./coh-r1-runtime-folder.md) | R | M | `[ ]` |
| **D1** | [`coh-d1-de-dispatch.md`](./coh-d1-de-dispatch.md) | D | M | `[ ]` |
| **E1** | [`coh-e1-effect-dispatch.md`](./coh-e1-effect-dispatch.md) | E | M | `[ ]` |
| **V1** | [`coh-v1-evolution-helpers.md`](./coh-v1-evolution-helpers.md) | V | M | `[ ]` |
| **G** | [`coh-gate.md`](./coh-gate.md) | Gate | S | `[ ]` |

---

## Agent pick (when CURRENT)

```text
NEXT: COH-0 then free chain heads R1|D1|E1|V1
```

---

## Done definition

1. Runtime types under `DomainModeling/Runtime/` (or documented defer if blocked).  
2. BindPeer/PreprocessQuantifiers share DomainExpressionDispatch (or measured residual).  
3. At least one Effect analysis walk uses EffectDispatch base.  
4. Evolution helpers reduce duplicated ReplaceInList patterns.  
5. Build + full suite green; CORE placement note if folders changed.  
