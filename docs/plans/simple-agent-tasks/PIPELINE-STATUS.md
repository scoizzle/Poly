# Pipeline status

**Updated:** 2026-08-08  
**Authority:** this file is the **sole CURRENT/DONE truth** for agent suite admission.  
Other indexes must **mirror** this file (or link here) — do not invent a second CURRENT line.

---

## Agent pick (one line)

```text
DONE:    gpure (2026-08-07 + follow-ups 08-08); mcp-minify (2026-08-08 + follow-ups)
CURRENT: (none)
ADMIT:   mut-safety
THEN:    mut-safety → p1 temporal
PARKED:  outbox lock; multi-assembly DM; actors/schedule; LeftAssoc live-fold (gpure S5)
PULL:    E5; EF codegen; naming cleanup; Validation/Text delete (see dead-dual inventory)
```

```bash
# When human admits next suite:
copilot --agent plan-suite-until-done -p "Suite: mut-safety. Mode: until-done."
```

---

## Suites

| Suite | Status | Notes |
|-------|--------|-------|
| **gpure** | ✅ **DONE** 2026-08-07 (+ follow-ups 2026-08-08) | Pure Grammar product path (Option A ladder + tables); S1–S5/N1–N4/P1 closed. |
| **mcp-minify** | ✅ **DONE** 2026-08-08 (+ follow-ups same day) | Catalog 46→24; DSL-only expressions; unified `add`/`remove`; follow-ups closed. |
| **mut-safety** | **Admit next** (not CURRENT until human admits) | Session lock + idempotent add + rollback DX |
| **p1** temporal | Ready after mut-safety | Patterns on both `expr-primary` + `expr-primary-no-not` |
| amu / p4 / coh / dogfood / GI / … | Archived | See `docs/plans/archive/` |

---

## Related

| Doc | Role |
|-----|------|
| [`READY-TO-TASK.md`](./READY-TO-TASK.md) | Ready-suite index (mirrors this) |
| [`../v2-to-v3/master-roadmap.md`](../v2-to-v3/master-roadmap.md) | Milestone index + Agent pick (mirrors this) |
| [`../README.md`](../README.md) | Plans admission rules (points here for CURRENT) |
| [`../dead-dual-inventory-2026-08-08.md`](../dead-dual-inventory-2026-08-08.md) | Validation / Text / second-evaluator kill list |

## Notes

- gpure successor (not a suite): live fold from `LeftAssoc` span tables — only with a consumer.
- Span-vs-fold `not`-in-chain pinned: `SpanVsFold_NotInChain_TableRejectsFoldAccepts`.
