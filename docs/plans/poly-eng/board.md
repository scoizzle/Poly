# Poly board: the one live queue

**Bot pipeline suspended 2026-10-10 21:33 CT; Scot runs slices from a local agent per [LOCAL-AGENT.md](LOCAL-AGENT.md).**

Updated 2026-10-16. Master: `ff83c598` (#191). Rules: [README](README.md). Old board with its run log and parked bug cards: [archive/board-2026-10-06.md](archive/board-2026-10-06.md). [LOCAL-AGENT.md](LOCAL-AGENT.md) §3 is the 2026-10-10 handoff; this table wins when they disagree.

**CURRENT:** [`pipeline-convergence-plan.md`](../../domain-modeling/pipeline-convergence-plan.md), trimmed to the DEI-retirement path (finish line C8d + C10, Scot 2026-10-09 18:56 CT).
**THEN:** platform-contract residual F8; MCP mut-safety; grammar wrap-up; V3 naming. **PULL:** E5; EF codegen; naming cleanup.
**Throttle / merge:** not applicable while the bot pipeline is suspended; Scot merges.

## Needs Scot

1. C8d depends on T3, which the 2026-10-09 trim parked. Un-park T3 or drop it from C8d's Depends.
2. C8-web and C8-clean are parkable, but C8d (and C8-num, for C8-clean) depend on them. Parking one means editing those Depends lines.
3. **F31 stopped NOT SHIP (`slice/F31`, `27d49b79`, no PR).** The card's DSL is reproduced exactly; F26 clears the first stop, but a typed `create in` with a computed value still dies on `Member 'IsSuccess' is not resolved.` at `DirectVmAbiEmitter.EmitMember` (`Poly/Interpretation/Vm/DirectVmAbiEmitter.Expressions.cs:401`), compiling the `if (!create.IsSuccess)` guard from `LowerCreateNavCall` (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:961`). Isolation: `create in sessions { }` and `{ ExpiresAt: Now }` run; only `ExpiresAt: Now + 30 Minutes` fails. The scalar-vs-reference question (card) is open, and the S8 row of `docs/probes/findings/scratch-2026-10-14.md` fails the same way from a different trigger (a target entity with a `value`-typed property). Decide: fix `LowerCreateNavCall`'s result guard as its own slice, or park F31.

## Demo

The product demo is `scripts/live-demo.sh`: compile the warehouse domain and walk create, get, and register-truck. `scripts/serve-poly.sh` is the same host left in the foreground. Both pass `--http` (CompileMode.All does not seed `http`) and pin `SQLitePCLRaw.lib.e_sqlite3` 2.1.13 so the generated project clears NU1903. `demo/README.md` matches that.

The next demo-quality gap after the walk is green is C8-web D3 and D4 (duplicate routes, child GET ignores the id). It stays at row 10 because the crm-dslcompiler goldens also change with C8-num. Do not pull it forward unless Scot says the API lie matters more than that churn.

## In flight (open PRs; GitHub is the live state)

| PR | Slice | Branch |
|----|-------|--------|
| #192 | docs: propose a multi-vendor storage layout | `docs/multi-vendor-storage` |
| #193 | docs: propose async/await semantics for the interpreter | `docs/async-await-interpreter` |
| #194 (draft) | C8-tc2: typed probe + real `EnsureUnique` — plan commit only so far | `slice/c8-tc2` |

## Next (in order; first unblocked row wins)

Details, dependencies, done tests and risk for each row: [LOCAL-AGENT.md](LOCAL-AGENT.md) §3. Rows 1–3b have merged; the first unblocked DEI row is 4. The swarm lane below is a separate queue. It does not move row 4.

| # | Slice | Depends / hold | Task file |
|---|-------|----------------|-----------|
| 1 | C6c | **merged** `2baec149` (#157) | [tasks/C6c.md](tasks/C6c.md) |
| 2 | C7d | **merged** `ef8f267b` (#159) | [tasks/C7d.md](tasks/C7d.md) |
| 3 | C8-tc1a | **merged** `86148036` (#162). Absorbed tc1b (`this.Create{Nav}`, probes deleted). #164 closed. Void-context linking stays an open decision. | [tasks/C8-tc1a.md](tasks/C8-tc1a.md) |
| 4 | C8-tc2 | C8-tc1 merged. **In flight** on `slice/c8-tc2` (#194 draft; the board refresh was its plan commit). **Narrowed:** real `EnsureUnique` only. Do not restore probes. `StoreBind.cs` is `StoreJobObjectType` and `TypedNull`. | [tasks/C8-tc2.md](tasks/C8-tc2.md) |
| 5 | C8-tc3 | C8-tc2 | [tasks/C8-tc3.md](tasks/C8-tc3.md) |
| 6 | C8-vm | False positives and three real bugs merged (#165 `a0c68e64`, #163 `29411bd7`). Still open: `Emit` does not call `ThrowIfVmHasErrors`. Re-check the rest of the task before starting. After C8-tc2. | [tasks/C8-vm.md](tasks/C8-vm.md) |
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

## Swarm (probe findings; one card per agent)

Offload queue for the open items in [`docs/probes/findings/evolve-2026-10-14.md`](../../probes/findings/evolve-2026-10-14.md). F27, F32, F33, and F34 are already on master (`06844e39`). **Nine of the eleven cards merged (`#182`–`#191`); F31 stopped NOT SHIP (Needs Scot 3) and F35b is blocked, so this queue is drained — no card here is startable.** U1 and U2 (printed `EnsureUnique`) stay on row 4. Rows 5–19 and THEN are unchanged.

[`docs/probes/findings/scratch-2026-10-14.md`](../../probes/findings/scratch-2026-10-14.md) (`#179`, the Oct-14 from-scratch probe) holds S1–S31: ten are fixed on master, S14 is covered by F28 (`#183`), and the rest are **evidence, not cards**, until Scot admits them.

Branch each card from current origin/master. One branch `slice/<id>`, one PR, leave it open. Scot merges. Take one start-now card whose files no other open PR is editing. A sequenced card starts only after every predecessor in its Depends line has merged.

Each task file has the edit, the test, the Leave list, and the stop condition. Stop and report when the card says stop.

| When | Card | Edit | Test class | Task |
|------|------|------|------------|------|
| merged | F29 | **merged** `efcbf53a` (#182) | `EntityLevelActionHintTests` | [tasks/F29.md](tasks/F29.md) |
| merged | F26 | **merged** `86ec8ba9` (#184) | `PolicyNegativeOffsetTests` | [tasks/F26.md](tasks/F26.md) |
| merged | F35a-check | **merged** `09fe6fc8` (#185) | `DateDifferenceDurationTests` | [tasks/F35a-check.md](tasks/F35a-check.md) |
| merged | F30a | **merged** `bdfbe1dd` (#186) | `SumParseTests` | [tasks/F30a.md](tasks/F30a.md) |
| merged | F28 | **merged** `0d7c1b13` (#183) | `CreateInWatchedStageTests` | [tasks/F28.md](tasks/F28.md) |
| blocked | F35b | assign target is not on the check scope | — | [tasks/F35b.md](tasks/F35b.md) |
| **NOT SHIP** | F31 | F26 cleared the card's first stop; the typed `create in` still dies on the result guard. Reproducer pinned on `slice/F31` (`27d49b79`), no PR. See Needs Scot 3. | `CreateInitializerDateTimeOffsetTests` | [tasks/F31.md](tasks/F31.md) |
| merged | F35a-lower | **merged** `6142ae58` (#188) | `DateDifferenceDurationTests` | [tasks/F35a-lower.md](tasks/F35a-lower.md) |
| merged | F30b | **merged** `9e3bd77c` (#189) | `SumAnalysisTests` | [tasks/F30b.md](tasks/F30b.md) |
| merged | F30c | **merged** `e38edcba` (#190) | `SumEvalTests` | [tasks/F30c.md](tasks/F30c.md) |
| merged | F30d | **merged** `ff83c598` (#191) | guide text | [tasks/F30d.md](tasks/F30d.md) |

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
| 10-15 | #191 | F30d: document `sum Rel Prop` in the DSL guide | `ff83c598` | see PR |
| 10-15 | #190 | F30c: evaluate `sum Rel Prop` | `e38edcba` | see PR |
| 10-15 | #189 | F30b: analyze `sum Rel Prop` | `9e3bd77c` | see PR |
| 10-15 | #188 | F35a-lower: lower date-minus-date to Subtract | `6142ae58` | see PR |
| 10-15 | #186 | F30a: parse and print `sum Rel Prop` | `bdfbe1dd` | see PR |
| 10-15 | #187 | fix: a subscribed create-in has no previous stage | `17d6f212` | see PR |
| 10-15 | #181 | demo: Poly IDE UI (MAUI Blazor Hybrid + Blazor Server) | `9e8b3207` | see PR |
| 10-15 | #179 | probe: from-scratch discovery 2026-10-14 (S1-S31) + two fixes | `a5eaedfb` | see PR |
| 10-15 | #185 | F35a-check: allow a date property minus a date property | `09fe6fc8` | see PR |
| 10-15 | #184 | F26: classify an unresolved temporal method as a heap value | `86ec8ba9` | see PR |
| 10-15 | #183 | F28: notify once when create-in lands in the watched stage | `0d7c1b13` | see PR |
| 10-15 | #182 | F29: hint when an entity-level action sits beside stages | `efcbf53a` | see PR |
| 10-15 | #180 | docs: queue the probe-finding swarm | `ddc90e52` | see PR |
| 10-14 | #178 | grammar: C# language surface over Poly.Grammar (read + write) | `ef5ad9f0` | see PR |
| 10-14 | #177 | fix: run the printed Create factory in the interpreter | `6c1bede3` | see PR |
| 10-14 | #176 | F26-F35 fixes: F32 coercion, F27 single-fire, F33 M:N export, F34 guide | `06844e39` | see PR |
| 10-14 | #175 | mission: real-app core utilities 2026-10-14 | `14849ff1` | see PR |
| 10-14 | #174 | MCP2: harness DI, per-session locks, idle eviction, one tool boundary | `d84995d6` | see PR |
| 10-14 | #173 | mission: evolutionary fact-finding 2026-10-13 | `929a2080` | see PR |
| 10-14 | #172 | CPM1: central package management via Directory.Packages.props | `bd416609` | see PR |
| 10-14 | #171 | MCP1: upgrade ModelContextProtocol to 2.2.0 | `d47cab42` | see PR |
| 10-13 | #169 | demo: serve the warehouse host | `485c3cac` | see PR |
| 10-13 | #168 | docs: Poly Agent Skill | `89fa8eb5` | see PR |
| 10-13 | #167 | docs: ontology-of-ontologies gap map | `e758d14c` | see PR |
| 10-12 | #162 | C8-tc1a: typed by-type create lowering (absorbed tc1b) | `86148036` | see PR |
| 10-12 | #165 | C8-vm: analyzer false positives V1 V3 V5 | `a0c68e64` | see PR |
| 10-12 | #163 | C8-vm: unknown assign names, create-in params, grandchild parents | `29411bd7` | see PR |
| 10-12 | #157 | C6c: Unlink cannot drop below a required minimum | `2baec149` | see PR |
| 10-10 | #155 | C8-tc: typed create in the compiled tree (plan) | `a6203103` | see PR |
| 10-10 | #158 | plan: C8-vm task file | `dafa6a72` | see PR |
| 10-10 | #159 | C7d: One-offs and BehaviorMetadata | `ef8f267b` | see PR |
| 10-10 | #160 | docs: local-agent handoff | `6665bf04` | see PR |
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
