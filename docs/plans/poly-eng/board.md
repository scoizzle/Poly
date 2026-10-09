# Poly board: the one live queue

Updated 2026-10-09. Master `b1fddf7e` (#110 C1a). Rules: [README](README.md). Old board with its run log and parked bug cards: [archive/board-2026-10-06.md](archive/board-2026-10-06.md).

**CURRENT:** [`pipeline-convergence-plan.md`](../../domain-modeling/pipeline-convergence-plan.md), wave 1 tail.
**THEN:** platform-contract residual F8; MCP mut-safety; grammar wrap-up; V3 naming. **PULL:** E5; EF codegen; naming cleanup.
**Throttle:** full send, WIP 2 (one per lane), max 2 mill runs (Scot 2026-10-09 04:20).
**Merge:** V11 slices merge on Razor SHIP (CI green on the SHIP SHA); every other slice needs Scot's OK. The standing window ended 2026-10-06 07:20.

## Needs Scot

1. ~~Lift the throttle to WIP 2?~~ Answered 2026-10-07: stays at WIP 1.
2. HP4 release: once A5b merges, may lane A start C9, then Q2 / H2 / H3 / H4 / K4 / P1 (wave 2)?
3. ~~Post-merge review of #120 (L1) and #121 (G2+G3)?~~ Answered 2026-10-07: deferred until the throttle lifts.
4. Decision 19 (registering contributors) blocks N2. Decision 16 blocks N1. Not urgent.

## In flight (open PRs; GitHub is the live state)

| PR | Slice | Lane | Tip | CI | Next |
|----|-------|------|-----|----|------|
| [#130](https://github.com/scoizzle/Poly/pull/130) | C1c unbound adapter result shape; last `BindForSimulate` arm | B | `6e0529f9` | — | Plan only; implementation follows on this branch. [tasks/C1c.md](tasks/C1c.md) |
| [#127](https://github.com/scoizzle/Poly/pull/127) | C1b dedicated constraint-failure exception; delete `AsVoidResultBody` | B | `ba76d6b2` | — | In review. [tasks/C1b.md](tasks/C1b.md) |
| [#125](https://github.com/scoizzle/Poly/pull/125) | C9: generators stop asking for the module | A | `0757a400` | — | Implementer sweep done; ready for Razor on OpenCode. [tasks/C9.md](tasks/C9.md) |
| [#110](https://github.com/scoizzle/Poly/pull/110) | C1a: root program params after `this` | B | `d7b6b9a1` | **red** | Base `11287134` is 9 commits behind. 100x rebases onto master and fixes CI, then Razor review, then fix and Razor verify. Waits for WIP room while the throttle is on. [tasks/C1a.md](tasks/C1a.md) |
| [#128](https://github.com/scoizzle/Poly/pull/128) | H2: Emit refuses VM-analysis Errors and registers the report | A | `0ba89d58` | — | Plan only; implementation follows on this branch. [tasks/H2.md](tasks/H2.md) |

## Next (in order; first unblocked row wins)

| # | Slice | Lane | Depends / hold | Review |
|---|-------|------|----------------|--------|
| 1 | E1 domain enums become real enum types | B | C1c (wave 2) | 2 |
| 2 | C2a create initializers without re-lowering | B | C0, C1c (wave 2) | 2 |

Further order is §12 of the plan ("Full sequence"). Do not copy it here.

## Later (parked; not work until Scot admits it)

- N2 (decision 19), N1 (decision 16), A6/A7 (wave 4), and runs-alone slices K2, C8-pre, C8d, K5, R1, R2.
- Nits to fold into a later docs slice: F170-F175 (C4a), F180-F183 (K1), F210-F212 (H1). The C8d done-when must include the MapModuleRequireFailure mapping.
- Suites on THEN/PULL live in [`../parked/`](../parked/README.md).

## Merged (last 30 days; older lines move to archive/)

| Date (CDT) | PR | Slice | Merge SHA | Reviewed |
|------------|----|-------|-----------|----------|
| 10-09 | #127 | C1b: dedicated constraint-failure exception; delete `AsVoidResultBody` | `20fd11e0` | Scot merged |
| 10-09 | #128 | H2: Emit refuses VM-analysis Errors and registers the report | `a0f56f58` | Scot merged |
| 10-09 | #126 | K4: stage-scoped policies reach the printed output | `0ba89d58` | Scot merged |
| 10-08 | #110 | C1a: root module bodies use real SetArgs slots after this | `b1fddf7e` | Razor + Grug |
| 10-08 | #125 | C9: generators stop asking for the module | `99d97e46` | Razor |
| 10-08 | #124 | docs-restructure: one live board, pipeline doc, stale plans archived | `a13f1586` | Razor + Grug |
| 10-08 | #123 | A5b printed files reference their trees | `7943b5cc` | Scot merged |
| 10-07 09:22 | #122 | A5a contributors return artifacts | `bfe66beb` | verify on Grok (same mill as implement) |
| 10-06 08:02 | #117 | PR 113 post-merge fixes | `2931f01f` | Razor + Final Boss SHIP |
| 10-06 07:59 | #121 | G2+G3 MCP export / oracle_expression refuse errors | `9bbe7364` | **none** |
| 10-06 07:56 | #118 | F380b peer-binder hint, DMEFF012 scope | `0aec94f0` | Final Boss SHIP |
| 10-06 07:53 | #120 | L1 invariant culture | `1e9daece` | **none** |
| 10-06 07:50 | #119 | docs: Final Boss log rows F304/F380 | `af31f216` | docs |
| 10-06 06:49 | #115 | F380 assign target reaching another entity | `dc281be7` | Final Boss SHIP |
| 10-06 06:43 | #116 | F304 printer quotes contract source/version | `0257a06f` | Final Boss SHIP |
| 10-06 06:32 | #114 | poly-eng board and task files in the repo | `b480caaa` | Final Boss SHIP |
| 10-05 | #101 #104 #111 #112 #108 #105 #106 #113 #109 #103 #107 #102 | F292, C4e, A4, NU1903, G1, A3b, N4, PR113, B1c, M1, N7, B1b | see archive | mixed (#113 unreviewed, then fixed by #117) |
