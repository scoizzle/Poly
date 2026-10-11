# Poly board: the one live queue

**Bot pipeline suspended 2026-10-10 21:33 CT; Scot runs slices from a local agent per [LOCAL-AGENT.md](LOCAL-AGENT.md).**

Updated 2026-10-10. Master: see GitHub (C7d #159 merged). Rules: [README](README.md). Old board with its run log and parked bug cards: [archive/board-2026-10-06.md](archive/board-2026-10-06.md).

**CURRENT:** [`pipeline-convergence-plan.md`](../../domain-modeling/pipeline-convergence-plan.md), trimmed to the DEI-retirement path (finish line C8d + C10, Scot 2026-10-09 18:56 CT).
**THEN:** platform-contract residual F8; MCP mut-safety; grammar wrap-up; V3 naming. **PULL:** E5; EF codegen; naming cleanup.
**Throttle / merge:** not applicable while the bot pipeline is suspended; Scot merges.

## Needs Scot

1. C8d depends on T3, which the 2026-10-09 trim parked. Un-park T3 or drop it from C8d's Depends.
2. C8-web and C8-clean are parkable, but C8d (and C8-num, for C8-clean) depend on them. Parking one means editing those Depends lines.
3. PR 157 (C6c) review is NOT SHIP (R1 major: `Remove` → `Unlink` throws on create-in rollback). Fix on `slice/c6c-plan`, then re-review.

## In flight (open PRs; GitHub is the live state)

| PR | Slice | Lane | Tip | CI | Next |
|----|-------|------|-----|----|------|
| [#157](https://github.com/scoizzle/Poly/pull/157) | C6c: Unlink cannot drop below a required minimum | B | `5cd51cd4` | green | Implemented; review NOT SHIP (R1 major, R2 nit). Fix, then re-review. |

## Next (in order; first unblocked row wins)

Details, dependencies, done tests and risk for each row: [LOCAL-AGENT.md](LOCAL-AGENT.md) §3.

| # | Slice | Depends / hold | Task file |
|---|-------|----------------|-----------|
| 1 | C6c | C6b (merged) | PR 157 branch |
| 2 | C7d | **merged** `ef8f267b` (#159) | [tasks/C7d.md](tasks/C7d.md) |
| 3 | C8-tc1a | C6a, C6b1 (merged) | [tasks/C8-tc1a.md](tasks/C8-tc1a.md) |
| 3b | C8-tc1b | C8-tc1a | [tasks/C8-tc1b.md](tasks/C8-tc1b.md) |
| 4 | C8-tc2 | C8-tc1 | [tasks/C8-tc2.md](tasks/C8-tc2.md) |
| 5 | C8-tc3 | C8-tc2 | [tasks/C8-tc3.md](tasks/C8-tc3.md) |
| 6 | C8-vm | C8-tc2 | [tasks/C8-vm.md](tasks/C8-vm.md) |
| 7 | C8-lc | C8-tc | [tasks/C8-lc.md](tasks/C8-lc.md) |
| 8 | C8-clean (optional before C8d) | C8-lc | [tasks/C8-clean.md](tasks/C8-clean.md) |
| 9 | C8-num | C8-clean | [tasks/C8-num.md](tasks/C8-num.md) |
| 10 | C8-web (optional; parkable) | C8-num | [tasks/C8-web.md](tasks/C8-web.md) |
| 11 | K2 (runs alone) | C0 (merged) | plan card |
| 12 | C8-pre (runs alone) | C2b, C7d | plan card |
| 13 | C8a1 | C5b, C6c, C7d | plan card |
| 14 | C8a2 | C8a1, C8-tc | plan card |
| 15 | C8b | C8a2, C8-pre | plan card |
| 16 | C8c1 | C8b | plan card |
| 17 | C8c2 | C8c1 | plan card |
| 18 | C8d (runs alone) | C8c2, T3 (parked; see Needs Scot), C8-lc/web/num/clean | plan card |
| 19 | C10 | C8d | plan card |

## Later (parked; not work until Scot admits it)

- N1 (decision 16), A6/A7 (wave 4), K3c/K3a/K3b/K5, R1, R2, and the rest of the 2026-10-09 trim list (F2-F6, H5, T3, Q1b). K2, C8-pre and C8d are now in Next.
- Nits to fold into a later docs slice: F170-F175 (C4a), F180-F183 (K1), F210-F212 (H1). The C8d done-when must include the MapModuleRequireFailure mapping.
- #126 `docs/plans/poly-eng/board.md`: in-flight row left at plan-only text / old tip after SHIP
- #128 `Poly/DomainModeling/Compile/DomainSession.cs`: `vm-analysis-report` ElementPath resolves to the domain name
- #128 `Poly.Tests/DomainModeling/Compile/VmAnalyzerReportTests.cs`: canary pins zero VM-clean/warning samples
- #128 `docs/plans/poly-eng/board.md`: in-flight row left at plan-only text after SHIP
- #129 `docs/plans/poly-eng/board.md`: in-flight row left at plan-only text / old tip after SHIP
- #129 `Poly/DomainModeling/Compile/DomainSession.cs`: comment "After the catalog is complete" overstates coverage (DslCompiler registers contributor files after Emit)
- #129 `docs/plans/poly-eng/tasks/H3.md`: shape-matrix cites `AnalysisReportArtifactTests`, which never inspects `catalog-reference-report`
- #131 `Poly.Tests/DomainModeling/Compile/EveryConceptHasTreeTests.cs`: constraint detection keys on exporter message fragments
- #131 `Poly.Tests/DomainModeling/Compile/EveryConceptHasTreeTests.cs`: `HandlerNames` re-implements `BuildHandlerNames`
- PR 133, `docs/plans/poly-eng/board.md:21`, in-flight row still showed tip `d0396038` and "Plan only".
- PR 133, `docs/plans/poly-eng/tasks/E1.md:29`, duplicate-name row cites `LowerTreesTests`; proof is `EnumStageNameCollisionTests`.
- PR 130, `docs/domain-modeling/pipeline-convergence-plan.md:395`, print still described as throwing `NotImplementedException`.
- PR 130, `docs/domain-modeling/pipeline-convergence-plan.decisions.md:132`, print still described as throwing `NotImplementedException`.
- PR 130, `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs:435`, hardcoded `adapterResult` local (duplicate var if a bound action parameter has that name).
- PR 132, `Poly.Mcp/Sessions/McpSessionStore.cs:96`, `Evolve` still `WithDomain`s; a uses change would reopen Core.
- PR 132, `Poly/DomainModeling/Compile/DomainSession.cs:146`, `WithDomain` still `Open(domain)` against Core when extensions change.
- #134 `docs/plans/poly-eng/board.md`: in-flight row left at tip `d1720b1d` after SHIP (head was `930dbecc`)
- #134 `docs/plans/poly-eng/tasks/E1.md`: unrelated #133 merged-doc edits rode on the C2a branch
- #134 `Poly/DomainModeling/Runtime/DomainEntityInstance.cs`: `_bindingTypeProvider` has no explicit reader after C2a helper deletions
- #134 `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs`: store-less create attaches the default store, so a later quantifier on that instance evaluates against the ad-hoc store instead of throwing
- PR 135, `Poly.Tests/DomainModeling/Lowering/MinimalApiGeneratorTests.cs:250`, comment still says DSL does not author `equals(...)`.
- PR 135, `docs/domain-modeling/pipeline-convergence-plan.md:333,359`, still say equality is DSL-unreachable / API-built only.
- PR 135, `Poly/DomainModeling/Language/DomainDslPrinter.cs:758,809`, `0.0###############` rounds tiny doubles to `0.0` and is duplicated in `PrintLiteral` / `PrintLiteralValue`.
- PR 135, `Poly/DomainModeling/Language/PolyDslParser.cs:1417`, API-built `EqualityConstraint(null)` prints `equals(null)` which the parser rejects.
- PR 135, `docs/plans/poly-eng/tasks/P1.md`, Log attributes all R1-R5 fixes to `40b1e4f0` and omits `8dc1fc93` / `38f0fe9a`.
- PR 137, `docs/plans/poly-eng/board.md:21`, in-flight row left at plan-only text / empty Tip after SHIP.
- PR 137, `docs/plans/poly-eng/tasks/K6.md:46`, Log attributes the inline sweep to `835ccf6a` and omits `d1270cdc` / `d3eac5a8`.
- Suites on THEN/PULL live in [`../parked/`](../parked/README.md).

## Merged (last 30 days; older lines move to archive/)

| Date (CDT) | PR | Slice | Merge SHA | Reviewed |
|------------|----|-------|-----------|----------|
| 10-10 | #156 | C7c: Entity and relationship lookups use index and navigation | `78231090` | see PR |
| 10-10 | #152 | C5b: Multi-hop, depth and fan-out leave the store | `60089ef0` | see PR |
| 10-10 | #153 | C7b: Three action-by-name lookups use the index | `67427b72` | see PR |
| 10-10 | #151 | C6b1: Link cardinality as a tree | `931b1461` | see PR |
| 10-10 | #150 | C7a: Six 'get compiled module' sites use the catalog | `3b53f0fa` | see PR |
| 10-10 | #149 | C5a: Store fan-out runs the compiled Notify tree | `fa994ff7` | see PR |
| 10-10 | #147 | C7-0: Catalog index | `d344de77` | see PR |
| 10-10 | #146 | C4d: Stage transition table as a tree | `3fdb8bb5` | see PR |
| 10-10 | #148 | C3b: Delete MaterializePeerInSyntax | `c3b9d9d3` | see PR |
| 10-10 | #145 | C3a: Peer passed as a real argument | `019a7238` | see PR |
| 10-09 | #144 | C6a: Remove auto-link; linking is explicit | `7b6f69e2` | see PR |
| 10-09 | #141 | C4c: Constraints on set; enum value check | `cc5cd55a` | see PR |
| 10-09 | #143 | Q1a: Parity rows for collection rules (any/all/none/filtered count) | `914e5ff2` | see PR |
| 10-09 | #142 | N2b: Database libraries register their own DbContext artifact | `91012793` | see PR |
| 10-09 | #140 | F1: The function record | `a5530ba9` | see PR |
| 10-09 | #139 | C4b: Unique on create in the factory tree | `b894d9a6` | see PR |
| 10-09 | #138 | N2: Http library registers its own contributor | `592a137f` | see PR |
| 10-09 | #136 | C2b: Run the compiled Create checks; delete the C# twins | `10e6570a` | see PR |
| 10-09 | #137 | K6: replace Emit's re-analysis | `d1ac0cde` | Grug SHIP |
| 10-09 | #135 | P1: Equality constraint is authorable and printed | `b38817e9` | Grug SHIP |
| 10-09 | #134 | C2a: create initializers without re-lowering | `ede905f9` | Grug SHIP |
| 10-09 | #132 | Q2: MCP harness opens domains that use sqlite or http | `d30268ec` | Grug SHIP |
| 10-09 | #133 | E1: domain enums become real enum type definitions | `71fef08f` | Grug SHIP |
| 10-09 | #130 | C1c: unbound adapter result shape; last `BindForSimulate` arm | `4fc7a206` | Grug SHIP |
| 10-09 | #131 | H4: every concept has its tree (ratchet) | `86ca12dd` | Scot merged |
| 10-09 | #129 | H3: dangling and wrong-type references as diagnostics | `ec98f971` | Scot merged |
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
