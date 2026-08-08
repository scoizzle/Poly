# Pipeline status

**Updated:** 2026-08-08

## Suites

| Suite | Status | Notes |
|-------|--------|-------|
| **gpure** (pure Grammar product path) | ✅ **DONE 2026-08-07** (+ post-gate follow-ups 2026-08-08) | Tasks 0–8 + gate; S1–S5/N1–N4/P1 all `[x]`; suite 1930 green. **No open gpure follow-up queue.** |
| **mcp-minify** | **CURRENT admit next** | JSON drop + unified add/remove — [`mcp-minify-README.md`](./mcp-minify-README.md) |
| mut-safety | Ready (after minify) | Session lock + idempotency |
| p1 (temporal) | Ready (after mut-safety) | Temporal pack; register patterns on both `expr-primary` + `expr-primary-no-not` (gpure inventory) |
| amu / p4 / coh / dogfood | Completed (earlier) | See archive |

## Notes

- gpure successor (not a suite task yet): drive live expr fold from `LeftAssoc` span tables (parent §8 / S5) — only when a consumer needs it.
- Span-vs-fold `not`-in-chain pinned: `SpanVsFold_NotInChain_TableRejectsFoldAccepts` + inventory §A1.
