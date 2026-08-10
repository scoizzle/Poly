# Pipeline status

**Updated:** 2026-08-08  
**Authority:** this file is the **sole CURRENT/DONE truth** for agent suite admission.  
Other indexes must **mirror** this file (or link here) — do not invent a second CURRENT line.

---

## Agent pick (one line)

```text
DONE:    gpure (2026-08-07 + follow-ups 08-08); mcp-minify (2026-08-08 + follow-ups); grammar-revision (2026-08-09: v2 engine + DSL cutover + printer + review fixes); dead-dual cleanup (2026-08-09: Validation + Text.Matching deleted)
CURRENT: (none)
ADMIT:   grammar wrap-up — LeftAssoc live-fold + S1 reconciliation (gpure S5 successor, 2026-08-08 decision)
THEN:    grammar wrap-up → mut-safety → p1 temporal
PARKED:  outbox lock; multi-assembly DM; actors/schedule
PULL:    E5; EF codegen; naming cleanup
```

```bash
# When human admits next suite:
copilot --agent plan-suite-until-done -p "Suite: grammar-wrap. Mode: until-done."
```

---

## Suites

| Suite | Status | Notes |
|-------|--------|-------|
| **gpure** | ✅ **DONE** 2026-08-07 (+ follow-ups 2026-08-08) | Pure Grammar product path (Option A ladder + tables); S1–S5/N1–N4/P1 closed. |
| **mcp-minify** | ✅ **DONE** 2026-08-08 (+ follow-ups same day) | Catalog 46→24; DSL-only expressions; unified `add`/`remove`; follow-ups closed. |
| **grammar-revision** | ✅ **DONE** 2026-08-09 | v2 engine (`Grammar<TToken, TTokenKind>`, examine/consume, longest-match, stateless printer) + DSL cutover; review B1–B3/N1–N3/C1 closed. Executed directly (not via plan-suite) — see [`../grammar-revision.md`](../grammar-revision.md) |
| **grammar wrap-up** | **Admit next** (human decision 2026-08-08: wrap up Grammar before anything else) | LeftAssoc live-fold drives the expr ladder (gpure S5 successor); S1 span-vs-fold reconciliation; docs honesty — see [`../grammar-pure-end-state.md`](../grammar-pure-end-state.md) §4/§8 |
| **mut-safety** | Ready after grammar wrap-up | Session lock + idempotent add + rollback DX |
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
| [`../grammar-revision.md`](../grammar-revision.md) | ✅ **DONE 2026-08-09** — v2 engine + DSL cutover + printer + review fixes closed |

## Notes

- **grammar wrap-up** (admit next): LeftAssoc live-fold + S1 span reconciliation — product fold path.
- **grammar-revision** ✅ DONE 2026-08-09: v2 engine + DSL cutover + printer + review fixes (B1–B3, N1–N3, C1).
- Span-vs-fold `not`-in-chain pinned: `SpanVsFold_NotInChain_TableRejectsFoldAccepts` until wrap-up reconciles.
