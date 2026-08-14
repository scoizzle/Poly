# P1 temporal pack — Agent Queue (`p1-*`)

**Parent lock:** [`../p1-temporal-design-lock.md`](../p1-temporal-design-lock.md)  
**Research:** [`../p1-temporal-research.md`](../p1-temporal-research.md)  
**Prereq:** Grammar GI + **E1** (`ExpressionFormRegistry`) **done**  
**Gate:** [`p1-gate.md`](./p1-gate.md)  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**Guide:** `Poly.Mcp/Docs/poly-dsl-guide.md`  

**Status:** **DONE — 2026-08-13 (p1-gate).** Phase 3a complete. All tasks 0–7 `[x]`; gate `[x]`. Full suite 2147/2147 green. Design-lock negatives covered by tests (incl. the Number-property date-operand gap fixed at the gate); guide documents temporal as shipped for authoring/analysis/round-trip with runtime clock eval explicitly NOT shipped (fixed `TimeProvider` seam is a recorded follow-up). P9 schedule NOT started.

---

## Objective

Ship **built-in temporal pack** vertical:

1. Author `Now`, `today`, and `N days` / `N months` via **E1 expression forms** (no core TokenKind forever).  
2. Resolve to existing `DateOperation` / clock-backed IR as locked.  
3. Policy compare + assign RHS goldens.  
4. Fail closed: unknown unit; pack-absent session.  
5. Guide honesty.

### Locks (from design lock — do not reopen)

| ID | Rule |
|----|------|
| T1 | Built-in pack on core seams; not forever hard-wired parser keywords for every unit |
| T2 | `DateOperation` stays resolved IR; generic lowering |
| T3 | Host clock: `TimeProvider` (CLR); injectable for tests |
| T4 | No `schedule at` (P9) |
| T5 | Unknown unit / missing pack → fail closed |
| T6 | Grammar patterns + print binders on both primaries (pack-host wave 1). `IExpressionPrimaryForm` only if an engine gap is cited. |

### Thin vertical success

```text
assign DueDate to Now - 12 days
→ DateOperation(Now, 12, AddDays) (or locked equivalent)
→ eval/export uses UtcNow.AddDays(-12) / TimeProvider

policy { ExpiryDate < Now }  with fixed clock → true/false

12 fortnights → error
```

---

## Task order

| ID | File | Size | Status |
|----|------|------|--------|
| **0** | [`p1-0-inventory-ir.md`](./p1-0-inventory-ir.md) | S | `[x]` |
| **1** | [`p1-1-now-expression.md`](./p1-1-now-expression.md) | M | `[x]` |
| **2** | [`p1-2-duration-forms.md`](./p1-2-duration-forms.md) | M | `[x]` |
| **3** | [`p1-3-pack-registration.md`](./p1-3-pack-registration.md) | M | `[x]` |
| **4** | [`p1-4-analysis-fail-closed.md`](./p1-4-analysis-fail-closed.md) | M | `[x]` |
| **5** | [`p1-5-goldens.md`](./p1-5-goldens.md) | M | `[x]` |
| **6** | [`p1-6-guide.md`](./p1-6-guide.md) | S | `[x]` |
| **7** | [`pack-3a-print-roundtrip.md`](./pack-3a-print-roundtrip.md) | M | `[x]` |
| **G** | [`p1-gate.md`](./p1-gate.md) | S | `[x]` |

### Kickoff

```bash
copilot --agent plan-suite-until-done -p "Suite: p1. Mode: until-done."
```

---

## Hard rules

| Rule | Why |
|------|-----|
| No schedule / business days / TZ | Pack-only later |
| No core TokenKind per unit | Open forms |
| Guide same PR as product claim | Honesty |
| Tests before Done | AGENTS §4 |

---

## Done definition

Design-lock appendix goldens green; guide lists temporal as shipped for the vertical; gate + suite green.  
