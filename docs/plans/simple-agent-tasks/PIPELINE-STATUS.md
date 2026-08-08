# Pipeline status

**Updated:** 2026-08-08

## Suites

| Suite | Status | Notes |
|-------|--------|-------|
| **gpure** (pure Grammar product path) | ✅ **DONE 2026-08-07** (+ post-gate follow-ups 2026-08-08) | Tasks 0–8 + gate; S1–S5/N1–N4/P1 all `[x]`; suite 1930 green. **No open gpure follow-up queue.** |
| **mcp-minify** | ✅ **DONE 2026-08-08** (follow-ups closed same day) | Catalog **46 → 24 tools**; zero `DomainExpressionJsonParser`; unified `add`/`remove` (kind+payload, policy remove supports stage/action scope) + `apply_dsl` only; DSL fragment API (`DslExpressionFragment.ParseExpressionFragment`); shared cursor base (`DslParseCursorBase`); oracle diet → one DSL expr oracle (`simulate_policy`); suite **1938 green**; review B1–B5/S1–S6/N1–N5/P1 all closed. [`mcp-minify-followups-2026-08-08.md`](./mcp-minify-followups-2026-08-08.md) |
| mut-safety | **CURRENT next** | Session lock + idempotency |
| p1 (temporal) | Ready (after mut-safety) | Temporal pack; register patterns on both `expr-primary` + `expr-primary-no-not` (gpure inventory) |
| amu / p4 / coh / dogfood | Completed (earlier) | See archive |

## Notes

- gpure successor (not a suite task yet): drive live expr fold from `LeftAssoc` span tables (parent §8 / S5) — only when a consumer needs it.
- Span-vs-fold `not`-in-chain pinned: `SpanVsFold_NotInChain_TableRejectsFoldAccepts` + inventory §A1.
