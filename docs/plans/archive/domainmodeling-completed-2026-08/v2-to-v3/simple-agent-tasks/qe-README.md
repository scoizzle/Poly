# Query + Effect Suite — Simple Agents (`qe-*`)

**Parents:**  
- [`../dsl-query-surface.md`](../dsl-query-surface.md) — product design + shipped Q1′/Q3′  
- [`../effect-surface-completeness.md`](../effect-surface-completeness.md) — effects track  

**Status:** **Complete under current bar** (2026-07-24).  
**Audience:** Historical micro-task index. **Do not pick new work from open Q3 residual tables.**

> **Do not reopen Q0–Q3′ authoring from this queue.**  
> Product claims live in the parent plans + guides. Optional hygiene only if listed under Pull.

---

## Shipped (do not re-implement)

| Slice | Outcome | Commits / note |
|-------|---------|----------------|
| Q0–Q1′ | Subject-first path-prefix, `Rel exists`, `where`, anti-dot | through `25a79ec` |
| Q3′ | `any`/`all`/`none`/`count` DSL + analysis + store eval | `bb5032b` |
| Q3 residuals | Guide empty semantics; `evaluate_policy(instanceId=)`; MCP e2e | `85d28fe` |
| `link_instances` | Public MCP link + validation + smokes; E2.1′ | `7d067c0` |
| E2.1 | create-in only for DSL graph spawn; MCP link for existing instances (E2.1′) | effect decision log |

**Honest product claim:** Q1′ + Q3′ authorable and evaluable via store-linked `EvaluatePolicy` (`create_instance` → `link_instances` → `evaluate_policy(instanceId=…)`). DSL still has **no** `link` keyword — spawn-and-wire remains `create in Rel`. JSON policies remain local comparison-only.

---

## Agent pick

```text
DONE:    Q0–Q3′; link_instances MCP; E2.1′
CURRENT: (none in qe queue) — dogfood / next product pain
PULL:    Q4 aggregates; date ops; JSON quantifiers; unlink_instances; optional E1 hygiene
```

---

## Optional hygiene (parallel anytime; do not invent a new suite)

| ID | Work | File | Status |
|----|------|------|--------|
| **Q1'''''''.1** | Guide: nested path-prefix in where body | (inline if small) | `[ ]` Low |
| **E1′′′.1** | Guide: `delete` reserved keyword | [`qe-opt-e1-reserved-delete.md`](qe-opt-e1-reserved-delete.md) | `[ ]` Low |
| **E1′′′.3** | Fail-loud bad effect token error-string smoke | [`qe-opt-e1-bad-effect-token-test.md`](qe-opt-e1-bad-effect-token-test.md) | `[ ]` Low |

---

## Do not pick

| Item | Why |
|------|-----|
| Re-implement Q3′ IR/parser | Shipped `bb5032b` |
| Product dots / C# LINQ chains | Rejected product direction |
| Q4 / date ops without dogfood pain | Pull |
| Full JSON policy = DSL quantifiers | Documented split |
| Infrastructure IR Bar B | Infra track pull — [`../../infrastructure-pass-NEXT.md`](../../infrastructure-pass-NEXT.md) |

---

## Frozen product direction (still binding)

| Rule | Product form |
|------|----------------|
| **Anti-dot** | No `rel.Prop`, no `rel->Prop` |
| **Subject-first path-prefix** | `assignee Active`, `customer Tier is "VIP"` |
| **Postfix exists** | `assignee exists` |
| **Absence** | `not assignee exists` only |
| **`where`** | Scope keyword; no forced parens |
| **Quantifiers** | `any`/`all`/`none`/`count` **Rel where …** |
| **Cross-entity reads** | Legal in policies + scalar assign RHS |
| **Cross-entity writes** | Banned on assign target |

Depth: parent [`../dsl-query-surface.md`](../dsl-query-surface.md) §3.1 + §4.0.

---

## Micro-task files

Completed `qe-q0-*`, `qe-q1-*`, `qe-q3-r*`, `qe-e21-*` files remain in this folder for history (same pattern as [`vs-README.md`](vs-README.md)). **Do not execute them as open work.**  
Always-on process: [`pr1-uncommitted-review-gate.md`](pr1-uncommitted-review-gate.md).
