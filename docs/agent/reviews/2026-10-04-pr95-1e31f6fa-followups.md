# Follow-ups from the Final Boss review of PR 95 (A3a) at `1e31f6fa`

Companion to `2026-10-04-pr95-1e31f6fa-final-boss.md`. Finding numbers continue from F260. Nothing here is a bug in the code; the two blockers (F260, F261) are listed first because they must be fixed before ship.

## Must fix before ship (documentation, no code)

| ID | Who | What |
|---|---|---|
| F260 | author (hand) | `pipeline-convergence-plan.md:153`: the "Wrong today" clause "the compiled trees are one list split by entity name in `Emit`" is false after A3a (the split is in `Lower`/`RegisterTrees`). Fix the clause and the "rest of this paragraph still holds" sentence. |
| F261 | author (hand) | PR 95 body: replace "product: DomainSession.cs only", "LowerTreesTests (4 tests)", "3058", and the `FormatException` shape-matrix row with what the head does (3 product files, 9 tests, 3069, MCP refuses bad names at entry and Lower keeps the throw as backstop; colliding type names now throw `InvalidOperationException`). Use `gh pr edit`. |

## Small, can go in the same PR or a later one

| ID | Where | What |
|---|---|---|
| F262 | `UnifiedAddTests.cs` | Assert the error message in both new tests (`Message` contains "must be non-empty"). Mutation M13 shows the text is untested. |
| F265 | `RegisterTrees` | Test the "no type definitions for entity" throw or mark it defensive. Deleting it keeps every test green (M12). |
| F266 | `DomainSession.cs` property doc and `LowerToCatalog` comment | Say "Lower or Emit" replaces `ArtifactCatalog`; last writer wins under concurrency. |
| F267 | `DomainTools.cs`, `ArtifactId.cs` | Single-source the message wording (for example have `RequireValid` and `InvalidNameMessage` share one string), add one direct unit test of `IsValidPart`, and consider one helper for the two MCP checks (Razor F8). |
| F268 | `LowerTreesTests.cs` | Rename the concurrency test to say file names, or compare file contents. |
| F269 | `LowerTreesTests.cs`, `DomainSession.cs` | Rename the `DomainResult` test to the duplicate-type refusal (Razor F11); mention the new throws in the `Lower` doc. |

## Decisions for Scot or Foreman (separate slices)

| ID | Question |
|---|---|
| F263 | Which rule is the entry rule for entity and domain names: the ArtifactId rule only (today), or also the DSL identifier grammar? Today `add` accepts `1Order`, `Order-Item`, `A.B`; `export_dsl` prints them and `apply_dsl` cannot read them back. This is also Razor F10 for stage and property names. |
| F264 | Move the `DomainResult` / `{Other}Stage` collision from `Lower` to the analyzer (`StructuralDomainAnalyzer`) so `add` entity refuses it at mutation time (Razor F9). |
| Razor F8 | Validate in the C# API too (`DomainFactory.Create`, `EvolutionBuilder.AddEntity`, `McpSessionStore.Create`), or keep the `Lower` backstop and document it. |
| Razor F5 | `EmitGoldenTests.SampleDomains` is a hand-kept list of 10; `smoke.poly`, `demo/live/checkout.poly`, `demo/live/domain.poly` have no golden. Enumerate samples instead of listing them. |

## Note, not a finding

`RuntimeAnalysisCache.GetOrLower` reads `holder.Module` outside its lock and returns it with a second read while `Bind` can reset it under the lock. It can only race when different analysis objects are used for one `Domain`. Not in this diff, but it is the source of the exceptions Razor saw on master; H2 or K3 may want to fix it.

## Plan housekeeping for Foreman (after the merge)

- `pipeline-convergence-plan.md` line 3 "Done so far" lacks N3, C4a and A3a; add A3a with its merge SHA.
- Wave lists: mark A3a done; A3b (each tree points back to its source element) is next in lane A.
